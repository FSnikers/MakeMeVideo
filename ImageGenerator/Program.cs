using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageGenerator.Interfaces;
using ImageGenerator.Models;
using ImageGenerator.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ImageGenerator;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Генератор изображений через ChatGPT ===\n");

        // Настройка DI контейнера
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddSingleton<IConfigManager, ConfigManager>();
        services.AddSingleton<IPromptRepository, FilePromptRepository>();
        services.AddHttpClient<IImageGenerator, OpenAiImageGenerator>();

        var serviceProvider = services.BuildServiceProvider();

        var generator = serviceProvider.GetRequiredService<IImageGenerator>();
        var repository = serviceProvider.GetRequiredService<IPromptRepository>();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        var configManager = serviceProvider.GetRequiredService<IConfigManager>();

        // Проверка конфигурации
        if (!configManager.ValidateConfig())
        {
            Console.WriteLine("Ошибка: Проверьте настройки в appsettings.json");
            Console.WriteLine("Укажите ApiKey и ServerUrl");
            return;
        }

        // Выбор режима работы
        Console.WriteLine("Выберите режим:");
        Console.WriteLine("1. Загрузить промпты из prompts.json");
        Console.WriteLine("2. Добавить новый промпт");
        Console.WriteLine("3. Запустить генерацию всех ожидающих промптов");
        Console.Write("\nВаш выбор: ");

        var choice = Console.ReadLine();

        switch (choice)
        {
            case "1":
                await LoadPromptsFromFileAsync(repository);
                break;
            case "2":
                await AddNewPromptAsync(repository);
                break;
            case "3":
                await GenerateAllPendingPromptsAsync(generator, repository, configManager);
                break;
            default:
                Console.WriteLine("Неверный выбор");
                break;
        }

        Console.WriteLine("\nНажмите любую клавишу для выхода...");
        Console.ReadKey();
    }

    static async Task LoadPromptsFromFileAsync(IPromptRepository repository)
    {
        var promptsFile = "prompts.json";
        if (!File.Exists(promptsFile))
        {
            Console.WriteLine($"Файл {promptsFile} не найден. Создаю пример...");
            CreateExamplePromptsFile();
        }

        var json = await File.ReadAllTextAsync(promptsFile);
        var prompts = System.Text.Json.JsonSerializer.Deserialize<List<PromptData>>(json);

        if (prompts != null && prompts.Any())
        {
            await repository.AddPromptsAsync(prompts);
            Console.WriteLine($"Загружено {prompts.Count} промптов в репозиторий");

            Console.Write("\nЗапустить генерацию? (y/n): ");
            if (Console.ReadLine()?.ToLower() == "y")
            {
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddSingleton<IConfigManager, ConfigManager>();
                services.AddSingleton<IPromptRepository, FilePromptRepository>();
                services.AddHttpClient<IImageGenerator, OpenAiImageGenerator>();
                var sp = services.BuildServiceProvider();

                var generator = sp.GetRequiredService<IImageGenerator>();
                var configManager = sp.GetRequiredService<IConfigManager>();

                await GenerateAllPendingPromptsAsync(generator, repository, configManager);
            }
        }
    }

    static async Task AddNewPromptAsync(IPromptRepository repository)
    {
        Console.Write("Введите промпт: ");
        var promptText = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(promptText))
        {
            Console.WriteLine("Промпт не может быть пустым");
            return;
        }

        var prompt = new PromptData
        {
            Text = promptText,
            Status = "pending"
        };

        await repository.AddPromptAsync(prompt);
        Console.WriteLine($"Промпт добавлен с ID: {prompt.Id}");
    }

    static async Task GenerateAllPendingPromptsAsync(
        IImageGenerator generator,
        IPromptRepository repository,
        IConfigManager configManager)
    {
        var pendingPrompts = await repository.GetPendingPromptsAsync();
        pendingPrompts = pendingPrompts.Where(p => p.Status == "pending").ToList();

        if (!pendingPrompts.Any())
        {
            Console.WriteLine("Нет ожидающих промптов для генерации");
            return;
        }

        Console.WriteLine($"\nНачинаю генерацию {pendingPrompts.Count()} изображений...\n");

        var config = configManager.GetConfig();
        var semaphore = new SemaphoreSlim(config.MaxParallelRequests);
        var tasks = new List<Task>();

        foreach (var prompt in pendingPrompts)
        {
            await semaphore.WaitAsync();

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await repository.UpdatePromptStatusAsync(prompt.Id, "processing");

                    Console.WriteLine($"[{prompt.Id}] Генерация: {prompt.Text[..Math.Min(60, prompt.Text.Length)]}...");

                    var request = new GenerationRequest
                    {
                        Prompt = prompt.Text,
                        NegativePrompt = prompt.NegativePrompt,
                        Width = prompt.Parameters?.Width ?? config.ImageWidth,
                        Height = prompt.Parameters?.Height ?? config.ImageHeight,
                        Quality = prompt.Parameters?.Quality ?? config.Quality,
                        Model = config.Model
                    };

                    var result = await generator.GenerateImageAsync(request);

                    if (result.IsSuccess)
                    {
                        await repository.UpdatePromptStatusAsync(prompt.Id, "completed", result.ImagePath);
                        Console.WriteLine($"✅ [{prompt.Id}] Готово! Путь: {result.ImagePath}");
                    }
                    else
                    {
                        await repository.UpdatePromptStatusAsync(prompt.Id, "failed", errorMessage: result.ErrorMessage);
                        Console.WriteLine($"❌ [{prompt.Id}] Ошибка: {result.ErrorMessage}");
                    }
                }
                catch (Exception ex)
                {
                    await repository.UpdatePromptStatusAsync(prompt.Id, "failed", errorMessage: ex.Message);
                    Console.WriteLine($"❌ [{prompt.Id}] Критическая ошибка: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(tasks);
        Console.WriteLine("\n✅ Генерация завершена!");
    }

    static void CreateExamplePromptsFile()
    {
        var examplePrompts = new[]
        {
            new PromptData
            {
                Id = Guid.NewGuid().ToString(),
                Text = "A serene landscape with mountains and a lake at sunset, digital art style",
                NegativePrompt = "blurry, low quality, distorted",
                Parameters = new GenerationParameters { Width = 1024, Height = 1024, Quality = "standard" }
            },
            new PromptData
            {
                Id = Guid.NewGuid().ToString(),
                Text = "Cyberpunk city street at night with neon lights, anime style, vibrant colors",
                NegativePrompt = "photorealistic, daytime",
                Parameters = new GenerationParameters { Width = 1024, Height = 1024, Quality = "standard" }
            },
            new PromptData
            {
                Id = Guid.NewGuid().ToString(),
                Text = "A cute cat wearing a wizard hat, casting a spell, cartoon style",
                NegativePrompt = "scary, dark",
                Parameters = new GenerationParameters { Width = 1024, Height = 1024, Quality = "standard" }
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(examplePrompts, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText("prompts.json", json);
        Console.WriteLine("Создан пример prompts.json");
    }
}
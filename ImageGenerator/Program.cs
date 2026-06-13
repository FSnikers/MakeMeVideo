using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        Console.WriteLine("=== MakeMeVideo Image Generator ===\n");

        var apiType = ReadConfigApiType();

        Console.WriteLine($"Режим: {(apiType == "ChatGPT" ? "ChatGPT (Selenium)" : "OpenAI API")}");

        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddSingleton<IConfigManager, ConfigManager>();
        services.AddSingleton<IPromptRepository, FilePromptRepository>();
        services.AddSingleton<IAccountStorage, JsonAccountStorage>();

        if (apiType == "ChatGPT")
        {
            services.AddSingleton<IImageGenerator>(sp =>
            {
                var accountStorage = sp.GetRequiredService<IAccountStorage>();
                var logger = sp.GetRequiredService<ILogger<ChatGptImageGenerator>>();
                var config = sp.GetRequiredService<IConfigManager>().GetConfig();
                return new ChatGptImageGenerator(accountStorage, logger, config.OutputDirectory, headless: false);
            });
        }
        else
        {
            services.AddHttpClient<IImageGenerator, OpenAiImageGenerator>();
        }

        var serviceProvider = services.BuildServiceProvider();

        var generator = serviceProvider.GetRequiredService<IImageGenerator>();
        var repository = serviceProvider.GetRequiredService<IPromptRepository>();
        var configManager = serviceProvider.GetRequiredService<IConfigManager>();

        if (apiType != "ChatGPT")
        {
            if (!configManager.ValidateConfig())
            {
                Console.WriteLine("Ошибка: Проверьте настройки в appsettings.json");
                Console.WriteLine("Укажите ApiKey и ServerUrl");
                return;
            }
        }
        else
        {
            Console.WriteLine("Режим ChatGPT: убедитесь, что Chrome установлен");
            Console.WriteLine("и аккаунты настроены в chatgpt_accounts.json\n");
        }

        while (true)
        {
            Console.WriteLine("\n--- Главное меню ---");
            Console.WriteLine("1. Загрузить промпты из prompts.json");
            Console.WriteLine("2. Добавить новый промпт");
            Console.WriteLine("3. Запустить генерацию всех ожидающих промптов");
            Console.WriteLine("4. Запустить одиночную генерацию");
            Console.WriteLine("5. Выйти");
            Console.Write("\nВаш выбор: ");

            var choice = Console.ReadLine()?.Trim();

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
                case "4":
                    await RunSingleGenerationAsync(generator, configManager);
                    break;
                case "5":
                    Console.WriteLine("Выход...");
                    return;
                default:
                    Console.WriteLine("Неверный выбор");
                    break;
            }
        }
    }

    static async Task RunSingleGenerationAsync(IImageGenerator generator, IConfigManager configManager)
    {
        Console.Write("\nВведите промпт для генерации изображения: ");
        var promptText = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(promptText))
        {
            Console.WriteLine("Промпт не может быть пустым");
            return;
        }

        var config = configManager.GetConfig();
        var request = new GenerationRequest
        {
            Prompt = promptText,
            Model = config.Model,
            Width = config.ImageWidth,
            Height = config.ImageHeight,
            Quality = config.Quality
        };

        Console.WriteLine("\nГенерация изображения...\n");
        var result = await generator.GenerateImageAsync(request);

        if (result.IsSuccess)
        {
            Console.WriteLine($"✅ Изображение сохранено: {result.ImagePath}");
            if (!string.IsNullOrEmpty(result.RevisedPrompt))
                Console.WriteLine($"Уточненный промпт: {result.RevisedPrompt}");
        }
        else
        {
            Console.WriteLine($"❌ Ошибка: {result.ErrorMessage}");
        }

        Console.WriteLine($"Длительность: {result.Duration.TotalSeconds:F1} сек");
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
        var prompts = JsonSerializer.Deserialize<List<PromptData>>(json);

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
                services.AddSingleton<IAccountStorage, JsonAccountStorage>();

                var apiType = ReadConfigApiType();
                if (apiType == "ChatGPT")
                {
                    services.AddSingleton<IImageGenerator>(sp =>
                    {
                        var accountStorage = sp.GetRequiredService<IAccountStorage>();
                        var logger = sp.GetRequiredService<ILogger<ChatGptImageGenerator>>();
                        var config = sp.GetRequiredService<IConfigManager>().GetConfig();
                        return new ChatGptImageGenerator(accountStorage, logger, config.OutputDirectory, headless: false);
                    });
                }
                else
                {
                    services.AddHttpClient<IImageGenerator, OpenAiImageGenerator>();
                }

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
                        Model = config.Model,
                        Width = prompt.Parameters?.Width ?? config.ImageWidth,
                        Height = prompt.Parameters?.Height ?? config.ImageHeight,
                        Quality = prompt.Parameters?.Quality ?? config.Quality
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

    static string ReadConfigApiType()
    {
        try
        {
            var json = File.ReadAllText("appsettings.json");
            var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (config != null && config.TryGetValue("ApiType", out var apiType))
            {
                return apiType.GetString() ?? "OpenAI";
            }
        }
        catch
        {
        }

        return "OpenAI";
    }

    static void CreateExamplePromptsFile()
    {
        var examplePrompts = new[]
        {
            new PromptData
            {
                Id = Guid.NewGuid().ToString(),
                Text = "A serene landscape with mountains and a lake at sunset, digital art style",
                Status = "pending"
            },
            new PromptData
            {
                Id = Guid.NewGuid().ToString(),
                Text = "Cyberpunk city street at night with neon lights, anime style, vibrant colors",
                Status = "pending"
            },
            new PromptData
            {
                Id = Guid.NewGuid().ToString(),
                Text = "A cute cat wearing a wizard hat, casting a spell, cartoon style",
                Status = "pending"
            }
        };

        var json = JsonSerializer.Serialize(examplePrompts,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText("prompts.json", json);
        Console.WriteLine("Создан пример prompts.json");
    }
}

using System.Text.Json;
using ImageGenerator.Config.Interfaces;
using ImageGenerator.Services.FilePrompt.Interfaces;
using ImageGenerator.Services.FilePrompt.Models;
using ImageGenerator.Services.ImageGenerator.Abstractions;

namespace ImageGenerator.Startup;

public static class PromptExtensions
{
    public static async Task LoadPromptsFromFileAsync(
        this IPromptRepository repository,
        IImageGenerator? generator = null,
        IConfigManager? configManager = null)
    {
        var promptsFile = "prompts.json";
        if (!File.Exists(promptsFile))
        {
            Console.WriteLine($"Файл {promptsFile} не найден. Создаю пример...");
            CreateExamplePromptsFile();
        }

        var json = await File.ReadAllTextAsync(promptsFile);
        PromptCollection? collection;

        try
        {
            collection = JsonSerializer.Deserialize<PromptCollection>(json);
        }
        catch
        {
            try
            {
                var oldPrompts = JsonSerializer.Deserialize<List<PromptData>>(json);
                if (oldPrompts != null)
                {
                    collection = new PromptCollection { Prompts = oldPrompts };
                }
                else
                {
                    collection = null;
                }
            }
            catch
            {
                collection = null;
            }
        }

        if (collection == null || !collection.Prompts.Any())
        {
            Console.WriteLine("Файл prompts.json пуст или содержит некорректные данные");
            return;
        }

        Console.WriteLine($"Загружено {collection.Prompts.Count} промптов в репозиторий");
        Console.WriteLine($"PrePromptText: {collection.PrePromptText}");
        Console.WriteLine($"PostPromptText: {collection.PostPromptText}");
        Console.WriteLine($"BaseChatMessage: {collection.BaseChatMessage}");

        if (generator == null) return;

        Console.Write("\nЗапустить генерацию? (y/n): ");
        if (Console.ReadLine()?.ToLower() != "y") return;

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("\nОтмена генерации...");
        };

        await generator.GenerateAllPendingPromptsAsync(repository, configManager!, cts.Token);
    }

    public static async Task AddNewPromptAsync(this IPromptRepository repository)
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

    public static void CreateExamplePromptsFile()
    {
        var collection = new PromptCollection
        {
            PrePromptText = "",
            PostPromptText = "",
            BaseChatMessage = "You are an expert AI image generator. Generate high-quality images for each prompt I provide.",
            Prompts =
            [
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
            ]
        };

        var json = JsonSerializer.Serialize(collection,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText("prompts.json", json);
        Console.WriteLine("Создан пример prompts.json");
    }
}

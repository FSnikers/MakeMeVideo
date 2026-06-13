using ImageGenerator.Interfaces;
using ImageGenerator.Models;

namespace ImageGenerator.Startup;

public static class GenerationExtensions
{
    public static async Task RunSingleGenerationAsync(
        this IImageGenerator generator,
        IConfigManager configManager,
        CancellationToken cancellationToken = default)
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
        var result = await generator.GenerateImageAsync(request, cancellationToken);

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

    public static async Task GenerateAllPendingPromptsAsync(
        this IImageGenerator generator,
        IPromptRepository repository,
        IConfigManager configManager,
        CancellationToken cancellationToken = default)
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
        var semaphore = new SemaphoreSlim(1, 1);
        var tasks = new List<Task>();

        foreach (var prompt in pendingPrompts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await semaphore.WaitAsync(cancellationToken);

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

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

                    var result = await generator.GenerateImageAsync(request, cancellationToken);

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
                catch (OperationCanceledException)
                {
                    Console.WriteLine($"⏹ [{prompt.Id}] Отменено");
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
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);
        Console.WriteLine("\n✅ Генерация завершена!");
    }
}

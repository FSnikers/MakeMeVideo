using System;

namespace ImageGenerator.Models;

/// <summary>
/// Результат генерации изображения
/// </summary>
public class GenerationResult
{
    public bool IsSuccess { get; set; }
    public string? ImagePath { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan Duration { get; set; }
    public string? PromptId { get; set; }
    public string? RevisedPrompt { get; set; } // DALL-E возвращает переписанный промпт
}
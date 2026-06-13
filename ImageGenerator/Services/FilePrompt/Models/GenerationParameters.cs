using System.Text.Json.Serialization;

namespace ImageGenerator.Services.FilePrompt.Models;

/// <summary>
/// Параметры генерации изображения
/// </summary>
public class GenerationParameters
{
    [JsonPropertyName("width")]
    public int Width { get; set; } = 1024;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 1024;

    [JsonPropertyName("quality")]
    public string Quality { get; set; } = "standard";

    [JsonPropertyName("style")]
    public string? Style { get; set; }
}
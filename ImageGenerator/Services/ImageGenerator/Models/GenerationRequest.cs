using System.Text.Json.Serialization;

namespace ImageGenerator.Services.ImageGenerator.Models;

public class GenerationRequest
{
    public string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("negativePrompt")]
    public string? NegativePrompt { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("n")]
    public int N { get; set; } = 1;

    [JsonPropertyName("width")]
    public int Width { get; set; } = 1024;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 1024;

    [JsonPropertyName("quality")]
    public string? Quality { get; set; }
}

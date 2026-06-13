namespace ImageGenerator.Models;

/// <summary>
/// Запрос на генерацию изображения для API
/// </summary>
public class GenerationRequest
{
    public string Prompt { get; set; } = "";
    public string? NegativePrompt { get; set; }
    public int Width { get; set; } = 1024;
    public int Height { get; set; } = 1024;
    public string Quality { get; set; } = "standard";
    public string Model { get; set; } = "dall-e-3";
    public int N { get; set; } = 1;
}
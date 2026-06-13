using System.Text.Json.Serialization;

namespace ImageGenerator.Models;

/// <summary>
/// Конфигурация приложения из appsettings.json
/// </summary>
public class AppConfig
{
    [JsonPropertyName("ApiType")]
    public string ApiType { get; set; } = "ChatGPT";

    [JsonPropertyName("ApiKey")]
    public string ApiKey { get; set; } = "";

    [JsonPropertyName("ServerUrl")]
    public string ServerUrl { get; set; } = "https://api.openai.com/v1/images/generations";

    [JsonPropertyName("Model")]
    public string Model { get; set; } = "dall-e-3";

    [JsonPropertyName("ImageWidth")]
    public int ImageWidth { get; set; } = 1024;

    [JsonPropertyName("ImageHeight")]
    public int ImageHeight { get; set; } = 1024;

    [JsonPropertyName("Quality")]
    public string Quality { get; set; } = "standard";

    [JsonPropertyName("MaxParallelRequests")]
    public int MaxParallelRequests { get; set; } = 2;

    [JsonPropertyName("TimeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 120;

    [JsonPropertyName("OutputDirectory")]
    public string OutputDirectory { get; set; } = "./output";

    [JsonPropertyName("RetryCount")]
    public int RetryCount { get; set; } = 3;

    [JsonPropertyName("RetryDelaySeconds")]
    public int RetryDelaySeconds { get; set; } = 5;

    [JsonPropertyName("Headless")]
    public bool Headless { get; set; } = false;
}
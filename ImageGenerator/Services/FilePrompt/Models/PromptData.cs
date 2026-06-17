using System.Text.Json.Serialization;

namespace ImageGenerator.Services.FilePrompt.Models;

public class PromptData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";
}

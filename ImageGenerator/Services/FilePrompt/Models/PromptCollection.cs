using System.Text.Json.Serialization;

namespace ImageGenerator.Services.FilePrompt.Models;

public class PromptCollection
{
    [JsonPropertyName("prePromptText")]
    public string PrePromptText { get; set; } = "";

    [JsonPropertyName("postPromptText")]
    public string PostPromptText { get; set; } = "";

    [JsonPropertyName("baseChatMessage")]
    public string BaseChatMessage { get; set; } = "";

    [JsonPropertyName("prompts")]
    public List<PromptData> Prompts { get; set; } = new();
}

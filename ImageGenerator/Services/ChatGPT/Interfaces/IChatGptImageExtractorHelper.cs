using ImageGenerator.Models;

namespace ImageGenerator.Services.ChatGPT.Interfaces;

public interface IChatGptImageExtractorHelper
{
    Task<GenerationResult?> ExtractImageFromPageAsync();
}

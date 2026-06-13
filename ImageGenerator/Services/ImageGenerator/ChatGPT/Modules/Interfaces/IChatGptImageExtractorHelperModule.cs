using ImageGenerator.Services.ImageGenerator.Models;

namespace ImageGenerator.Services.ImageGenerator.ChatGPT.Modules.Interfaces;

public interface IChatGptImageExtractorHelperModule
{
    Task<GenerationResult?> ExtractImageFromPageAsync();
}

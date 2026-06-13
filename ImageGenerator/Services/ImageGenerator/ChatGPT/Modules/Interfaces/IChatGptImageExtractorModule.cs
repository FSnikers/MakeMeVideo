using ImageGenerator.Services.ImageGenerator.ChatGPT.Models;
using ImageGenerator.Services.ImageGenerator.Models;

namespace ImageGenerator.Services.ImageGenerator.ChatGPT.Modules.Interfaces;

public interface IChatGptImageExtractorModule
{
    Task<GenerationResult> SendPromptAndWaitForImageAsync(string prompt, ChatGptAccount? currentAccount, CancellationToken cancellationToken = default);
}

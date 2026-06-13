using ImageGenerator.Models;

namespace ImageGenerator.Services.ChatGPT.Interfaces;

public interface IChatGptImageExtractor
{
    Task<GenerationResult> SendPromptAndWaitForImageAsync(string prompt, ChatGptAccount? currentAccount, CancellationToken cancellationToken = default);
}

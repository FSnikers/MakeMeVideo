using ImageGenerator.Services.ImageGenerator.ChatGPT.Models;
using ImageGenerator.Services.ImageGenerator.Models;

namespace ImageGenerator.Services.ImageGenerator.ChatGPT.Actions;

public interface IChatGptGenerateAction
{
    Task<GenerationResult> ExecuteAsync(string prompt, ChatGptAccount account, CancellationToken cancellationToken = default);
}

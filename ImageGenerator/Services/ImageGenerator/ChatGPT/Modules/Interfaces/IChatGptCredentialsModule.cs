using ImageGenerator.Services.ImageGenerator.ChatGPT.Models;

namespace ImageGenerator.Services.ImageGenerator.ChatGPT.Modules.Interfaces;

public interface IChatGptCredentialsModule
{
    Task FillEmailAsync(ChatGptAccount account, CancellationToken cancellationToken = default);

    Task FillPasswordAsync(ChatGptAccount account, CancellationToken cancellationToken = default);
}

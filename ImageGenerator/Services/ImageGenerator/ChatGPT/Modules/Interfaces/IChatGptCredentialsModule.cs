using ImageGenerator.Services.ImageGenerator.ChatGPT.Models;

namespace ImageGenerator.Services.ImageGenerator.ChatGPT.Modules.Interfaces;

public interface IChatGptCredentialsModule
{
    Task FillCredentialsAsync(ChatGptAccount account, CancellationToken cancellationToken = default);
}

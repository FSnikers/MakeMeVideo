using ImageGenerator.Services.ImageGenerator.ChatGPT.Models;

namespace ImageGenerator.Services.ImageGenerator.ChatGPT.Modules.Interfaces;

public interface IChatGptLoginModule
{
    Task LoginToChatGptAsync(ChatGptAccount account, CancellationToken cancellationToken = default);
}

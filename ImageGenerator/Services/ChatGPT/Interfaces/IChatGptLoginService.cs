using ImageGenerator.Models;

namespace ImageGenerator.Services.ChatGPT.Interfaces;

public interface IChatGptLoginService
{
    Task LoginToChatGptAsync(ChatGptAccount account, CancellationToken cancellationToken = default);
}

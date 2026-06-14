using ImageGenerator.Services.ImageGenerator.ChatGPT.Models;

namespace ImageGenerator.Services.ImageGenerator.ChatGPT.Actions;

public interface IChatGptAuthAction
{
    Task<bool> TryRestoreCookiesAsync(ChatGptAccount account, CancellationToken cancellationToken = default);

    Task LoginAsync(ChatGptAccount account, CancellationToken cancellationToken = default);

    Task HandleSessionExpiredAsync(CancellationToken cancellationToken = default);

    Task HandleAccountChooserAsync(CancellationToken cancellationToken = default);
}

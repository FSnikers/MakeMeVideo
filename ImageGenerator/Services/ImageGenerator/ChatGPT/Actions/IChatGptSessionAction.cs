using ImageGenerator.Services.ImageGenerator.ChatGPT.Models;

namespace ImageGenerator.Services.ImageGenerator.ChatGPT.Actions;

public interface IChatGptSessionAction
{
    Task ForceLogoutAsync(CancellationToken cancellationToken = default);

    Task MarkAccountUsedAsync(ChatGptAccount account);

    Task MarkAccountLimitReachedAsync(ChatGptAccount account);
}

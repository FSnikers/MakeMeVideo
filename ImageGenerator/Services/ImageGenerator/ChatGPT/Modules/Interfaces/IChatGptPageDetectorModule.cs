using ImageGenerator.Services.ImageGenerator.ChatGPT.Models;

namespace ImageGenerator.Services.ImageGenerator.ChatGPT.Modules.Interfaces;

public interface IChatGptPageDetectorModule
{
    Task<ChatGptPageStatus> DetectCurrentPageAsync();

    Task<ChatGptPageStatus> WaitForStatusAsync(
        Func<ChatGptPageStatus, bool> predicate,
        int timeoutSeconds = 15);

    Task<bool> WaitForExactStatusAsync(
        ChatGptPageStatus expected,
        int timeoutSeconds = 15);

    Task<bool> EnsureTransitionAwayFromAsync(
        ChatGptPageStatus current,
        int timeoutSeconds = 15);

    Task<ChatGptPageStatus> DetectWithConfirmAsync(int retryCount = 3, int delayMs = 400,
        CancellationToken cancellationToken = default);
}

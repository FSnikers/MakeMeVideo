using System.Collections.Concurrent;
using System.Diagnostics;
using ImageGenerator.Services.AccountStorage.Interfaces;
using ImageGenerator.Services.Browser;
using ImageGenerator.Services.ImageGenerator.Abstractions;
using ImageGenerator.Services.ImageGenerator.ChatGPT.Actions;
using ImageGenerator.Services.ImageGenerator.ChatGPT.Models;
using ImageGenerator.Services.ImageGenerator.ChatGPT.Modules.Interfaces;
using ImageGenerator.Services.ImageGenerator.Models;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.Services.ImageGenerator.ChatGPT;

public class ChatGptImageGenerator : IImageGenerator
{
    private readonly IAccountStorage _accountStorage;
    private readonly ILogger<ChatGptImageGenerator> _logger;
    private readonly IBrowserDriverFactory _driverFactory;
    private readonly IChatGptPageDetectorModule _detector;
    private readonly IChatGptAuthAction _authAction;
    private readonly IChatGptGenerateAction _generateAction;
    private readonly IChatGptSessionAction _sessionAction;
    private readonly SemaphoreSlim _driverLock = new(1, 1);
    private readonly ConcurrentDictionary<string, bool> _cookieRestoreAttempted = new();
    private ChatGptAccount? _currentAccount;
    private bool _disposed;

    private const int MaxAccountSwitchAttempts = 99;
    private const int MaxDetectionCycles = 15;

    public ChatGptImageGenerator(
        IAccountStorage accountStorage,
        ILogger<ChatGptImageGenerator> logger,
        IBrowserDriverFactory driverFactory,
        IChatGptPageDetectorModule detector,
        IChatGptAuthAction authAction,
        IChatGptGenerateAction generateAction,
        IChatGptSessionAction sessionAction)
    {
        _accountStorage = accountStorage;
        _logger = logger;
        _driverFactory = driverFactory;
        _detector = detector;
        _authAction = authAction;
        _generateAction = generateAction;
        _sessionAction = sessionAction;
    }

    public async Task<GenerationResult> GenerateImageAsync(GenerationRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        await _driverLock.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 0; attempt < MaxAccountSwitchAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_currentAccount == null)
                {
                    _currentAccount = await _accountStorage.GetNextAvailableAccountAsync();
                    if (_currentAccount == null)
                    {
                        _logger.LogError("No available ChatGPT accounts");
                        return GenerationResult.Failure(
                            "Нет доступных аккаунтов ChatGPT. Добавьте учетные записи в chatgpt_accounts.json",
                            "NoAccounts");
                    }
                }

                _logger.LogInformation("Using account: {Email} (attempt {Attempt})",
                    _currentAccount.Email, attempt + 1);

                try
                {
                    var result = await ExecuteForAccountAsync(request, cancellationToken);

                    if (result != null)
                    {
                        result.Duration = stopwatch.Elapsed;
                        return result;
                    }
                    _logger.LogInformation("Switching from account {Email}", _currentAccount.Email);
                    _currentAccount = await _accountStorage.GetNextAvailableAccountAsync();
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Image generation cancelled for account {Email}", _currentAccount.Email);
                    return GenerationResult.Failure("Генерация отменена", "Cancelled");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error generating image with account {Email}", _currentAccount.Email);
                    return GenerationResult.Failure(
                        $"Ошибка генерации: {ex.Message}", "GenerationError");
                }

                if (attempt < MaxAccountSwitchAttempts - 1)
                {
                    await _sessionAction.ForceLogoutAsync(cancellationToken);
                }
            }
        }
        finally
        {
            _driverLock.Release();
        }

        stopwatch.Stop();
        return GenerationResult.Failure(
            $"Не удалось сгенерировать изображение после {MaxAccountSwitchAttempts} попыток (все аккаунты исчерпали лимит)",
            "MaxAttemptsReached");
    }

    private async Task<GenerationResult?> ExecuteForAccountAsync(GenerationRequest request, CancellationToken cancellationToken)
    {
        for (var cycle = 0; cycle < MaxDetectionCycles; cycle++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Delay(1000, cancellationToken);
            var status = await _detector.DetectWithConfirmAsync(cancellationToken: cancellationToken);
            _logger.LogInformation("Cycle {Cycle}: confirmed status = {Status}", cycle, status);

            switch (status)
            {
                case ChatGptPageStatus.UnAuthChatPage:
                    _driverFactory.NavigateToUrl("https://chatgpt.com/auth/login");
                    break;
                case ChatGptPageStatus.ChatPage:
                    var result = await _generateAction.ExecuteAsync(
                        request.Prompt, _currentAccount!, cancellationToken);

                    if (result.IsSuccess)
                    {
                        await _sessionAction.MarkAccountUsedAsync(_currentAccount!);
                        return result;
                    }

                    switch (result.ErrorType)
                    {
                        case "LimitReached":
                            await _sessionAction.MarkAccountLimitReachedAsync(_currentAccount!);
                            return null;

                        case "Timeout":
                            return GenerationResult.Failure(
                                "Превышено время ожидания генерации изображения", "Timeout");

                        default:
                            _logger.LogWarning("Unrecognized error type {ErrorType} for account {Email}, switching",
                                result.ErrorType, _currentAccount!.Email);
                            return null;
                    }

                case ChatGptPageStatus.LoginPage:
                    var email = _currentAccount!.Email;
                    if (!_cookieRestoreAttempted.GetOrAdd(email, false))
                    {
                        _cookieRestoreAttempted[email] = true;
                        if (await _authAction.TryRestoreCookiesAsync(_currentAccount, cancellationToken))
                            break;
                    }
                    await _authAction.LoginAsync(_currentAccount, cancellationToken);
                    break;

                case ChatGptPageStatus.SessionExpired:
                    await _authAction.HandleSessionExpiredAsync(cancellationToken);
                    break;

                case ChatGptPageStatus.AccountChooser:
                    await _authAction.HandleAccountChooserAsync(cancellationToken);
                    break;

                case ChatGptPageStatus.GmailLogin:
                    await _authAction.HandleGmailLoginAsync(_currentAccount!, cancellationToken);
                    break;

                case ChatGptPageStatus.CloudflareChallenge:
                    _logger.LogInformation("Cloudflare challenge detected, waiting...");
                    await Task.Delay(3000, cancellationToken);
                    _driverFactory.NavigateToUrl("https://chatgpt.com");
                    await _detector.WaitForStatusAsync(
                        s => s != ChatGptPageStatus.Unknown, 20);
                    break;

                case ChatGptPageStatus.LimitReached:
                    _logger.LogWarning("Limit page detected for account {Email}", _currentAccount!.Email);
                    await _sessionAction.MarkAccountLimitReachedAsync(_currentAccount);
                    return null;

                case ChatGptPageStatus.ProhibitedContent:
                    _logger.LogWarning("Prohibited content page detected");
                    return GenerationResult.Failure(
                        "Обнаружен запрещенный контент на странице", "ProhibitedContent");

                case ChatGptPageStatus.Unknown:
                default:
                    _driverFactory.NavigateToUrl("https://chatgpt.com");
                    await _detector.WaitForStatusAsync(
                        s => s != ChatGptPageStatus.Unknown, 15);
                    break;
            }
        }

        _logger.LogWarning("ExecuteForAccount did not reach terminal state after {Max} cycles", MaxDetectionCycles);
        return GenerationResult.Failure(
            "Не удалось достичь стабильного состояния после максимального количества циклов",
            "MaxCyclesReached");
    }

    public async Task<bool> SendBaseMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(message)) return true;

        await _driverLock.WaitAsync(cancellationToken);
        try
        {
            return await _generateAction.SendBaseMessageAsync(message, cancellationToken);
        }
        finally
        {
            _driverLock.Release();
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        await _driverLock.WaitAsync(cancellationToken);
        try
        {
            if (_driverFactory.Driver == null) return false;

            var status = await _detector.DetectCurrentPageAsync();
            return status != ChatGptPageStatus.Unknown;
        }
        catch
        {
            return false;
        }
        finally
        {
            _driverLock.Release();
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _driverFactory.Dispose();
            _disposed = true;
        }
    }
}
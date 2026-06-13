using System.Diagnostics;
using ImageGenerator.Interfaces;
using ImageGenerator.Models;
using ImageGenerator.Services.Browser;
using ImageGenerator.Services.ChatGPT;
using ImageGenerator.Services.ChatGPT.Interfaces;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.Services;

public class ChatGptImageGenerator : IImageGenerator, IDisposable
{
    private readonly IAccountStorage _accountStorage;
    private readonly ILogger<ChatGptImageGenerator> _logger;
    private readonly IBrowserDriverFactory _driverFactory;
    private readonly IChatGptLoginService _loginService;
    private readonly IChatGptImageExtractor _imageExtractor;
    private readonly SemaphoreSlim _driverLock = new(1, 1);
    private ChatGptAccount? _currentAccount;
    private bool _disposed;

    private const int MaxAccountSwitchAttempts = 3;

    public ChatGptImageGenerator(
        IAccountStorage accountStorage,
        ILogger<ChatGptImageGenerator> logger,
        IBrowserDriverFactory driverFactory,
        IChatGptLoginService loginService,
        IChatGptImageExtractor imageExtractor)
    {
        _accountStorage = accountStorage;
        _logger = logger;
        _driverFactory = driverFactory;
        _loginService = loginService;
        _imageExtractor = imageExtractor;
    }

    public async Task<GenerationResult> GenerateImageAsync(GenerationRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var lastException = (Exception?)null;

        await _driverLock.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 0; attempt < MaxAccountSwitchAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _currentAccount = await _accountStorage.GetNextAvailableAccountAsync();

                if (_currentAccount == null)
                {
                    _logger.LogError("No available ChatGPT accounts");
                    return GenerationResult.Failure(
                        "Нет доступных аккаунтов ChatGPT. Добавьте учетные записи в chatgpt_accounts.json",
                        "NoAccounts");
                }

                _logger.LogInformation("Using account: {Email} (attempt {Attempt})",
                    _currentAccount.Email, attempt + 1);

                try
                {
                    await _loginService.LoginToChatGptAsync(_currentAccount, cancellationToken);
                    var result = await _imageExtractor.SendPromptAndWaitForImageAsync(
                        request.Prompt, _currentAccount, cancellationToken);

                    if (result.IsSuccess)
                    {
                        await _accountStorage.MarkAccountUsedAsync(_currentAccount);
                        result.Duration = stopwatch.Elapsed;
                        _logger.LogInformation("Image generated successfully with account {Email} in {Duration}",
                            _currentAccount.Email, stopwatch.Elapsed);
                        return result;
                    }

                    if (result.ErrorType == "LimitReached")
                    {
                        _logger.LogWarning("Limit reached for account {Email}, switching...", _currentAccount.Email);
                        await _accountStorage.MarkAccountLimitReachedAsync(_currentAccount);
                        _driverFactory.NavigateToNewChat();
                        continue;
                    }

                    if (result.ErrorType == "ProhibitedContent" || result.ErrorType == "ContentPolicyViolation")
                    {
                        result.Duration = stopwatch.Elapsed;
                        return result;
                    }

                    if (result.ErrorType == "Timeout" && attempt < MaxAccountSwitchAttempts - 1)
                    {
                        _logger.LogWarning("Timeout on account {Email}, retrying with next account...", _currentAccount.Email);
                        _driverFactory.NavigateToNewChat();
                        continue;
                    }

                    result.Duration = stopwatch.Elapsed;
                    return result;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Image generation cancelled for account {Email}", _currentAccount.Email);
                    return GenerationResult.Failure("Генерация отменена", "Cancelled");
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.LogError(ex, "Error generating image with account {Email}", _currentAccount.Email);

                    if (attempt < MaxAccountSwitchAttempts - 1)
                    {
                        _logger.LogInformation("Retrying with next account...");
                        _driverFactory.RestartDriver();
                    }
                }
            }
        }
        finally
        {
            _driverLock.Release();
        }

        stopwatch.Stop();
        return GenerationResult.Failure(
            $"Не удалось сгенерировать изображение после {MaxAccountSwitchAttempts} попыток. " +
            $"Последняя ошибка: {lastException?.Message}",
            "MaxAttemptsReached");
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        await _driverLock.WaitAsync(cancellationToken);
        try
        {
            var driver = _driverFactory.Driver;
            if (driver == null) return false;

            var url = driver.Url;
            return !string.IsNullOrEmpty(url);
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

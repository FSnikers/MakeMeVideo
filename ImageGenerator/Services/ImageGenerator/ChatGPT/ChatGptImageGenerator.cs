using System.Diagnostics;
using System.Text.Json;
using ImageGenerator.Services.AccountStorage.Interfaces;
using ImageGenerator.Services.Browser;
using ImageGenerator.Services.ImageGenerator.Abstractions;
using ImageGenerator.Services.ImageGenerator.ChatGPT.Models;
using ImageGenerator.Services.ImageGenerator.ChatGPT.Modules.Interfaces;
using ImageGenerator.Services.ImageGenerator.Models;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.Services.ImageGenerator.ChatGPT;

public class ChatGptImageGenerator : IImageGenerator, IDisposable
{
    private readonly IAccountStorage _accountStorage;
    private readonly ILogger<ChatGptImageGenerator> _logger;
    private readonly IBrowserDriverFactory _driverFactory;
    private readonly IChatGptPageDetectorModule _pageDetectorModule;
    private readonly IChatGptBannerHandlerModule _bannerHandlerModule;
    private readonly IChatGptCredentialsModule _credentialsModule;
    private readonly IChatGptImageExtractorModule _imageExtractorModule;
    private readonly SemaphoreSlim _driverLock = new(1, 1);
    private ChatGptAccount? _currentAccount;
    private bool _disposed;

    private const int MaxAccountSwitchAttempts = 3;

    public ChatGptImageGenerator(
        IAccountStorage accountStorage,
        ILogger<ChatGptImageGenerator> logger,
        IBrowserDriverFactory driverFactory,
        IChatGptPageDetectorModule pageDetectorModule,
        IChatGptBannerHandlerModule bannerHandlerModule,
        IChatGptCredentialsModule credentialsModule,
        IChatGptImageExtractorModule imageExtractorModule)
    {
        _accountStorage = accountStorage;
        _logger = logger;
        _driverFactory = driverFactory;
        _pageDetectorModule = pageDetectorModule;
        _bannerHandlerModule = bannerHandlerModule;
        _credentialsModule = credentialsModule;
        _imageExtractorModule = imageExtractorModule;
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
                    await EnsureChatPageAsync(cancellationToken);

                    var result = await _imageExtractorModule.SendPromptAndWaitForImageAsync(
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
                        await _pageDetectorModule.WaitForStatusAsync(
                            s => s != ChatGptPageStatus.Unknown, 10);
                        continue;
                    }

                    if (result.ErrorType is "ProhibitedContent" or "ContentPolicyViolation")
                    {
                        result.Duration = stopwatch.Elapsed;
                        return result;
                    }

                    if (result.ErrorType == "Timeout" && attempt < MaxAccountSwitchAttempts - 1)
                    {
                        _logger.LogWarning("Timeout on account {Email}, retrying with next account...", _currentAccount.Email);
                        _driverFactory.NavigateToNewChat();
                        await _pageDetectorModule.WaitForStatusAsync(
                            s => s != ChatGptPageStatus.Unknown, 10);
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
                        await _pageDetectorModule.WaitForStatusAsync(
                            s => s != ChatGptPageStatus.Unknown, 15);
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

    private async Task EnsureChatPageAsync(CancellationToken cancellationToken)
    {
        for (var cycle = 0; cycle < 10; cycle++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var status = await _pageDetectorModule.DetectCurrentPageAsync();
            _logger.LogInformation("EnsureChatPage cycle {Cycle}: status = {Status}", cycle, status);

            switch (status)
            {
                case ChatGptPageStatus.ChatPage:
                    _logger.LogInformation("Already on chat page");
                    return;

                case ChatGptPageStatus.SessionExpired:
                    var bannerClicked = await _bannerHandlerModule.TryClickLogInAsync();
                    if (!bannerClicked)
                    {
                        _logger.LogWarning("Banner click failed, will try direct navigation");
                        _driverFactory.NavigateToUrl("https://chatgpt.com/auth/login");
                    }
                    await _pageDetectorModule.WaitForStatusAsync(
                        s => s is ChatGptPageStatus.LoginPage or ChatGptPageStatus.ChatPage, 10);
                    break;

                case ChatGptPageStatus.LoginPage:
                    await HandleLoginPageAsync(cancellationToken);
                    break;

                case ChatGptPageStatus.LimitReached:
                    throw new InvalidOperationException("Limit reached on account");

                case ChatGptPageStatus.ProhibitedContent:
                    throw new InvalidOperationException("Content policy violation");

                case ChatGptPageStatus.Unknown:
                default:
                    _driverFactory.NavigateToUrl("https://chatgpt.com");
                    await _pageDetectorModule.WaitForStatusAsync(
                        s => s != ChatGptPageStatus.Unknown, 15);
                    break;
            }
        }

        _logger.LogWarning("EnsureChatPage did not reach chat page after 10 cycles, continuing anyway");
    }

    private async Task HandleLoginPageAsync(CancellationToken cancellationToken)
    {
        var cookiesJson = await _accountStorage.LoadCookiesAsync(_currentAccount!.Email);
        if (!string.IsNullOrEmpty(cookiesJson))
        {
            if (await TryRestoreSessionFromCookiesAsync(cookiesJson))
                return;
        }

        await _credentialsModule.FillCredentialsAsync(_currentAccount, cancellationToken);

        var afterLogin = await _pageDetectorModule.WaitForExactStatusAsync(ChatGptPageStatus.ChatPage, 30);
        if (afterLogin)
        {
            var driver = _driverFactory.Driver;
            if (driver != null)
            {
                var cookieDataList = driver.Manage().Cookies.AllCookies
                    .Select(ToCookieData).ToList();
                var json = JsonSerializer.Serialize(cookieDataList);
                await _accountStorage.SaveCookiesAsync(_currentAccount.Email, json);
            }
        }
    }

    private async Task<bool> TryRestoreSessionFromCookiesAsync(string cookiesJson)
    {
        var driver = _driverFactory.Driver;
        if (driver == null) return false;

        try
        {
            var cookieDataList = JsonSerializer.Deserialize<List<CookieData>>(cookiesJson);
            if (cookieDataList == null || cookieDataList.Count == 0)
                return false;

            driver.Manage().Cookies.DeleteAllCookies();

            _driverFactory.NavigateToUrl("https://chatgpt.com");

            foreach (var cd in cookieDataList)
            {
                try
                {
                    driver.Manage().Cookies.AddCookie(FromCookieData(cd));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to add cookie {Name}: {Message}", cd.Name, ex.Message);
                }
            }

            driver.Navigate().Refresh();
            _driverFactory.WaitForPageLoad();

            var status = await _pageDetectorModule.WaitForStatusAsync(
                s => s is ChatGptPageStatus.ChatPage or ChatGptPageStatus.SessionExpired, 15);

            if (status == ChatGptPageStatus.ChatPage)
            {
                _logger.LogInformation("Session restored from cookies");
                return true;
            }

            if (status == ChatGptPageStatus.SessionExpired)
            {
                _logger.LogInformation("Session restored but expired banner detected, clicking Log in");
                var clicked = await _bannerHandlerModule.TryClickLogInAsync();
                if (clicked)
                {
                    var loginPage = await _pageDetectorModule.WaitForExactStatusAsync(ChatGptPageStatus.LoginPage, 10);
                    return loginPage;
                }
            }

            _logger.LogInformation("Cookies expired, need to login");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to restore session: {Message}", ex.Message);
            return false;
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        await _driverLock.WaitAsync(cancellationToken);
        try
        {
            if (_driverFactory.Driver == null) return false;

            var status = await _pageDetectorModule.DetectCurrentPageAsync();
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

    private static CookieData ToCookieData(OpenQA.Selenium.Cookie c)
    {
        return new CookieData
        {
            Name = c.Name,
            Value = c.Value,
            Domain = c.Domain ?? string.Empty,
            Path = c.Path ?? "/",
            Expiry = c.Expiry.HasValue
                ? new DateTimeOffset(c.Expiry.Value.ToUniversalTime()).ToUnixTimeSeconds()
                : null,
            Secure = c.Secure,
            HttpOnly = c.IsHttpOnly
        };
    }

    private static OpenQA.Selenium.Cookie FromCookieData(CookieData cd)
    {
        DateTime? expiry = null;
        if (cd.Expiry.HasValue)
        {
            var dt = DateTimeOffset.FromUnixTimeSeconds(cd.Expiry!.Value).UtcDateTime;
            if (dt > DateTime.UtcNow)
                expiry = dt;
        }

        return new OpenQA.Selenium.Cookie(
            cd.Name,
            cd.Value,
            cd.Domain,
            cd.Path,
            expiry);
    }


}

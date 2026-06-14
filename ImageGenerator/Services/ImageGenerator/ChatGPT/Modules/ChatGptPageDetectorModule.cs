using ImageGenerator.Services.Browser;
using ImageGenerator.Services.ImageGenerator.ChatGPT.Models;
using ImageGenerator.Services.ImageGenerator.ChatGPT.Modules.Interfaces;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

namespace ImageGenerator.Services.ImageGenerator.ChatGPT.Modules;

public class ChatGptPageDetectorModule : IChatGptPageDetectorModule
{
    private readonly IBrowserDriverFactory _driverFactory;
    private readonly ILogger<ChatGptPageDetectorModule> _logger;

    private static readonly string[] ChatPageIndicators =
    [
        "#prompt-textarea",
        "textarea[placeholder*='Message']",
        "textarea[placeholder*='message']",
        "div[contenteditable='true']",
        "[data-message-author-role]",
    ];

    private static readonly string[] LimitErrorPatterns =
    [
        "limit reached",
        "generation limit",
        "too many requests",
        "rate limit",
        "try again later",
        "usage limit",
        "you've reached"
    ];

    private static readonly string[] ProhibitedContentPatterns =
    [
        "cannot generate",
        "can't generate",
        "unable to generate",
        "violates our",
        "content policy",
        "safety system",
        "not allowed",
        "inappropriate",
        "harmful content",
        "prohibited",
        "does not comply"
    ];

    public ChatGptPageDetectorModule(
        IBrowserDriverFactory driverFactory,
        ILogger<ChatGptPageDetectorModule> logger)
    {
        _driverFactory = driverFactory;
        _logger = logger;
    }

    public Task<ChatGptPageStatus> DetectCurrentPageAsync()
    {
        var driver = _driverFactory.Driver;
        if (driver == null)
            return Task.FromResult(ChatGptPageStatus.Unknown);

        try
        {
            var url = driver.Url.ToLowerInvariant();

            if (string.IsNullOrEmpty(url) || url == "about:blank")
                return Task.FromResult(ChatGptPageStatus.Unknown);

            if (IsCloudflareChallenge(driver))
                return Task.FromResult(ChatGptPageStatus.CloudflareChallenge);

            if (url.Contains("auth") || url.Contains("login"))
                return Task.FromResult(ChatGptPageStatus.LoginPage);

            if (url.Contains("chatgpt.com"))
            {
                if (IsSessionExpired(driver))
                    return Task.FromResult(ChatGptPageStatus.SessionExpired);

                if (IsAccountChooser(driver))
                    return Task.FromResult(ChatGptPageStatus.AccountChooser);

                if (HasChatPageElements(driver))
                {
                    if (IsLoginButtonVisible(driver))
                        return Task.FromResult(ChatGptPageStatus.LoginPage);
                    return Task.FromResult(ChatGptPageStatus.ChatPage);
                }

                if (HasErrorState(driver, out var errorStatus))
                    return Task.FromResult(errorStatus);

                return Task.FromResult(ChatGptPageStatus.LoginPage);
            }

            return Task.FromResult(ChatGptPageStatus.Unknown);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to detect page status: {Message}", ex.Message);
            return Task.FromResult(ChatGptPageStatus.Unknown);
        }
    }

    public async Task<ChatGptPageStatus> WaitForStatusAsync(
        Func<ChatGptPageStatus, bool> predicate,
        int timeoutSeconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var status = await DetectCurrentPageAsync();
            if (predicate(status))
                return status;
            await Task.Delay(500);
        }
        var last = await DetectCurrentPageAsync();
        _logger.LogWarning("WaitForStatusAsync timed out after {Timeout}s, last status: {Status}",
            timeoutSeconds, last);
        return last;
    }

    public async Task<bool> WaitForExactStatusAsync(
        ChatGptPageStatus expected,
        int timeoutSeconds = 15)
    {
        var result = await WaitForStatusAsync(s => s == expected, timeoutSeconds);
        return result == expected;
    }

    public async Task<bool> EnsureTransitionAwayFromAsync(
        ChatGptPageStatus current,
        int timeoutSeconds = 15)
    {
        var result = await WaitForStatusAsync(s => s != current, timeoutSeconds);
        return result != current;
    }

    public async Task<ChatGptPageStatus> DetectWithConfirmAsync(
        int retryCount = 3, int delayMs = 400,
        CancellationToken cancellationToken = default)
    {
        for (var retry = 0; retry < retryCount; retry++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var first = await DetectCurrentPageAsync();
            await Task.Delay(delayMs, cancellationToken);
            var second = await DetectCurrentPageAsync();

            if (first == second)
                return first;

            _logger.LogWarning("Detect mismatch: {First} vs {Second}, retry {Retry}",
                first, second, retry + 1);
        }

        _logger.LogWarning("DetectWithConfirm failed to get stable status after {RetryCount} retries", retryCount);
        return ChatGptPageStatus.Unknown;
    }

    private static bool IsCloudflareChallenge(ChromeDriver driver)
    {
        try
        {
            var url = driver.Url.ToLowerInvariant();
            var title = driver.Title.ToLowerInvariant();
            var bodyText = driver.FindElement(By.TagName("body")).Text.ToLowerInvariant();
            var pageSource = driver.PageSource.ToLowerInvariant();

            return url.Contains("cloudflare") ||
                   url.Contains("challenge") ||
                   title.Contains("cloudflare") ||
                   title.Contains("just a moment") ||
                   bodyText.Contains("checking your browser") ||
                   bodyText.Contains("verifying you are human") ||
                   bodyText.Contains("enable javascript") ||
                   bodyText.Contains("cloudflare") ||
                   pageSource.Contains("cf-challenge") ||
                   pageSource.Contains("cf-browser-verification") ||
                   pageSource.Contains("_cf_chl_opt") ||
                   pageSource.Contains("__cf_chl_f_tk") ||
                   pageSource.Contains("cf-please-wait");
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSessionExpired(ChromeDriver driver)
    {
        try
        {
            if (driver.PageSource.Contains("Your session has expired", StringComparison.OrdinalIgnoreCase))
                return true;

            var expiredElements = driver.FindElements(By.XPath("//*[contains(text(), 'session has expired')]"));
            if (expiredElements.Count > 0)
                return true;

            var sessionBanners = driver.FindElements(
                By.CssSelector("[class*='session'], [class*='expired'], [role='alert'], [role='dialog']"));
            foreach (var el in sessionBanners)
            {
                var text = el.Text.ToLowerInvariant();
                if (text.Contains("session") && (text.Contains("expired") || text.Contains("ended")))
                    return true;
            }

            var openDialogs = driver.FindElements(By.CssSelector("[role='dialog'][data-state='open']"));
            foreach (var dlg in openDialogs)
            {
                var text = dlg.Text.ToLowerInvariant();
                if (text.Contains("session") && text.Contains("expired"))
                    return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool IsAccountChooser(ChromeDriver driver)
    {
        try
        {
            var modal = driver.FindElements(By.CssSelector(
                "#modal-no-auth-login, [data-testid='modal-no-auth-login']"));
            if (modal.Count > 0 && modal.Any(e => e.Displayed))
                return true;

            var logBackForm = driver.FindElements(
                By.CssSelector("[data-testid='log-back-form']"));
            if (logBackForm.Count > 0 && logBackForm.Any(e => e.Displayed))
                return true;

            var pageText = driver.FindElement(By.TagName("body")).Text.ToLowerInvariant();
            return pageText.Contains("choose an account to continue");
        }
        catch
        {
            return false;
        }
    }

    private static bool HasErrorState(ChromeDriver driver, out ChatGptPageStatus errorStatus)
    {
        errorStatus = ChatGptPageStatus.Unknown;
        try
        {
            var errorContainers = driver.FindElements(
                By.CssSelector("[role='alert'], .error, .warning, [class*='toast'], " +
                               "[class*='modal'], [class*='overlay'], " +
                               "[class*='banner'], [class*='notification']"));

            foreach (var container in errorContainers)
            {
                var text = container.Text.ToLowerInvariant();

                if (LimitErrorPatterns.Any(p => text.Contains(p)))
                {
                    errorStatus = ChatGptPageStatus.LimitReached;
                    return true;
                }

                if (ProhibitedContentPatterns.Any(p => text.Contains(p)))
                {
                    errorStatus = ChatGptPageStatus.ProhibitedContent;
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool HasChatPageElements(ChromeDriver driver)
    {
        try
        {
            foreach (var selector in ChatPageIndicators)
            {
                try
                {
                    var elements = driver.FindElements(By.CssSelector(selector));
                    if (elements.Count > 0 && elements.Any(e => e.Displayed))
                        return true;
                }
                catch
                {
                }
            }

            var textareas = driver.FindElements(By.TagName("textarea"));
            if (textareas.Count > 0 && textareas.Any(t => t.Displayed))
                return true;

            var navElements = driver.FindElements(By.CssSelector("nav"));
            if (navElements.Count > 0)
                return true;

            var pageText = driver.FindElement(By.TagName("body")).Text.ToLowerInvariant();
            if (pageText.Contains("what can i help with"))
                return true;
        }
        catch
        {
        }

        return false;
    }

    private static bool IsLoginButtonVisible(ChromeDriver driver)
    {
        try
        {
            var loginButtons = driver.FindElements(
                By.CssSelector("[data-testid='login-button']"));
            return loginButtons.Count > 0 && loginButtons.Any(e => e.Displayed);
        }
        catch
        {
            return false;
        }
    }
}

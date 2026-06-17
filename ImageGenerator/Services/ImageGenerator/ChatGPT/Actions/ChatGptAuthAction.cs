using System.Text.Json;
using ImageGenerator.Services.AccountStorage.Interfaces;
using ImageGenerator.Services.Browser;
using ImageGenerator.Services.ImageGenerator.ChatGPT.Models;
using ImageGenerator.Services.ImageGenerator.ChatGPT.Modules.Interfaces;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace ImageGenerator.Services.ImageGenerator.ChatGPT.Actions;

public class ChatGptAuthAction : IChatGptAuthAction
{
    private readonly IBrowserDriverFactory _driverFactory;
    private readonly IAccountStorage _accountStorage;
    private readonly IChatGptPageDetectorModule _detector;
    private readonly IChatGptCredentialsModule _credentialsModule;
    private readonly ILogger<ChatGptAuthAction> _logger;
    private static readonly Random _rng = new();

    private const int LoginTimeoutSeconds = 30;
    private const int ShortTimeoutSeconds = 10;

    public ChatGptAuthAction(
        IBrowserDriverFactory driverFactory,
        IAccountStorage accountStorage,
        IChatGptPageDetectorModule detector,
        IChatGptCredentialsModule credentialsModule,
        ILogger<ChatGptAuthAction> logger)
    {
        _driverFactory = driverFactory;
        _accountStorage = accountStorage;
        _detector = detector;
        _credentialsModule = credentialsModule;
        _logger = logger;
    }

    public async Task LoginAsync(ChatGptAccount account, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Logging in with credentials as {Email}", account.Email);

        var currentStatus = await _detector.DetectCurrentPageAsync();
        if (currentStatus == ChatGptPageStatus.ChatPage)
        {
            _logger.LogInformation("Already on ChatPage (auto-login), saving cookies");
            await SaveCookiesForAccountAsync(account);
            return;
        }

        await _credentialsModule.FillEmailAsync(account, cancellationToken);

        var afterEmail = await _detector.WaitForStatusAsync(
            s => s is ChatGptPageStatus.ChatPage or ChatGptPageStatus.GmailLogin or ChatGptPageStatus.LoginPage,
            LoginTimeoutSeconds);

        if (afterEmail == ChatGptPageStatus.ChatPage)
        {
            _logger.LogInformation("Auto-logged in after email submit");
            await SaveCookiesForAccountAsync(account);
            return;
        }

        if (afterEmail == ChatGptPageStatus.GmailLogin)
        {
            _logger.LogInformation("Redirected to Gmail, handling password");
            await HandleGmailLoginAsync(account, cancellationToken);
            return;
        }

        _logger.LogInformation("Still on LoginPage, filling password");
        await _credentialsModule.FillPasswordAsync(account, cancellationToken);

        var afterPassword = await _detector.WaitForExactStatusAsync(ChatGptPageStatus.ChatPage, LoginTimeoutSeconds);
        if (afterPassword)
        {
            await SaveCookiesForAccountAsync(account);
        }
    }

    public async Task HandleGmailLoginAsync(ChatGptAccount account, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Handling Gmail login for {Email}", account.Email);

        var driver = _driverFactory.Driver;
        if (driver == null) return;

        await _detector.WaitForExactStatusAsync(ChatGptPageStatus.GmailLogin, 15);

        // Email comes pre-filled from ChatGPT OAuth redirect
        // Click Next on the identifier page if still showing
        ClickGmailNext();
        Thread.Sleep(2000);

        var passwordInput = FindGmailElement(By.CssSelector("input[type='password']"), 15);
        if (passwordInput == null)
        {
            _logger.LogWarning("Gmail password input not found");
            return;
        }

        HumanLikeType(passwordInput, account.Password);
        Thread.Sleep(800);

        ClickGmailNext();

        var afterLogin = await _detector.WaitForExactStatusAsync(ChatGptPageStatus.ChatPage, LoginTimeoutSeconds);
        if (afterLogin)
        {
            await SaveCookiesForAccountAsync(account);
        }
    }

    public async Task<bool> TryRestoreCookiesAsync(ChatGptAccount account, CancellationToken cancellationToken = default)
    {
        var cookiesJson = await _accountStorage.LoadCookiesAsync(account.Email);
        if (string.IsNullOrEmpty(cookiesJson))
            return false;

        return await TryRestoreSessionFromCookiesAsync(cookiesJson);
    }

    public async Task HandleSessionExpiredAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Session expired dialog detected, clicking 'Log in'");

        var clicked = ClickDialogLogInButton();

        if (clicked)
        {
            var afterClick = await _detector.WaitForStatusAsync(
                s => s is ChatGptPageStatus.LoginPage
                         or ChatGptPageStatus.AccountChooser
                         or ChatGptPageStatus.ChatPage, ShortTimeoutSeconds);

            if (afterClick == ChatGptPageStatus.AccountChooser)
            {
                _logger.LogInformation("Session expired → Log in → account chooser, handling...");
                await HandleAccountChooserAsync(cancellationToken);
                return;
            }

            if (afterClick == ChatGptPageStatus.ChatPage)
                return;

            _logger.LogInformation("Session expired → Log in → LoginPage");
            return;
        }

        _logger.LogWarning("Direct button click failed, trying banner handler fallback");
        var bannerClicked = await TryClickLogInFallbackAsync();

        if (bannerClicked)
        {
            var afterFallback = await _detector.WaitForStatusAsync(
                s => s is ChatGptPageStatus.LoginPage
                         or ChatGptPageStatus.AccountChooser
                         or ChatGptPageStatus.ChatPage, ShortTimeoutSeconds);

            if (afterFallback == ChatGptPageStatus.AccountChooser)
            {
                await HandleAccountChooserAsync(cancellationToken);
                return;
            }

            if (afterFallback == ChatGptPageStatus.ChatPage)
                return;

            return;
        }

        _logger.LogWarning("All click attempts failed, navigating directly to login");
        _driverFactory.NavigateToUrl("https://chatgpt.com/auth/login");
        await _detector.WaitForExactStatusAsync(ChatGptPageStatus.LoginPage, ShortTimeoutSeconds);
    }

    public async Task HandleAccountChooserAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Account chooser modal detected, clicking 'Log in to another account'");

        var driver = _driverFactory.Driver;
        if (driver == null) return;

        try
        {
            var buttons = driver.FindElements(By.TagName("button"));
            foreach (var btn in buttons)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (btn.Text.Contains("Log in to another account", StringComparison.OrdinalIgnoreCase))
                {
                    NaturalClick(btn);
                    _logger.LogInformation("Clicked 'Log in to another account'");

                    await _detector.WaitForExactStatusAsync(ChatGptPageStatus.LoginPage, ShortTimeoutSeconds);
                    return;
                }
            }

            _logger.LogWarning("'Log in to another account' button not found, navigating directly");
            _driverFactory.NavigateToUrl("https://chatgpt.com/auth/login");
            await _detector.WaitForExactStatusAsync(ChatGptPageStatus.LoginPage, ShortTimeoutSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to handle account chooser: {Message}", ex.Message);
            _driverFactory.NavigateToUrl("https://chatgpt.com/auth/login");
            await _detector.WaitForExactStatusAsync(ChatGptPageStatus.LoginPage, ShortTimeoutSeconds);
        }
    }

    private bool ClickDialogLogInButton()
    {
        var driver = _driverFactory.Driver;
        if (driver == null) return false;

        try
        {
            var dialogs = driver.FindElements(By.CssSelector(
                "div[role='dialog'][data-state='open']"));

            foreach (var dialog in dialogs)
            {
                var text = dialog.Text.ToLowerInvariant();
                if (!text.Contains("session has expired"))
                    continue;

                var buttons = dialog.FindElements(By.TagName("button"));
                foreach (var btn in buttons)
                {
                    var btnText = btn.Text.Trim();
                    if (btnText.Equals("Log in", StringComparison.OrdinalIgnoreCase)
                        || btnText.Equals("Sign in", StringComparison.OrdinalIgnoreCase))
                    {
                        if (btn.Displayed && btn.Enabled)
                        {
                            _logger.LogInformation("Clicking '{ButtonText}' in session expired dialog", btnText);
                            NaturalClick(btn);
                            Thread.Sleep(1500);
                            return true;
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private async Task<bool> TryClickLogInFallbackAsync()
    {
        var driver = _driverFactory.Driver;
        if (driver == null) return false;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var logInButtons = driver.FindElements(
                    By.XPath("//button[.//text()[contains(., 'Log in')]]"));

                foreach (var btn in logInButtons)
                {
                    if (btn.Displayed && btn.Enabled)
                    {
                        NaturalClick(btn);
                        _logger.LogInformation("Clicked Log in (fallback)");
                        Thread.Sleep(1000);
                        return await _detector.WaitForExactStatusAsync(ChatGptPageStatus.LoginPage, ShortTimeoutSeconds);
                    }
                }
            }
            catch
            {
            }
        }

        return false;
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

            var status = await _detector.WaitForStatusAsync(
                s => s is ChatGptPageStatus.ChatPage or ChatGptPageStatus.SessionExpired, 15);

            if (status == ChatGptPageStatus.ChatPage)
            {
                _logger.LogInformation("Session restored from cookies");
                return true;
            }

            if (status == ChatGptPageStatus.SessionExpired)
            {
                _logger.LogInformation("Cookies expired for this account, will login with credentials");
                return false;
            }

            _logger.LogInformation("Cookies restoration did not lead to ChatPage, need to login");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to restore session: {Message}", ex.Message);
            return false;
        }
    }

    private async Task SaveCookiesForAccountAsync(ChatGptAccount account)
    {
        var driver = _driverFactory.Driver;
        if (driver == null) return;

        try
        {
            var cookieDataList = driver.Manage().Cookies.AllCookies
                .Select(ToCookieData).ToList();
            var json = JsonSerializer.Serialize(cookieDataList);
            await _accountStorage.SaveCookiesAsync(account.Email, json);
            _logger.LogInformation("Cookies saved for {Email}", account.Email);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to save cookies: {Message}", ex.Message);
        }
    }

    private void NaturalClick(IWebElement element)
    {
        var driver = _driverFactory.Driver;
        if (driver == null) { element.Click(); return; }

        try
        {
            var actions = new OpenQA.Selenium.Interactions.Actions(driver);
            var offsetX = _rng.Next(1, Math.Max(1, element.Size.Width - 1));
            var offsetY = _rng.Next(1, Math.Max(1, element.Size.Height - 1));

            actions
                .MoveToElement(element, offsetX, offsetY)
                .Pause(TimeSpan.FromMilliseconds(_rng.Next(50, 200)))
                .Click()
                .Perform();
            element.Click();
        }
        catch
        {
            element.Click();
        }
    }

    private void ClickGmailNext()
    {
        var driver = _driverFactory.Driver;
        if (driver == null) return;

        try
        {
            var nextButtons = driver.FindElements(By.CssSelector(
                "#identifierNext button, #passwordNext button, " +
                "button[jsname='V67aGc'], " +
                "div[role='button'][jsname]"));
            foreach (var btn in nextButtons)
            {
                var text = btn.Text.Trim().ToLowerInvariant();
                if (text is "next" or "далее")
                {
                    if (btn.Displayed && btn.Enabled)
                    {
                        NaturalClick(btn);
                        return;
                    }
                }
            }

            var allButtons = driver.FindElements(By.TagName("button"));
            foreach (var btn in allButtons)
            {
                var text = btn.Text.Trim().ToLowerInvariant();
                if (text is "next" or "далее" && btn.Displayed && btn.Enabled)
                {
                    NaturalClick(btn);
                    return;
                }
            }
        }
        catch
        {
        }
    }

    private IWebElement? FindGmailElement(By by, int timeoutSeconds)
    {
        var driver = _driverFactory.Driver;
        if (driver == null) return null;

        try
        {
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutSeconds))
            {
                PollingInterval = TimeSpan.FromMilliseconds(200)
            };

            return wait.Until(d =>
            {
                try
                {
                    var el = d.FindElement(by);
                    return el != null && el.Displayed && el.Enabled ? el : null;
                }
                catch
                {
                    return null;
                }
            });
        }
        catch
        {
            return null;
        }
    }

    private void HumanLikeType(IWebElement element, string text)
    {
        element.Click();

        if (string.IsNullOrEmpty(text))
            return;

        element.Clear();

        foreach (var c in text)
        {
            element.SendKeys(c.ToString());
            Thread.Sleep(_rng.Next(40, 120));

            if (_rng.Next(30) == 0)
                Thread.Sleep(_rng.Next(200, 400));
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

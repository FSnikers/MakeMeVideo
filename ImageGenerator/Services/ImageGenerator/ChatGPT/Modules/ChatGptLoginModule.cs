using System.Text.Json;
using ImageGenerator.Services.AccountStorage.Interfaces;
using ImageGenerator.Services.Browser;
using ImageGenerator.Services.ImageGenerator.ChatGPT.Models;
using ImageGenerator.Services.ImageGenerator.ChatGPT.Modules.Interfaces;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;

namespace ImageGenerator.Services.ImageGenerator.ChatGPT.Modules;

public class ChatGptLoginModule : IChatGptLoginModule
{
    private readonly IBrowserDriverFactory _driverFactory;
    private readonly IAccountStorage _accountStorage;
    private readonly ILogger<ChatGptLoginModule> _logger;

    private const int ShortTimeoutSeconds = 10;

    private static readonly string[] LoginInputSelectors =
    [
        "input[name='email']",
        "input[type='email']",
        "input[placeholder*='email' i]",
        "input[name='username']"
    ];

    private static readonly string[] PasswordInputSelectors =
    [
        "input[name='password']",
        "input[type='password']",
        "input[placeholder*='password' i]"
    ];

    private static readonly string[] SubmitButtonSelectors =
    [
        "button[type='submit']",
        "button:has-text('Continue')",
        "button:has-text('Log in')",
        "button:has-text('Sign in')",
        "button[data-action='submit']"
    ];

    private static readonly Random _rng = new();

    public ChatGptLoginModule(
        IBrowserDriverFactory driverFactory,
        IAccountStorage accountStorage,
        ILogger<ChatGptLoginModule> logger)
    {
        _driverFactory = driverFactory;
        _accountStorage = accountStorage;
        _logger = logger;
    }

    public async Task LoginToChatGptAsync(ChatGptAccount account, CancellationToken cancellationToken = default)
    {
        var driver = _driverFactory.Driver;
        if (driver == null)
            throw new InvalidOperationException("Driver is not initialized");

        cancellationToken.ThrowIfCancellationRequested();

        _driverFactory.NavigateToUrl("https://chatgpt.com/auth/login");

        var cookiesJson = await _accountStorage.LoadCookiesAsync(account.Email);
        if (!string.IsNullOrEmpty(cookiesJson))
        {
            if (TryRestoreSession(cookiesJson))
                return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        await LoginWithCredentialsAsync(account);

        var cookiesToSave = driver.Manage().Cookies.AllCookies.ToList();
        var cookieDataList = cookiesToSave.Select(ToCookieData).ToList();
        var cookiesJsonToSave = JsonSerializer.Serialize(cookieDataList);
        await _accountStorage.SaveCookiesAsync(account.Email, cookiesJsonToSave);

        _logger.LogInformation("Successfully logged in as {Email}", account.Email);
    }

    private bool TryRestoreSession(string cookiesJson)
    {
        var driver = _driverFactory.Driver;
        try
        {
            if (driver == null) return false;

            driver.Manage().Cookies.DeleteAllCookies();

            var cookieDataList = JsonSerializer.Deserialize<List<CookieData>>(cookiesJson);
            if (cookieDataList == null || cookieDataList.Count == 0)
                return false;

            _driverFactory.NavigateToUrl("https://chatgpt.com");

            foreach (var cd in cookieDataList)
            {
                try
                {
                    var cookie = FromCookieData(cd);
                    driver.Manage().Cookies.AddCookie(cookie);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to add cookie {Name}: {Message}", cd.Name, ex.Message);
                }
            }

            driver.Navigate().Refresh();
            _driverFactory.WaitForPageLoad();
            Thread.Sleep(3000);

            if (driver.Url.Contains("chatgpt.com") &&
                !driver.Url.Contains("auth") &&
                !driver.Url.Contains("login"))
            {
                _logger.LogInformation("Session restored from cookies");
                return true;
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

    private async Task LoginWithCredentialsAsync(ChatGptAccount account)
    {
        var driver = _driverFactory.Driver;
        if (driver == null || account == null)
            throw new InvalidOperationException("Driver or account not initialized");

        _logger.LogInformation("Logging in as {Email}", account.Email);

        try
        {
            _driverFactory.NavigateToUrl("https://chatgpt.com/auth/login");
            RandomDelay(1500, 3000);

            var emailInput = FindElementWithFallback(LoginInputSelectors, ShortTimeoutSeconds);
            if (emailInput != null)
            {
                HumanLikeType(emailInput, account.Email);
                RandomDelay(800, 1500);

                var continueBtn = FindElementWithFallback(SubmitButtonSelectors, ShortTimeoutSeconds);
                continueBtn?.Click();
                RandomDelay(2000, 3000);
            }

            var passwordInput = FindElementWithFallback(PasswordInputSelectors, ShortTimeoutSeconds);
            if (passwordInput != null)
            {
                HumanLikeType(passwordInput, account.Password);
                RandomDelay(800, 1500);

                var loginBtn = FindElementWithFallback(SubmitButtonSelectors, ShortTimeoutSeconds);
                loginBtn?.Click();
            }
            else
            {
                var allInputs = driver.FindElements(By.TagName("input"));
                if (allInputs.Count >= 1)
                {
                    HumanLikeType(allInputs[0], account.Email);
                    RandomDelay(800, 1500);

                    var btns = FindSubmitButtons();
                    btns.FirstOrDefault()?.Click();
                    RandomDelay(2000, 3000);

                    if (allInputs.Count >= 2)
                    {
                        HumanLikeType(allInputs[1], account.Password);
                        RandomDelay(800, 1500);
                        btns.FirstOrDefault()?.Click();
                    }
                }
            }

            WaitForLoginComplete();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Standard login failed, trying alternative: {Message}", ex.Message);
            await AlternativeLoginAsync(account);
        }
    }

    private async Task AlternativeLoginAsync(ChatGptAccount account)
    {
        var driver = _driverFactory.Driver;
        if (driver == null || account == null) return;

        _driverFactory.NavigateToUrl("https://chatgpt.com/auth/login");
        Thread.Sleep(3000);

        var allInputs = driver.FindElements(By.TagName("input")).ToList();
        var allButtons = driver.FindElements(By.TagName("button")).ToList();

        _logger.LogInformation("Found {InputCount} inputs, {ButtonCount} buttons on login page",
            allInputs.Count, allButtons.Count);

        if (allInputs.Count > 0)
        {
            var emailField = allInputs[0];
            HumanLikeType(emailField, account.Email);
            RandomDelay(800, 1500);

            var submitBtns = allButtons.Where(b =>
            {
                var text = b.Text.ToLowerInvariant();
                return text.Contains("continue") || text.Contains("submit") ||
                       text.Contains("next") || text.Contains("log") ||
                       text.Contains("sign");
            }).ToList();

            var firstRelevant = submitBtns.FirstOrDefault() ?? allButtons.FirstOrDefault();
            firstRelevant?.Click();
            RandomDelay(2000, 3000);
        }

        var inputsAfter = driver.FindElements(By.TagName("input")).ToList();
        if (inputsAfter.Count > 0)
        {
            var passwordField = inputsAfter[0];
            HumanLikeType(passwordField, account.Password);
            RandomDelay(800, 1500);

            var buttonsAfter = driver.FindElements(By.TagName("button")).ToList();
            var submitBtns2 = buttonsAfter.Where(b =>
            {
                var text = b.Text.ToLowerInvariant();
                return text.Contains("continue") || text.Contains("submit") ||
                       text.Contains("log") || text.Contains("sign") ||
                       text.Contains("enter");
            }).ToList();

            var btn = submitBtns2.FirstOrDefault() ?? buttonsAfter.FirstOrDefault();
            btn?.Click();
        }

        WaitForLoginComplete();
    }

    private void WaitForLoginComplete()
    {
        var driver = _driverFactory.Driver;
        if (driver == null) return;
        try
        {
            var tempWait = new WebDriverWait(driver, TimeSpan.FromSeconds(30))
            {
                PollingInterval = TimeSpan.FromMilliseconds(500)
            };

            tempWait.Until(d =>
            {
                var url = d.Url.ToLowerInvariant();
                return (url.Contains("chatgpt.com") && !url.Contains("auth") && !url.Contains("login")) ||
                       d.FindElements(By.CssSelector("#prompt-textarea")).Count > 0 ||
                       d.FindElements(By.CssSelector("textarea")).Count > 0 ||
                       d.FindElements(By.CssSelector("div[contenteditable='true']")).Count > 0 ||
                       d.FindElements(By.CssSelector("nav")).Count > 0 ||
                       d.FindElements(By.CssSelector("[data-message-author-role]")).Count > 0 ||
                       d.PageSource.Contains("What can I help with") ||
                       d.PageSource.Contains("ChatGPT") && !url.Contains("auth") && !url.Contains("login");
            });

            _logger.LogInformation("Login successful");
        }
        catch (WebDriverTimeoutException)
        {
            _logger.LogWarning("Login wait timeout");
        }
    }

    private static void HumanLikeType(IWebElement element, string text)
    {
        element.Click();

        if (string.IsNullOrEmpty(text))
            return;

        if (text.Length > 50)
        {
            foreach (var c in text)
            {
                element.SendKeys(c.ToString());
                var baseDelay = text.Length > 200 ? 5 : 15;
                var variation = _rng.Next(5, 25);
                Thread.Sleep(baseDelay + variation);

                if (_rng.Next(50) == 0)
                    Thread.Sleep(_rng.Next(200, 500));
            }
        }
        else
        {
            element.Clear();

            foreach (var c in text)
            {
                element.SendKeys(c.ToString());
                Thread.Sleep(_rng.Next(40, 120));

                if (_rng.Next(30) == 0)
                    Thread.Sleep(_rng.Next(200, 400));
            }
        }
    }

    private static void RandomDelay(int minMs = 300, int maxMs = 800)
    {
        Thread.Sleep(_rng.Next(minMs, maxMs));
    }

    private void NaturalClick(IWebElement element)
    {
        var driver = _driverFactory.Driver;
        if (driver == null) { element.Click(); return; }
        var actions = new Actions(driver);
        var offsetX = _rng.Next(1, Math.Max(1, element.Size.Width - 1));
        var offsetY = _rng.Next(1, Math.Max(1, element.Size.Height - 1));

        actions
            .MoveToElement(element, offsetX, offsetY)
            .Pause(TimeSpan.FromMilliseconds(_rng.Next(50, 200)))
            .Click()
            .Perform();
    }

    private void ScrollToElement(IWebElement element)
    {
        var driver = _driverFactory.Driver;
        if (driver == null) return;
        try
        {
            var y = element.Location.Y - _rng.Next(100, 300);
            driver.ExecuteScript($"window.scrollTo({{ top: {y}, behavior: 'smooth' }});");
            Thread.Sleep(_rng.Next(300, 700));
        }
        catch
        {
        }
    }

    private IWebElement? FindElementWithFallback(string[] selectors, int timeoutSeconds)
    {
        var driver = _driverFactory.Driver;
        if (driver == null) return null;

        foreach (var selector in selectors)
        {
            try
            {
                var tempWait = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutSeconds))
                {
                    PollingInterval = TimeSpan.FromMilliseconds(200)
                };

                var element = tempWait.Until(d =>
                {
                    try
                    {
                        var el = d.FindElement(By.CssSelector(selector));
                        return el != null && el.Displayed && el.Enabled ? el : null;
                    }
                    catch
                    {
                        return null;
                    }
                });

                if (element != null)
                    return element;
            }
            catch
            {
            }
        }

        return null;
    }

    private List<IWebElement> FindSubmitButtons()
    {
        var driver = _driverFactory.Driver;
        if (driver == null) return [];

        return driver.FindElements(By.TagName("button"))
            .Where(b =>
            {
                try
                {
                    var text = b.Text.ToLowerInvariant();
                    var type = b.GetAttribute("type")?.ToLowerInvariant() ?? "";
                    var ariaLabel = b.GetAttribute("aria-label")?.ToLowerInvariant() ?? "";

                    return text.Contains("continue") || text.Contains("submit") ||
                           text.Contains("log in") || text.Contains("sign in") ||
                           text.Contains("next") || text.Contains("send") ||
                           type == "submit" ||
                           ariaLabel.Contains("continue") || ariaLabel.Contains("submit");
                }
                catch
                {
                    return false;
                }
            })
            .ToList();
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
            var dt = DateTimeOffset.FromUnixTimeSeconds(cd.Expiry.Value).UtcDateTime;
            if (dt > DateTime.UtcNow)
                expiry = dt;
        }

        var cookie = new OpenQA.Selenium.Cookie(
            cd.Name,
            cd.Value,
            cd.Domain,
            cd.Path,
            expiry);

        return cookie;
    }
}

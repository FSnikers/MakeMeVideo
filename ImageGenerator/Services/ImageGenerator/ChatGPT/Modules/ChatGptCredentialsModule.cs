using ImageGenerator.Services.Browser;
using ImageGenerator.Services.ImageGenerator.ChatGPT.Models;
using ImageGenerator.Services.ImageGenerator.ChatGPT.Modules.Interfaces;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace ImageGenerator.Services.ImageGenerator.ChatGPT.Modules;

public class ChatGptCredentialsModule : IChatGptCredentialsModule
{
    private readonly IBrowserDriverFactory _driverFactory;
    private readonly IChatGptPageDetectorModule _pageDetector;
    private readonly ILogger<ChatGptCredentialsModule> _logger;

    private const int ShortTimeoutSeconds = 10;

    private static readonly string[] EmailInputSelectors =
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

    public ChatGptCredentialsModule(
        IBrowserDriverFactory driverFactory,
        IChatGptPageDetectorModule pageDetector,
        ILogger<ChatGptCredentialsModule> logger)
    {
        _driverFactory = driverFactory;
        _pageDetector = pageDetector;
        _logger = logger;
    }

    public async Task FillCredentialsAsync(ChatGptAccount account, CancellationToken cancellationToken = default)
    {
        var driver = _driverFactory.Driver;
        if (driver == null || account == null)
            throw new InvalidOperationException("Driver or account not initialized");

        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation("Filling credentials for {Email}", account.Email);

        var emailInput = FindElementWithFallback(EmailInputSelectors, ShortTimeoutSeconds);
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
            await FallbackFillAsync(account);
        }
    }

    private async Task FallbackFillAsync(ChatGptAccount account)
    {
        var driver = _driverFactory.Driver;
        if (driver == null) return;

        var allInputs = driver.FindElements(By.TagName("input")).ToList();
        var allButtons = driver.FindElements(By.TagName("button")).ToList();

        if (allInputs.Count == 0) return;

        HumanLikeType(allInputs[0], account.Email);
        RandomDelay(800, 1500);

        var submitBtns = allButtons.Where(b =>
        {
            var text = b.Text.ToLowerInvariant();
            return text.Contains("continue") || text.Contains("submit") ||
                   text.Contains("next") || text.Contains("log") ||
                   text.Contains("sign");
        }).ToList();

        (submitBtns.FirstOrDefault() ?? allButtons.FirstOrDefault())?.Click();
        RandomDelay(2000, 3000);

        var inputsAfter = driver.FindElements(By.TagName("input")).ToList();
        if (inputsAfter.Count >= 1)
        {
            HumanLikeType(inputsAfter[0], account.Password);
            RandomDelay(800, 1500);

            var buttonsAfter = driver.FindElements(By.TagName("button")).ToList();
            var submitBtns2 = buttonsAfter.Where(b =>
            {
                var text = b.Text.ToLowerInvariant();
                return text.Contains("continue") || text.Contains("submit") ||
                       text.Contains("log") || text.Contains("sign") ||
                       text.Contains("enter");
            }).ToList();

            (submitBtns2.FirstOrDefault() ?? buttonsAfter.FirstOrDefault())?.Click();
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

        try
        {
            var seleniumActions = new OpenQA.Selenium.Interactions.Actions(driver);
            var offsetX = _rng.Next(1, Math.Max(1, element.Size.Width - 1));
            var offsetY = _rng.Next(1, Math.Max(1, element.Size.Height - 1));

            seleniumActions
                .MoveToElement(element, offsetX, offsetY)
                .Pause(TimeSpan.FromMilliseconds(_rng.Next(50, 200)))
                .Click()
                .Perform();
        }
        catch
        {
            element.Click();
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
}

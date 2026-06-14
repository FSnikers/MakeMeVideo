using ImageGenerator.Services.Browser;
using ImageGenerator.Services.ImageGenerator.ChatGPT.Models;
using ImageGenerator.Services.ImageGenerator.ChatGPT.Modules.Interfaces;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;

namespace ImageGenerator.Services.ImageGenerator.ChatGPT.Modules;

public class ChatGptBannerHandlerModule : IChatGptBannerHandlerModule
{
    private readonly IBrowserDriverFactory _driverFactory;
    private readonly IChatGptPageDetectorModule _pageDetector;
    private readonly ILogger<ChatGptBannerHandlerModule> _logger;
    private static readonly Random _rng = new();

    public ChatGptBannerHandlerModule(
        IBrowserDriverFactory driverFactory,
        IChatGptPageDetectorModule pageDetector,
        ILogger<ChatGptBannerHandlerModule> logger)
    {
        _driverFactory = driverFactory;
        _pageDetector = pageDetector;
        _logger = logger;
    }

    public async Task<bool> TryClickLogInAsync()
    {
        _logger.LogInformation("Attempting to click Log in on expired session banner");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (ClickLogInButton())
            {
                var reachedLogin = await _pageDetector.WaitForExactStatusAsync(
                    ChatGptPageStatus.LoginPage, 10);

                if (reachedLogin)
                {
                    _logger.LogInformation("Successfully transitioned from expired banner to login page");
                    return true;
                }

                _logger.LogWarning("Click Log in attempt {Attempt} did not transition to login page", attempt + 1);
            }
        }

        _logger.LogWarning("Failed to click Log in on expired session banner after 2 attempts");
        return false;
    }

    private bool ClickLogInButton()
    {
        var driver = _driverFactory.Driver;
        if (driver == null) return false;

        try
        {
            var logInButtons = driver.FindElements(
                By.XPath("//button[.//text()[contains(., 'Log in')]]"));

            foreach (var btn in logInButtons)
            {
                try
                {
                    if (btn.Displayed && btn.Enabled)
                    {
                        NaturalClick(btn);
                        _logger.LogInformation("Clicked Log in on expired session banner");
                        Thread.Sleep(1000);
                        return true;
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        return false;
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
}

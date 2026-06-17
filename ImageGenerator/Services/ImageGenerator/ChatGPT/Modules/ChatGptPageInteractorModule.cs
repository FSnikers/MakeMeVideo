using ImageGenerator.Services.Browser;
using ImageGenerator.Services.ImageGenerator.ChatGPT.Modules.Interfaces;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace ImageGenerator.Services.ImageGenerator.ChatGPT.Modules;

public class ChatGptPageInteractorModule : IChatGptPageInteractorModule
{
    private readonly IBrowserDriverFactory _driverFactory;
    private readonly ILogger<ChatGptPageInteractorModule> _logger;
    private readonly Random _rng = new();

    private static readonly string[] PromptTextareaSelectors =
    [
        "#prompt-textarea",
        "textarea[placeholder*='Message']",
        "textarea[placeholder*='message']",
        "textarea[placeholder*='Send' i]",
        "div[contenteditable='true']",
        "div[contenteditable='true'] p",
        "div[role='textbox']",
        "div[data-message-author-role] + div div[contenteditable]"
    ];

    private static readonly string[] SendButtonSelectors =
    [
        "button[data-testid='send-button']",
        "button[aria-label='Send prompt']",
        "button[aria-label='Send']",
        "button[data-testid='fruitfly-send-button']",
        "button[type='submit']:not([hidden])",
        "button:has(svg):not([disabled])"
    ];

    private static readonly string[] StopButtonSelectors =
    [
        "button[data-testid='stop-button']",
        "button[aria-label='Stop generating']",
        "button[aria-label='Stop']",
        "button:has(svg):not([disabled]) svg path[d*='M6 6']"
    ];

    public ChatGptPageInteractorModule(
        IBrowserDriverFactory driverFactory,
        ILogger<ChatGptPageInteractorModule> logger)
    {
        _driverFactory = driverFactory;
        _logger = logger;
    }

    public IWebElement? FindPromptTextarea(int timeoutSeconds)
    {
        var textarea = FindElementWithFallback(PromptTextareaSelectors, timeoutSeconds);
        if (textarea != null)
            return textarea;

        var driver = _driverFactory.Driver;
        if (driver == null)
            return null;

        var allTextareas = driver.FindElements(By.TagName("textarea")).ToList();
        if (allTextareas.Count > 0)
            return allTextareas[0];

        var contenteditables = driver.FindElements(By.CssSelector("div[contenteditable='true']")).ToList();
        return contenteditables.Count > 0 ? contenteditables[0] : null;
    }

    public IWebElement? FindSendButton(int timeoutSeconds)
    {
        return FindElementWithFallback(SendButtonSelectors, timeoutSeconds);
    }

    public IWebElement? FindStopButton(int timeoutSeconds)
    {
        return FindElementWithFallback(StopButtonSelectors, timeoutSeconds);
    }

    public void TypeText(IWebElement element, string text, bool fastInput = false)
    {
        if (fastInput)
        {
            TypeFastText(element, text);
            return;
        }
        
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

    private void TypeFastText(IWebElement element, string text)
    {
        element.Click();

        if (string.IsNullOrEmpty(text))
            return;

        if (text.Length > 50)
        {
                element.SendKeys(text);
        }
        else
        {
            element.Clear();

            foreach (var c in text)
            {
                element.SendKeys(c.ToString());
                Thread.Sleep(_rng.Next(4, 12));
            }
        }
    }

    public void RandomDelay(int minMs = 300, int maxMs = 800)
    {
        Thread.Sleep(new Random().Next(minMs, maxMs));
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
}

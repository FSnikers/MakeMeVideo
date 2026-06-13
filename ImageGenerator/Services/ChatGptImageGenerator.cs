using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ImageGenerator.Interfaces;
using ImageGenerator.Models;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;

namespace ImageGenerator.Services;

public class ChatGptImageGenerator : IImageGenerator, IDisposable
{
    private readonly IAccountStorage _accountStorage;
    private readonly ILogger<ChatGptImageGenerator> _logger;
    private readonly string _outputDirectory;
    private ChromeDriver? _driver;
    private WebDriverWait? _wait;
    private ChatGptAccount? _currentAccount;
    private bool _disposed;

    private const int DefaultTimeoutSeconds = 300;
    private const int ShortTimeoutSeconds = 10;
    private const int PollingIntervalMs = 1000;
    private const int MaxAccountSwitchAttempts = 3;
    private const long MaxImageBytes = 20 * 1024 * 1024;

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

    private static readonly string[] ImageSelectors =
    [
        "figure img[src*='data:image']",
        "figure img[src*='chatgpt.com' i]",
        "figure img[src*='oaidalleapiprodscus' i]",
        "img[alt*='image' i]",
        "img[alt*='generated' i]",
        "figure img",
        "[data-message-author-role='assistant'] img"
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
        if (_driver == null) { element.Click(); return; }
        var actions = new Actions(_driver);
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
        if (_driver == null) return;
        try
        {
            var y = element.Location.Y - _rng.Next(100, 300);
            _driver.ExecuteScript($"window.scrollTo({{ top: {y}, behavior: 'smooth' }});");
            Thread.Sleep(_rng.Next(300, 700));
        }
        catch
        {
        }
    }

    public ChatGptImageGenerator(
        IAccountStorage accountStorage,
        ILogger<ChatGptImageGenerator> logger,
        string outputDirectory = "./output",
        bool headless = false)
    {
        _accountStorage = accountStorage;
        _logger = logger;
        _outputDirectory = outputDirectory;

        LaunchDriver(headless);
    }

    public async Task<GenerationResult> GenerateImageAsync(GenerationRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var lastException = (Exception?)null;

        for (var attempt = 0; attempt < MaxAccountSwitchAttempts; attempt++)
        {
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
                await LoginToChatGptAsync();
                var result = await SendPromptAndWaitForImageAsync(request.Prompt);

                if (result.IsSuccess)
                {
                    await _accountStorage.MarkAccountUsedAsync(_currentAccount);
                    stopwatch.Stop();
                    result.Duration = stopwatch.Elapsed;
                    _logger.LogInformation("Image generated successfully with account {Email} in {Duration}",
                        _currentAccount.Email, stopwatch.Elapsed);
                    return result;
                }

                if (result.ErrorType == "LimitReached")
                {
                    _logger.LogWarning("Limit reached for account {Email}, switching...", _currentAccount.Email);
                    await _accountStorage.MarkAccountLimitReachedAsync(_currentAccount);
                    NavigateToNewChat();
                    continue;
                }

                if (result.ErrorType == "ProhibitedContent" || result.ErrorType == "ContentPolicyViolation")
                {
                    stopwatch.Stop();
                    result.Duration = stopwatch.Elapsed;
                    return result;
                }

                if (result.ErrorType == "Timeout" && attempt < MaxAccountSwitchAttempts - 1)
                {
                    _logger.LogWarning("Timeout on account {Email}, retrying with next account...", _currentAccount.Email);
                    NavigateToNewChat();
                    continue;
                }

                stopwatch.Stop();
                result.Duration = stopwatch.Elapsed;
                return result;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogError(ex, "Error generating image with account {Email}", _currentAccount.Email);

                if (attempt < MaxAccountSwitchAttempts - 1)
                {
                    _logger.LogInformation("Retrying with next account...");
                    RestartDriver();
                }
            }
        }

        stopwatch.Stop();
        return GenerationResult.Failure(
            $"Не удалось сгенерировать изображение после {MaxAccountSwitchAttempts} попыток. " +
            $"Последняя ошибка: {lastException?.Message}",
            "MaxAttemptsReached");
    }

    public Task<bool> IsAvailableAsync()
    {
        try
        {
            _driver?.Navigate().GoToUrl("https://chatgpt.com");
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private async Task LoginToChatGptAsync()
    {
        if (_driver == null)
            throw new InvalidOperationException("Driver is not initialized");

        _driver.Navigate().GoToUrl("https://chatgpt.com/auth/login");
        WaitForPageLoad();

        var cookiesJson = await _accountStorage.LoadCookiesAsync(_currentAccount!.Email);
        if (!string.IsNullOrEmpty(cookiesJson))
        {
            if (TryRestoreSession(cookiesJson))
                return;
        }

        await LoginWithCredentialsAsync();

        var cookiesToSave = _driver.Manage().Cookies.AllCookies.ToList();
        var cookieDataList = cookiesToSave.Select(ToCookieData).ToList();
        var cookiesJsonToSave = JsonSerializer.Serialize(cookieDataList);
        await _accountStorage.SaveCookiesAsync(_currentAccount.Email, cookiesJsonToSave);

        _logger.LogInformation("Successfully logged in as {Email}", _currentAccount.Email);
    }

    private bool TryRestoreSession(string cookiesJson)
    {
        try
        {
            if (_driver == null) return false;

            _driver.Manage().Cookies.DeleteAllCookies();

            var cookieDataList = JsonSerializer.Deserialize<List<CookieData>>(cookiesJson);
            if (cookieDataList == null || cookieDataList.Count == 0)
                return false;

            _driver.Navigate().GoToUrl("https://chatgpt.com");

            foreach (var cd in cookieDataList)
            {
                try
                {
                    var cookie = FromCookieData(cd);
                    _driver.Manage().Cookies.AddCookie(cookie);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to add cookie {Name}: {Message}", cd.Name, ex.Message);
                }
            }

            _driver.Navigate().Refresh();
            WaitForPageLoad();
            Thread.Sleep(3000);

            if (_driver.Url.Contains("chatgpt.com") &&
                !_driver.Url.Contains("auth") &&
                !_driver.Url.Contains("login"))
            {
                _logger.LogInformation("Session restored from cookies for {Email}", _currentAccount?.Email);
                return true;
            }

            _logger.LogInformation("Cookies expired, need to login for {Email}", _currentAccount?.Email);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to restore session: {Message}", ex.Message);
            return false;
        }
    }

    private async Task LoginWithCredentialsAsync()
    {
        if (_driver == null || _currentAccount == null)
            throw new InvalidOperationException("Driver or account not initialized");

        _logger.LogInformation("Logging in as {Email}", _currentAccount.Email);

        try
        {
            _driver.Navigate().GoToUrl("https://chatgpt.com/auth/login");
            WaitForPageLoad();
            RandomDelay(1500, 3000);

            var emailInput = FindElementWithFallback(LoginInputSelectors, ShortTimeoutSeconds);
            if (emailInput != null)
            {
                HumanLikeType(emailInput, _currentAccount.Email);
                RandomDelay(800, 1500);

                var continueBtn = FindElementWithFallback(SubmitButtonSelectors, ShortTimeoutSeconds);
                continueBtn?.Click();
                RandomDelay(2000, 3000);
            }

            var passwordInput = FindElementWithFallback(PasswordInputSelectors, ShortTimeoutSeconds);
            if (passwordInput != null)
            {
                HumanLikeType(passwordInput, _currentAccount.Password);
                RandomDelay(800, 1500);

                var loginBtn = FindElementWithFallback(SubmitButtonSelectors, ShortTimeoutSeconds);
                loginBtn?.Click();
            }
            else
            {
                var allInputs = _driver.FindElements(By.TagName("input"));
                if (allInputs.Count >= 1)
                {
                    HumanLikeType(allInputs[0], _currentAccount.Email);
                    RandomDelay(800, 1500);

                    var btns = FindSubmitButtons();
                    btns.FirstOrDefault()?.Click();
                    RandomDelay(2000, 3000);

                    if (allInputs.Count >= 2)
                    {
                        HumanLikeType(allInputs[1], _currentAccount.Password);
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
            await AlternativeLoginAsync();
        }
    }

    private async Task AlternativeLoginAsync()
    {
        if (_driver == null || _currentAccount == null) return;

        _driver.Navigate().GoToUrl("https://chatgpt.com/auth/login");
        WaitForPageLoad();
        Thread.Sleep(3000);

        var allInputs = _driver.FindElements(By.TagName("input")).ToList();
        var allButtons = _driver.FindElements(By.TagName("button")).ToList();

        _logger.LogInformation("Found {InputCount} inputs, {ButtonCount} buttons on login page",
            allInputs.Count, allButtons.Count);

        if (allInputs.Count > 0)
        {
            var emailField = allInputs[0];
            HumanLikeType(emailField, _currentAccount.Email);
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

        var inputsAfter = _driver.FindElements(By.TagName("input")).ToList();
        if (inputsAfter.Count > 0)
        {
            var passwordField = inputsAfter[0];
            HumanLikeType(passwordField, _currentAccount.Password);
            RandomDelay(800, 1500);

            var buttonsAfter = _driver.FindElements(By.TagName("button")).ToList();
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
        try
        {
            var tempWait = new WebDriverWait(_driver, TimeSpan.FromSeconds(30))
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

            _logger.LogInformation("Login successful, current URL: {Url}", _driver?.Url);
        }
        catch (WebDriverTimeoutException)
        {
            _logger.LogWarning("Login wait timeout, current URL: {Url}", _driver?.Url);
        }
    }

    private async Task<GenerationResult> SendPromptAndWaitForImageAsync(string prompt)
    {
        if (_driver == null)
            return GenerationResult.Failure("Driver not initialized", "Exception");

        try
        {
            EnsureOnChatPage();
            Thread.Sleep(2000);

            var textarea = FindElementWithFallback(PromptTextareaSelectors, ShortTimeoutSeconds);
            if (textarea == null)
            {
                _logger.LogWarning("Could not find prompt textarea, trying JavaScript injection");
                _driver.ExecuteScript("window.location.href = 'https://chatgpt.com/';");
                Thread.Sleep(3000);
                textarea = FindElementWithFallback(PromptTextareaSelectors, ShortTimeoutSeconds);

                if (textarea == null)
                {
                    var allTextareas = _driver.FindElements(By.TagName("textarea")).ToList();
                    if (allTextareas.Count > 0)
                        textarea = allTextareas[0];
                    else
                    {
                        var contenteditables = _driver.FindElements(By.CssSelector("div[contenteditable='true']")).ToList();
                        if (contenteditables.Count > 0)
                            textarea = contenteditables[0];
                        else
                            return GenerationResult.Failure(
                                "Не удалось найти поле ввода промпта на странице ChatGPT", "NoInputField");
                    }
                }
            }

            HumanLikeType(textarea, prompt);
            RandomDelay(500, 1200);

            var sendButton = FindElementWithFallback(SendButtonSelectors, ShortTimeoutSeconds);
            if (sendButton == null || !sendButton.Enabled)
            {
                _logger.LogInformation("Send button not found/disabled, trying Enter key...");
                textarea.SendKeys(Keys.Enter);
            }
            else
            {
                try
                {
                    sendButton.Click();
                }
                catch (ElementClickInterceptedException)
                {
                    _logger.LogInformation("Send button click intercepted, using Enter key...");
                    textarea.SendKeys(Keys.Enter);
                }
            }

            _logger.LogInformation("Prompt sent, waiting for image generation...");
            return await WaitForGeneratedImageAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during prompt submission");
            return GenerationResult.Failure($"Ошибка при отправке промпта: {ex.Message}", "Exception");
        }
    }

    private async Task<GenerationResult> WaitForGeneratedImageAsync()
    {
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds);

        while (DateTime.UtcNow - startTime < timeout)
        {
            try
            {
                var pageText = _driver?.PageSource ?? "";
                var pageBody = _driver?.FindElement(By.TagName("body")).Text ?? "";

                var limitPattern = LimitErrorPatterns.FirstOrDefault(p =>
                    pageBody.Contains(p, StringComparison.OrdinalIgnoreCase));
                if (limitPattern != null)
                {
                    _logger.LogWarning("Limit detected: '{Pattern}'", limitPattern);
                    return GenerationResult.Failure(
                        $"Достигнут лимит генерации для аккаунта {_currentAccount?.Email}",
                        "LimitReached");
                }

                var prohibitedPattern = ProhibitedContentPatterns.FirstOrDefault(p =>
                    pageBody.Contains(p, StringComparison.OrdinalIgnoreCase));
                if (prohibitedPattern != null)
                {
                    _logger.LogWarning("Prohibited content detected: '{Pattern}'", prohibitedPattern);
                    return GenerationResult.Failure(
                        "ChatGPT не может сгенерировать изображение по данному промпту: " +
                        "нарушение политики безопасности", "ProhibitedContent");
                }

                var stopBtn = FindElementWithFallback(StopButtonSelectors, 2);
                if (stopBtn != null && stopBtn.Displayed)
                {
                    await Task.Delay(PollingIntervalMs);
                    continue;
                }

                var imageResult = ExtractImageFromPage();
                if (imageResult != null)
                {
                    return imageResult;
                }

                var assistantMessages = _driver?.FindElements(
                    By.CssSelector("[data-message-author-role='assistant']")).ToList();

                if (assistantMessages != null && assistantMessages.Count > 0)
                {
                    var lastMessage = assistantMessages.Last().Text;
                    if (!string.IsNullOrEmpty(lastMessage) && !lastMessage.Contains("Image") &&
                        !lastMessage.Contains("image") && !lastMessage.Contains("picture") &&
                        !lastMessage.Contains("generat"))
                    {
                        var textLower = lastMessage.ToLowerInvariant();
                        var errorIndicators = new[]
                        {
                            "sorry", "cannot", "can't", "unable", "not able to",
                            "i don't", "i'm not able", "doesn't work", "not supported",
                            "not available", "currently unavailable", "having trouble"
                        };

                        if (errorIndicators.Any(e => textLower.Contains(e)) &&
                            DateTime.UtcNow - startTime > TimeSpan.FromSeconds(15))
                        {
                            _logger.LogWarning("ChatGPT responded with error text: {Text}",
                                lastMessage[..Math.Min(200, lastMessage.Length)]);

                            if (textLower.Contains("policy") || textLower.Contains("content") ||
                                textLower.Contains("safety") || textLower.Contains("guideline") ||
                                textLower.Contains("prohibit") || textLower.Contains("violat") ||
                                textLower.Contains("not allow"))
                            {
                                return GenerationResult.Failure(
                                    "ChatGPT отклонил запрос: " + lastMessage[..Math.Min(300, lastMessage.Length)],
                                    "ProhibitedContent");
                            }

                            if (textLower.Contains("rate limit") || textLower.Contains("too many") ||
                                textLower.Contains("try again later") || textLower.Contains("limit"))
                            {
                                return GenerationResult.Failure(
                                    "Достигнут лимит генерации", "LimitReached");
                            }

                            return GenerationResult.Failure(
                                "ChatGPT не смог сгенерировать изображение: " +
                                lastMessage[..Math.Min(300, lastMessage.Length)],
                                "CantGenerate");
                        }
                    }
                }
            }
            catch (StaleElementReferenceException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error during polling: {Message}", ex.Message);
            }

            await Task.Delay(PollingIntervalMs);
        }

        _logger.LogWarning("Image generation timed out after {Timeout}", timeout);
        return GenerationResult.Failure(
            "Превышено время ожидания генерации изображения", "Timeout");
    }

    private GenerationResult? ExtractImageFromPage()
    {
        if (_driver == null) return null;

        foreach (var selector in ImageSelectors)
        {
            try
            {
                var images = _driver.FindElements(By.CssSelector(selector));
                foreach (var img in images)
                {
                    try
                    {
                        var src = img.GetAttribute("src");
                        if (string.IsNullOrEmpty(src)) continue;

                        if (src.StartsWith("data:image"))
                        {
                            var base64Data = src[(src.IndexOf(",") + 1)..];
                            var imageData = Convert.FromBase64String(base64Data);
                            var outputPath = SaveImageToFile(imageData);
                            _logger.LogInformation("Image extracted from data URI, saved to {Path}", outputPath);
                            return GenerationResult.Success(outputPath);
                        }

                        if (src.Contains("oaidalleapiprodscus") || src.Contains("chatgpt") && src.EndsWith(".png"))
                        {
                            var outputPath = DownloadAndSaveImage(src);
                            if (outputPath != null)
                            {
                                _logger.LogInformation("Image downloaded from {Url}", src);
                                return GenerationResult.Success(outputPath);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Failed to process image element: {Message}", ex.Message);
                    }
                }
            }
            catch (NoSuchElementException)
            {
            }
        }

        try
        {
            var allImgs = _driver.FindElements(By.TagName("img"));
            foreach (var img in allImgs)
            {
                try
                {
                    var src = img.GetAttribute("src");
                    if (string.IsNullOrEmpty(src)) continue;

                    if (src.StartsWith("data:image"))
                    {
                        var base64Data = src[(src.IndexOf(",") + 1)..];
                        var imageData = Convert.FromBase64String(base64Data);
                        var outputPath = SaveImageToFile(imageData);
                        _logger.LogInformation("Image extracted from data URI (fallback), saved to {Path}", outputPath);
                        return GenerationResult.Success(outputPath);
                    }

                    if (src.Contains("oaidalleapiprodscus"))
                    {
                        var outputPath = DownloadAndSaveImage(src);
                        if (outputPath != null)
                            return GenerationResult.Success(outputPath);
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

        return null;
    }

    private string SaveImageToFile(byte[] imageData)
    {
        Directory.CreateDirectory(_outputDirectory);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var fileName = $"{timestamp}_{Guid.NewGuid():N}";

        var extensions = DetectImageExtension(imageData);
        var filePath = Path.Combine(_outputDirectory, $"{fileName}{extensions}");

        File.WriteAllBytes(filePath, imageData);
        _logger.LogInformation("Image saved: {Path} ({Size} bytes)", filePath, imageData.Length);

        return filePath;
    }

    private string? DownloadAndSaveImage(string url)
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var imageData = httpClient.GetByteArrayAsync(url).GetAwaiter().GetResult();

            if (imageData.Length == 0 || imageData.Length > MaxImageBytes)
            {
                _logger.LogWarning("Image data invalid: {Length} bytes", imageData.Length);
                return null;
            }

            return SaveImageToFile(imageData);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to download image from {Url}: {Message}", url, ex.Message);
            return null;
        }
    }

    private static string DetectImageExtension(byte[] data)
    {
        if (data.Length < 4) return ".png";

        if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return ".jpg";

        if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return ".png";

        if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46)
            return ".gif";

        if (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46)
            return ".webp";

        if (data[0] == 0x42 && data[1] == 0x4D)
            return ".bmp";

        return ".png";
    }

    private void EnsureOnChatPage()
    {
        if (_driver == null) return;

        var currentUrl = _driver.Url.ToLowerInvariant();
        if (!currentUrl.Contains("chatgpt.com") || currentUrl.Contains("auth") || currentUrl.Contains("login"))
        {
            _driver.Navigate().GoToUrl("https://chatgpt.com/");
            WaitForPageLoad();
            Thread.Sleep(2000);
        }
    }

    private void NavigateToNewChat()
    {
        try
        {
            _driver?.Navigate().GoToUrl("https://chatgpt.com/");
            WaitForPageLoad();
            Thread.Sleep(2000);
        }
        catch
        {
            RestartDriver();
        }
    }

    private void LaunchDriver(bool headless = false)
    {
        var options = CreateChromeOptions(headless);

        var service = ChromeDriverService.CreateDefaultService();
        service.HideCommandPromptWindow = true;
        service.SuppressInitialDiagnosticInformation = true;

        _driver = new ChromeDriver(service, options);
        _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(DefaultTimeoutSeconds))
        {
            PollingInterval = TimeSpan.FromMilliseconds(PollingIntervalMs)
        };

        _driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(60);
        ApplyAntiDetectionMeasures();
    }

    private static ChromeOptions CreateChromeOptions(bool headless)
    {
        var options = new ChromeOptions();

        options.AddArgument("--no-first-run");
        options.AddArgument("--no-default-browser-check");
        options.AddArgument("--disable-search-engine-choice-screen");

        var userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                        "(KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";
        options.AddArgument($"--user-agent={userAgent}");

        options.AddArgument("--disable-blink-features=AutomationControlled");
        options.AddArgument("--disable-dev-shm-usage");
        options.AddArgument("--no-sandbox");
        options.AddArgument("--disable-notifications");
        options.AddArgument("--lang=en-US");
        options.AddArgument("--disable-renderer-backgrounding");
        options.AddArgument("--disable-background-timer-throttling");
        options.AddArgument("--disable-backgrounding-occluded-windows");

        options.AddExcludedArgument("enable-automation");
        options.AddAdditionalOption("useAutomationExtension", false);

        options.AddUserProfilePreference("credentials_enable_service", false);
        options.AddUserProfilePreference("profile.password_manager_enabled", false);
        options.AddUserProfilePreference("excludeSwitches", new[] { "enable-automation" });
        options.AddUserProfilePreference("enable_do_not_track", true);
        options.AddUserProfilePreference("download_restrictions", 3);

        if (headless)
            options.AddArgument("--headless=new");

        return options;
    }

    private void ApplyAntiDetectionMeasures()
    {
        if (_driver == null) return;

        var script = @"
            Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
            Object.defineProperty(navigator, '__webdriver', { get: () => undefined });
            Object.defineProperty(navigator, '__driver_evaluate', { get: () => undefined });
            Object.defineProperty(navigator, '__selenium_evaluate', { get: () => undefined });
            Object.defineProperty(navigator, '__fxdriver_evaluate', { get: () => undefined });
            Object.defineProperty(navigator, '__webdriver_evaluate', { get: () => undefined });
            Object.defineProperty(navigator, '__lastWatirAlert', { get: () => undefined });
            Object.defineProperty(navigator, '__lastWatirConfirm', { get: () => undefined });
            Object.defineProperty(navigator, '__lastWatirPrompt', { get: () => undefined });

            window.chrome = { runtime: {} };
            Object.defineProperty(navigator, 'plugins', { get: () => [1, 2, 3, 4, 5] });
            Object.defineProperty(navigator, 'languages', { get: () => ['en-US', 'en', 'ru'] });

            const originalQuery = window.navigator.permissions.query;
            window.navigator.permissions.query = (parameters) => (
                parameters.name === 'notifications'
                    ? Promise.resolve({ state: Notification.permission })
                    : originalQuery(parameters)
            );

            Object.defineProperty(navigator, 'deviceMemory', { get: () => 8 });
            Object.defineProperty(navigator, 'hardwareConcurrency', { get: () => 8 });
            Object.defineProperty(navigator, 'maxTouchPoints', { get: () => 0 });
            Object.defineProperty(navigator, 'pdfViewerEnabled', { get: () => false });

            const getParameter = WebGLRenderingContext.prototype.getParameter;
            WebGLRenderingContext.prototype.getParameter = function(parameter) {
                if (parameter === 37445) return 'Intel Inc.';
                if (parameter === 37446) return 'Intel Iris OpenGL Engine';
                return getParameter(parameter);
            };
        ";

        _driver.ExecuteScript(script);

        _driver.ExecuteScript(script);
    }

    private void RestartDriver()
    {
        try
        {
            _driver?.Quit();
            _driver?.Dispose();
        }
        catch
        {
        }

        LaunchDriver();
    }

    private void WaitForPageLoad()
    {
        try
        {
            var tempWait = new WebDriverWait(_driver, TimeSpan.FromSeconds(30));
            tempWait.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").Equals("complete"));
            ApplyAntiDetectionMeasures();
        }
        catch
        {
        }
    }

    private IWebElement? FindElementWithFallback(string[] selectors, int timeoutSeconds)
    {
        if (_driver == null) return null;

        foreach (var selector in selectors)
        {
            try
            {
                var tempWait = new WebDriverWait(_driver, TimeSpan.FromSeconds(timeoutSeconds))
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
        if (_driver == null) return [];

        return _driver.FindElements(By.TagName("button"))
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
            Domain = c.Domain,
            Path = c.Path,
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

    public void Dispose()
    {
        if (!_disposed)
        {
            try
            {
                _driver?.Quit();
                _driver?.Dispose();
            }
            catch
            {
            }

            _driver = null;
            _wait = null;
            _disposed = true;
        }
    }
}

using System.Globalization;
using ImageGenerator.Services.Browser;
using ImageGenerator.Services.ImageGenerator.ChatGPT.Models;
using ImageGenerator.Services.ImageGenerator.ChatGPT.Modules.Interfaces;
using ImageGenerator.Services.ImageGenerator.Models;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

namespace ImageGenerator.Services.ImageGenerator.ChatGPT.Actions;

public class ChatGptGenerateAction : IChatGptGenerateAction
{
    private readonly IBrowserDriverFactory _driverFactory;
    private readonly IChatGptPageInteractorModule _interactor;
    private readonly ILogger<ChatGptGenerateAction> _logger;
    private readonly string _outputDirectory;

    private const int DefaultTimeoutSeconds = 300;
    private const int ShortTimeoutSeconds = 10;
    private const int PollingIntervalMs = 1000;
    private const long MaxImageBytes = 20 * 1024 * 1024;

    private static readonly string[] LimitErrorPatterns =
    [
       // "limit reached",
        "generation limit",
        "too many requests",
        "rate limit",
        "try again later",
        "usage limit",
       // "you've reached",
       // "image creation limit",
        "Upgrade to ChatGPT Plus or try again after",
        "You can create more images when the limit resets",
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

    public ChatGptGenerateAction(
        IBrowserDriverFactory driverFactory,
        IChatGptPageInteractorModule interactor,
        ILogger<ChatGptGenerateAction> logger,
        string outputDirectory = "./output")
    {
        _driverFactory = driverFactory;
        _interactor = interactor;
        _logger = logger;
        _outputDirectory = outputDirectory;
    }

    public async Task<bool> SendBaseMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        var driver = _driverFactory.Driver;
        if (driver == null) return false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            _driverFactory.EnsureOnChatPage();
            Thread.Sleep(2000);

            var textarea = _interactor.FindPromptTextarea(ShortTimeoutSeconds);
            if (textarea == null) return false;

            _interactor.TypeText(textarea, message, true);
            _interactor.RandomDelay(500, 1200);

            var sendButton = _interactor.FindSendButton(ShortTimeoutSeconds);
            if (sendButton == null || !sendButton.Enabled)
            {
                textarea.SendKeys(Keys.Enter);
            }
            else
            {
                try { sendButton.Click(); }
                catch (ElementClickInterceptedException) { textarea.SendKeys(Keys.Enter); }
            }

            _logger.LogInformation("Base chat message sent, waiting for response...");
            await Task.Delay(3000, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to send base message: {Message}", ex.Message);
            return false;
        }
    }

    public async Task<GenerationResult> ExecuteAsync(string prompt, ChatGptAccount account, CancellationToken cancellationToken = default)
    {
        var driver = _driverFactory.Driver;
        if (driver == null)
            return GenerationResult.Failure("Driver not initialized", "Exception");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            _driverFactory.EnsureOnChatPage();
            Thread.Sleep(2000);

            var textarea = _interactor.FindPromptTextarea(ShortTimeoutSeconds);
            if (textarea == null)
            {
                _logger.LogWarning("Could not find prompt textarea, navigating directly");
                _driverFactory.NavigateToUrl("https://chatgpt.com/");
                Thread.Sleep(2000);
                textarea = _interactor.FindPromptTextarea(ShortTimeoutSeconds);

                if (textarea == null)
                    return GenerationResult.Failure(
                        "Не удалось найти поле ввода промпта на странице ChatGPT", "NoInputField");
            }

            _interactor.TypeText(textarea, prompt,true);
            _interactor.RandomDelay(500, 1200);

            var sendButton = _interactor.FindSendButton(ShortTimeoutSeconds);
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
            return await WaitForGeneratedImageAsync(account.Email, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during prompt submission");
            return GenerationResult.Failure($"Ошибка при отправке промпта: {ex.Message}", "Exception");
        }
    }

    private async Task<GenerationResult> WaitForGeneratedImageAsync(string email, CancellationToken cancellationToken = default)
    {
        var driver = _driverFactory.Driver;
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds);

        while (DateTime.UtcNow - startTime < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var contentText = GetPageContentText(driver);

                var bodyError = AnalyzePageBody(contentText);
                if (bodyError != null)
                {
                    if (bodyError.ErrorType == "LimitReached")
                        return GenerationResult.Failure(
                            $"Достигнут лимит генерации для аккаунта {email}",
                            "LimitReached");
                    return bodyError;
                }

                var imageResult = await ExtractImageFromPageAsync();
                var stopBtn = _interactor.FindStopButton(2);
                var generationCompleted = stopBtn == null || !stopBtn.Displayed;

                if (imageResult != null && generationCompleted)
                    return imageResult;

                var assistantMessages = driver?.FindElements(
                    By.CssSelector("section[data-turn='assistant']")).ToList();

                if (assistantMessages != null && assistantMessages.Count > 0 && generationCompleted)
                {
                    var lastMessage = assistantMessages.Last().Text;
                    var msgError = AnalyzeAssistantMessage(lastMessage, DateTime.UtcNow - startTime);
                    if (msgError != null)
                        return msgError;
                }
            }
            catch (StaleElementReferenceException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error during polling: {Message}", ex.Message);
            }

            await Task.Delay(PollingIntervalMs, cancellationToken);
        }

        _logger.LogWarning("Image generation timed out after {Timeout}", timeout);
        return GenerationResult.Failure(
            "Превышено время ожидания генерации изображения", "Timeout");
    }

    private static string GetPageContentText(ChromeDriver? driver)
    {
        if (driver == null) return "";

        try
        {
            var mainContent = driver.FindElements(
                    By.XPath("//div[contains(@class, '@container/main')]"))
                .FirstOrDefault(e => e.Displayed);
            return mainContent?.Text ?? driver.FindElement(By.TagName("body")).Text ?? "";
        }
        catch
        {
            return "";
        }
    }

    private GenerationResult? AnalyzePageBody(string pageBody)
    {
        if (string.IsNullOrEmpty(pageBody))
            return null;

        var limitPattern = LimitErrorPatterns.FirstOrDefault(p =>
            pageBody.Contains(p, StringComparison.OrdinalIgnoreCase));
        if (limitPattern != null)
        {
            _logger.LogWarning("Limit detected: '{Pattern}'", limitPattern);
            return GenerationResult.Failure(
                "Достигнут лимит генерации", "LimitReached");
        }

        var prohibitedPattern = ProhibitedContentPatterns.FirstOrDefault(p =>
            pageBody.Contains(p, StringComparison.OrdinalIgnoreCase));
        if (prohibitedPattern != null)
        {
            _logger.LogWarning("Prohibited content detected: '{Pattern}'", prohibitedPattern);
            return GenerationResult.Failure(
                "ChatGPT не может сгенерировать изображение по данному промпту: нарушение политики безопасности",
                "ProhibitedContent");
        }

        return null;
    }

    private GenerationResult? AnalyzeAssistantMessage(string message, TimeSpan elapsed)
    {
        if (string.IsNullOrEmpty(message))
            return null;

        if (message.Contains("Image") || message.Contains("image") ||
            message.Contains("picture") || message.Contains("generat"))
            return null;

        var textLower = message.ToLowerInvariant();
        var errorIndicators = new[]
        {
            "sorry", "cannot", "can't", "unable", "not able to",
            "i don't", "i'm not able", "doesn't work", "not supported",
            "not available", "currently unavailable", "having trouble"
        };

        if (!errorIndicators.Any(e => textLower.Contains(e)))
            return null;

        if (elapsed <= TimeSpan.FromSeconds(15))
            return null;

        _logger.LogWarning("ChatGPT responded with error text: {Text}",
            message[..Math.Min(200, message.Length)]);

        if (textLower.Contains("policy") || textLower.Contains("content") ||
            textLower.Contains("safety") || textLower.Contains("guideline") ||
            textLower.Contains("prohibit") || textLower.Contains("violat") ||
            textLower.Contains("not allow"))
        {
            return GenerationResult.Failure(
                "ChatGPT отклонил запрос: " + message[..Math.Min(300, message.Length)],
                "ProhibitedContent");
        }

        if (textLower.Contains("rate limit") || textLower.Contains("too many") ||
            textLower.Contains("try again later") || textLower.Contains("limit"))
        {
            return GenerationResult.Failure(
                "Достигнут лимит генерации", "LimitReached");
        }

        return GenerationResult.Failure(
            "ChatGPT не смог сгенерировать изображение: " + message[..Math.Min(300, message.Length)],
            "CantGenerate");
    }

    private async Task<GenerationResult?> ExtractImageFromPageAsync()
    {
        var driver = _driverFactory.Driver;
        if (driver == null) return null;

        try
        {
            var assistantMessages = driver.FindElements(
                By.CssSelector("section[data-turn='assistant']")).ToList();

            if (assistantMessages.Count == 0)
                return null;

            var lastAssistant = assistantMessages.Last();

            var images = lastAssistant.FindElements(By.TagName("img"));
            foreach (var img in images)
            {
                try
                {
                    var src = img.GetAttribute("src");
                    if (string.IsNullOrEmpty(src)) continue;

                    string? outputPath = null;

                    if (src.StartsWith("https://chatgpt.com/backend-api/estuary/content"))
                    {
                        var imageBytes = await DownloadImageViaFetchAsync(src);
                        outputPath = SaveImageToFile(imageBytes);
                    }
                    else if (src.Contains("oaidalleapiprodscus") ||
                             (src.Contains("chatgpt") && src.EndsWith(".png")))
                    {
                        outputPath = await DownloadAndSaveImageAsync(src);
                    }

                    if (outputPath != null)
                    {
                        _logger.LogInformation("Image extracted from last assistant message, saved to {Path}", outputPath);
                        return GenerationResult.Success(outputPath);
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
        catch (Exception ex)
        {
            _logger.LogWarning("Error finding assistant messages: {Message}", ex.Message);
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

    private async Task<byte[]> DownloadImageViaFetchAsync(string url)
    {
        var driver = _driverFactory.Driver;
        if (driver == null)
            throw new InvalidOperationException("Driver is not initialized");

        var script = @"
            return await fetch(arguments[0], { credentials: 'include' })
                .then(response => response.blob())
                .then(blob => new Promise((resolve, reject) => {
                    const reader = new FileReader();
                    reader.onloadend = () => resolve(reader.result);
                    reader.onerror = reject;
                    reader.readAsDataURL(blob);
                }));
        ";

        var result = driver.ExecuteScript(script, url) as string;
        if (string.IsNullOrEmpty(result))
            throw new InvalidOperationException("Failed to download image via driver");

        var base64 = result[(result.IndexOf(",") + 1)..];
        return Convert.FromBase64String(base64);
    }

    private async Task<string?> DownloadAndSaveImageAsync(string url)
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var imageData = await httpClient.GetByteArrayAsync(url);

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
}

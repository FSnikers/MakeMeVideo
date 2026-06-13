using System.Globalization;
using ImageGenerator.Models;
using ImageGenerator.Services.Browser;
using ImageGenerator.Services.ChatGPT.Interfaces;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;

namespace ImageGenerator.Services.ChatGPT;

public class ChatGptImageExtractorHelper : IChatGptImageExtractorHelper
{
    private readonly IBrowserDriverFactory _driverFactory;
    private readonly ILogger<ChatGptImageExtractorHelper> _logger;
    private readonly string _outputDirectory;

    private const long MaxImageBytes = 20 * 1024 * 1024;

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

    public ChatGptImageExtractorHelper(
        IBrowserDriverFactory driverFactory,
        ILogger<ChatGptImageExtractorHelper> logger,
        string outputDirectory = "./output")
    {
        _driverFactory = driverFactory;
        _logger = logger;
        _outputDirectory = outputDirectory;
    }

    public async Task<GenerationResult?> ExtractImageFromPageAsync()
    {
        var driver = _driverFactory.Driver;
        if (driver == null) return null;

        foreach (var selector in ImageSelectors)
        {
            try
            {
                var images = driver.FindElements(By.CssSelector(selector));
                foreach (var img in images)
                {
                    try
                    {
                        var src = img.GetAttribute("src");
                        if (string.IsNullOrEmpty(src)) continue;

                        if (src.StartsWith("https://chatgpt.com/backend-api/estuary/content"))
                        {
                            var base64Data = src[(src.IndexOf(",") + 1)..];
                            var imageData = Convert.FromBase64String(base64Data);
                            var outputPath = SaveImageToFile(imageData);
                            _logger.LogInformation("Image extracted from data URI, saved to {Path}", outputPath);
                            return GenerationResult.Success(outputPath);
                        }

                        if (src.Contains("oaidalleapiprodscus") || src.Contains("chatgpt") && src.EndsWith(".png"))
                        {
                            var outputPath = await DownloadAndSaveImageAsync(src);
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
            var allImgs = driver.FindElements(By.TagName("img"));
            foreach (var img in allImgs)
            {
                try
                {
                    var src = img.GetAttribute("src");
                    if (string.IsNullOrEmpty(src)) continue;

                    if (src.StartsWith("https://chatgpt.com/backend-api/estuary/content"))
                    {
                        var imageBytes = await DownloadImageAsync(src);
                        var outputPath = SaveImageToFile(imageBytes);
                        _logger.LogInformation("Image extracted from data URI (fallback), saved to {Path}", outputPath);
                        return GenerationResult.Success(outputPath);
                    }

                    if (src.Contains("oaidalleapiprodscus"))
                    {
                        var outputPath = await DownloadAndSaveImageAsync(src);
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

    private async Task<byte[]> DownloadImageAsync(string url)
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

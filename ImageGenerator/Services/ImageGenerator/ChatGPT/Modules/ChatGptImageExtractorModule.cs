using ImageGenerator.Services.Browser;
using ImageGenerator.Services.ImageGenerator.ChatGPT.Models;
using ImageGenerator.Services.ImageGenerator.ChatGPT.Modules.Interfaces;
using ImageGenerator.Services.ImageGenerator.Models;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;

namespace ImageGenerator.Services.ImageGenerator.ChatGPT.Modules;

public class ChatGptImageExtractorModule : IChatGptImageExtractorModule
{
    private readonly IBrowserDriverFactory _driverFactory;
    private readonly ILogger<ChatGptImageExtractorModule> _logger;
    private readonly IChatGptPageInteractorModule _pageInteractorModule;
    private readonly IChatGptErrorAnalyzerModule _errorAnalyzerModule;
    private readonly IChatGptImageExtractorHelperModule _imageHelperModule;

    private const int DefaultTimeoutSeconds = 300;
    private const int ShortTimeoutSeconds = 10;
    private const int PollingIntervalMs = 1000;

    public ChatGptImageExtractorModule(
        IBrowserDriverFactory driverFactory,
        ILogger<ChatGptImageExtractorModule> logger,
        IChatGptPageInteractorModule pageInteractorModule,
        IChatGptErrorAnalyzerModule errorAnalyzerModule,
        IChatGptImageExtractorHelperModule imageHelperModule)
    {
        _driverFactory = driverFactory;
        _logger = logger;
        _pageInteractorModule = pageInteractorModule;
        _errorAnalyzerModule = errorAnalyzerModule;
        _imageHelperModule = imageHelperModule;
    }

    public async Task<GenerationResult> SendPromptAndWaitForImageAsync(string prompt, ChatGptAccount? currentAccount, CancellationToken cancellationToken = default)
    {
        var driver = _driverFactory.Driver;
        if (driver == null)
            return GenerationResult.Failure("Driver not initialized", "Exception");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _driverFactory.EnsureOnChatPage();
            Thread.Sleep(2000);

            var textarea = _pageInteractorModule.FindPromptTextarea(ShortTimeoutSeconds);
            if (textarea == null)
            {
                _logger.LogWarning("Could not find prompt textarea, navigating directly");
                _driverFactory.NavigateToUrl("https://chatgpt.com/");
                Thread.Sleep(2000);
                textarea = _pageInteractorModule.FindPromptTextarea(ShortTimeoutSeconds);

                if (textarea == null)
                    return GenerationResult.Failure(
                        "Не удалось найти поле ввода промпта на странице ChatGPT", "NoInputField");
            }

            _pageInteractorModule.TypeText(textarea, prompt);
            _pageInteractorModule.RandomDelay(500, 1200);

            var sendButton = _pageInteractorModule.FindSendButton(ShortTimeoutSeconds);
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
            return await WaitForGeneratedImageAsync(currentAccount, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during prompt submission");
            return GenerationResult.Failure($"Ошибка при отправке промпта: {ex.Message}", "Exception");
        }
    }

    private async Task<GenerationResult> WaitForGeneratedImageAsync(ChatGptAccount? currentAccount, CancellationToken cancellationToken = default)
    {
        var driver = _driverFactory.Driver;
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds);

        while (DateTime.UtcNow - startTime < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var pageBody = driver?.FindElement(By.TagName("body")).Text ?? "";

                var bodyError = _errorAnalyzerModule.AnalyzePageBody(pageBody);
                if (bodyError != null)
                {
                    if (bodyError.ErrorType == "LimitReached")
                        return GenerationResult.Failure(
                            $"Достигнут лимит генерации для аккаунта {currentAccount?.Email}",
                            "LimitReached");
                    return bodyError;
                }

                var stopBtn = _pageInteractorModule.FindStopButton(2);
                if (stopBtn != null && stopBtn.Displayed)
                {
                    await Task.Delay(PollingIntervalMs, cancellationToken);
                    continue;
                }

                var imageResult = await _imageHelperModule.ExtractImageFromPageAsync();
                if (imageResult != null)
                    return imageResult;

                var assistantMessages = driver?.FindElements(
                    By.CssSelector("[data-message-author-role='assistant']")).ToList();

                if (assistantMessages != null && assistantMessages.Count > 0)
                {
                    var lastMessage = assistantMessages.Last().Text;
                    var msgError = _errorAnalyzerModule.AnalyzeAssistantMessage(lastMessage, DateTime.UtcNow - startTime);
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
}

using ImageGenerator.Services.AccountStorage.Interfaces;
using ImageGenerator.Services.Browser;
using ImageGenerator.Services.ImageGenerator.ChatGPT.Models;
using ImageGenerator.Services.ImageGenerator.ChatGPT.Modules.Interfaces;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;

namespace ImageGenerator.Services.ImageGenerator.ChatGPT.Actions;

public class ChatGptSessionAction : IChatGptSessionAction
{
    private readonly IBrowserDriverFactory _driverFactory;
    private readonly IAccountStorage _accountStorage;
    private readonly IChatGptPageDetectorModule _detector;
    private readonly ILogger<ChatGptSessionAction> _logger;

    public ChatGptSessionAction(
        IBrowserDriverFactory driverFactory,
        IAccountStorage accountStorage,
        IChatGptPageDetectorModule detector,
        ILogger<ChatGptSessionAction> logger)
    {
        _driverFactory = driverFactory;
        _accountStorage = accountStorage;
        _detector = detector;
        _logger = logger;
    }

    public async Task ForceLogoutAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Force logout");

        var driver = _driverFactory.Driver;
        if (driver == null)
        {
            _logger.LogWarning("Driver is null, navigating to login page");
            _driverFactory.NavigateToUrl("https://chatgpt.com/auth/login");
            await _detector.WaitForExactStatusAsync(ChatGptPageStatus.LoginPage, 15);
            return;
        }

        try
        {
            driver.Manage().Cookies.DeleteAllCookies();
            _logger.LogInformation("All cookies deleted for logout");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to delete cookies during logout: {Message}", ex.Message);
        }

        _driverFactory.NavigateToUrl("https://chatgpt.com/auth/login");
        var onLoginPage = await _detector.WaitForExactStatusAsync(ChatGptPageStatus.LoginPage, 15);

        if (onLoginPage)
        {
            _logger.LogInformation("Force logout successful, confirmed on LoginPage");
        }
        else
        {
            _logger.LogWarning("Force logout may have failed, current page is not LoginPage");
            var status = await _detector.DetectCurrentPageAsync();
            _logger.LogInformation("Current status after logout attempt: {Status}", status);
        }
    }

    public async Task MarkAccountUsedAsync(ChatGptAccount account)
    {
        await _accountStorage.MarkAccountUsedAsync(account);
        _logger.LogInformation("Account {Email} marked as used", account.Email);
    }

    public async Task MarkAccountLimitReachedAsync(ChatGptAccount account)
    {
        await _accountStorage.MarkAccountLimitReachedAsync(account);
        _logger.LogInformation("Account {Email} marked as limit reached", account.Email);
    }
}

using OpenQA.Selenium.Chrome;

namespace ImageGenerator.Services.Browser;

public interface IBrowserDriverFactory : IDisposable
{
    ChromeDriver? Driver { get; }
    void CreateDriver(bool headless);
    void RestartDriver();
    void NavigateToUrl(string url);
    void WaitForPageLoad();
    void EnsureOnChatPage();
    void NavigateToNewChat();
}

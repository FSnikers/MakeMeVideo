using OpenQA.Selenium;

namespace ImageGenerator.Services.ChatGPT.Interfaces;

public interface IChatGptPageInteractor
{
    IWebElement? FindPromptTextarea(int timeoutSeconds);
    IWebElement? FindSendButton(int timeoutSeconds);
    IWebElement? FindStopButton(int timeoutSeconds);
    void TypeText(IWebElement element, string text);
    void RandomDelay(int minMs = 300, int maxMs = 800);
}

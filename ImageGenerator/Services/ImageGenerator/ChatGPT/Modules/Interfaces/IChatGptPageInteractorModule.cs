using OpenQA.Selenium;

namespace ImageGenerator.Services.ImageGenerator.ChatGPT.Modules.Interfaces;

public interface IChatGptPageInteractorModule
{
    IWebElement? FindPromptTextarea(int timeoutSeconds);
    IWebElement? FindSendButton(int timeoutSeconds);
    IWebElement? FindStopButton(int timeoutSeconds);
    void TypeText(IWebElement element, string text);
    void RandomDelay(int minMs = 300, int maxMs = 800);
}

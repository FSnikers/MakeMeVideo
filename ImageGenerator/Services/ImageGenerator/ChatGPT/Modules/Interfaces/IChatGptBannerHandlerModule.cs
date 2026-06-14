namespace ImageGenerator.Services.ImageGenerator.ChatGPT.Modules.Interfaces;

public interface IChatGptBannerHandlerModule
{
    Task<bool> TryClickLogInAsync();
}

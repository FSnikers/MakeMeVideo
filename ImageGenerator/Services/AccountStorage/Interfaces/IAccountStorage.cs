using ImageGenerator.Services.ImageGenerator.ChatGPT.Models;

namespace ImageGenerator.Services.AccountStorage.Interfaces;

public interface IAccountStorage
{
    Task<ChatGptAccount?> GetNextAvailableAccountAsync();
    Task MarkAccountUsedAsync(ChatGptAccount account);
    Task MarkAccountLimitReachedAsync(ChatGptAccount account);
    Task SaveCookiesAsync(string email, string cookiesJson);
    Task<string?> LoadCookiesAsync(string email);
}

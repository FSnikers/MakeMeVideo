using System.Threading.Tasks;
using ImageGenerator.Models;

namespace ImageGenerator.Services;

public interface IAccountStorage
{
    Task<ChatGptAccount?> GetNextAvailableAccountAsync();
    Task MarkAccountUsedAsync(ChatGptAccount account);
    Task MarkAccountLimitReachedAsync(ChatGptAccount account);
    Task SaveCookiesAsync(string email, string cookiesJson);
    Task<string?> LoadCookiesAsync(string email);
}

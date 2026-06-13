using System.Text.Json;
using ImageGenerator.Services.AccountStorage.Interfaces;
using ImageGenerator.Services.ImageGenerator.ChatGPT.Models;

namespace ImageGenerator.Services.AccountStorage;

public class JsonAccountStorage : IAccountStorage
{
    private readonly string _accountsFilePath;
    private readonly string _cookiesDirectory;
    private List<ChatGptAccount> _accounts;
    private readonly object _lock = new();
    
    private const int MaxGenerationsPerDay = 50;
    
    public JsonAccountStorage(string accountsFilePath = "chatgpt_accounts.json", string cookiesDirectory = "cookies")
    {
        _accountsFilePath = accountsFilePath;
        _cookiesDirectory = cookiesDirectory;
        
        if (!Directory.Exists(_cookiesDirectory))
        {
            Directory.CreateDirectory(_cookiesDirectory);
        }
        
        _accounts = LoadAccountsFromFile();
    }
    
    private List<ChatGptAccount> LoadAccountsFromFile()
    {
        if (!File.Exists(_accountsFilePath))
        {
            var defaultAccounts = new List<ChatGptAccount>
            {
                new() { Email = "your_email1@gmail.com", Password = "your_password1", IsActive = true },
                new() { Email = "your_email2@gmail.com", Password = "your_password2", IsActive = true }
            };
            var json = JsonSerializer.Serialize(defaultAccounts, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_accountsFilePath, json);
            return defaultAccounts;
        }
        
        var fileContent = File.ReadAllText(_accountsFilePath);
        return JsonSerializer.Deserialize<List<ChatGptAccount>>(fileContent) ?? new List<ChatGptAccount>();
    }
    
    private void SaveAccountsToFile()
    {
        var json = JsonSerializer.Serialize(_accounts, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_accountsFilePath, json);
    }
    
    private void ResetDailyCountersIfNeeded()
    {
        var today = DateTime.UtcNow.Date;
        foreach (var account in _accounts)
        {
            if (account.LastResetDate?.Date != today)
            {
                account.GenerationCountToday = 0;
                account.LastResetDate = today;
            }
        }
        SaveAccountsToFile();
    }
    
    public Task<ChatGptAccount?> GetNextAvailableAccountAsync()
    {
        lock (_lock)
        {
            ResetDailyCountersIfNeeded();
            
            var availableAccount = _accounts
                .Where(a => a.IsActive && a.GenerationCountToday < MaxGenerationsPerDay)
                .OrderBy(a => a.LastResetDate)
                .FirstOrDefault();
            
            return Task.FromResult(availableAccount);
        }
    }
    
    public Task MarkAccountUsedAsync(ChatGptAccount account)
    {
        lock (_lock)
        {
            ResetDailyCountersIfNeeded();
            
            var targetAccount = _accounts.FirstOrDefault(a => a.Email == account.Email);
            if (targetAccount != null)
            {
                targetAccount.GenerationCountToday++;
                SaveAccountsToFile();
            }
        }
        return Task.CompletedTask;
    }
    
    public Task MarkAccountLimitReachedAsync(ChatGptAccount account)
    {
        lock (_lock)
        {
            var targetAccount = _accounts.FirstOrDefault(a => a.Email == account.Email);
            if (targetAccount != null)
            {
                targetAccount.IsActive = false;
                SaveAccountsToFile();
            }
        }
        return Task.CompletedTask;
    }
    
    public Task SaveCookiesAsync(string email, string cookiesJson)
    {
        var cookieFile = Path.Combine(_cookiesDirectory, $"{SanitizeFilename(email)}.json");
        File.WriteAllText(cookieFile, cookiesJson);
        return Task.CompletedTask;
    }
    
    public Task<string?> LoadCookiesAsync(string email)
    {
        var cookieFile = Path.Combine(_cookiesDirectory, $"{SanitizeFilename(email)}.json");
        if (!File.Exists(cookieFile))
        {
            return Task.FromResult<string?>(null);
        }
        
        var json = File.ReadAllText(cookieFile);
        return Task.FromResult<string?>(json);
    }
    
    private static string SanitizeFilename(string filename)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Concat(filename.Select(c => invalidChars.Contains(c) ? '_' : c));
    }
}

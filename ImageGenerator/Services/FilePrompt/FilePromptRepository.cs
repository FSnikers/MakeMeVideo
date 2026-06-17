using System.Text.Encodings.Web;
using System.Text.Json;
using ImageGenerator.Services.FilePrompt.Interfaces;
using ImageGenerator.Services.FilePrompt.Models;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.Services.FilePrompt;

public class FilePromptRepository : IPromptRepository
{
    private readonly string _filePath;
    private readonly ILogger<FilePromptRepository> _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public FilePromptRepository(ILogger<FilePromptRepository> logger, string filePath = "prompts.json")
    {
        _filePath = filePath;
        _logger = logger;
        EnsureFileExists();
    }

    private void EnsureFileExists()
    {
        if (!File.Exists(_filePath))
        {
            var empty = new PromptCollection();
            var json = JsonSerializer.Serialize(empty, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
    }

    public async Task<PromptCollection> GetPromptCollectionAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            return JsonSerializer.Deserialize<PromptCollection>(json) ?? new PromptCollection();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task WritePromptsAsync(PromptCollection collection)
    {
        await _fileLock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(collection, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<PromptData?> GetPromptByIdAsync(string id)
    {
        var collection = await GetPromptCollectionAsync();
        return collection.Prompts.FirstOrDefault(p => p.Id == id);
    }

    public async Task AddPromptAsync(PromptData prompt)
    {
        var collection = await GetPromptCollectionAsync();
        collection.Prompts.Add(prompt);
        await WritePromptsAsync(collection);
        _logger.LogInformation("Добавлен промпт {Id}: {Text}", prompt.Id, prompt.Text[..Math.Min(50, prompt.Text.Length)]);
    }

    public async Task<IEnumerable<PromptData>> GetPendingPromptsAsync()
    {
        var collection = await GetPromptCollectionAsync();
        return collection.Prompts.Where(p => p.Status == "pending" || p.Status == "processing");
    }

    public async Task UpdatePromptStatusAsync(string id, string status)
    {
        var collection = await GetPromptCollectionAsync();
        var prompt = collection.Prompts.FirstOrDefault(p => p.Id == id);
        if (prompt != null)
        {
            prompt.Status = status;
            await WritePromptsAsync(collection);
        }
    }

    public async Task DeletePromptAsync(string id)
    {
        var collection = await GetPromptCollectionAsync();
        collection.Prompts.RemoveAll(p => p.Id == id);
        await WritePromptsAsync(collection);
    }
}
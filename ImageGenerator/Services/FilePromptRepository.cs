using System.Text.Json;
using ImageGenerator.Interfaces;
using ImageGenerator.Models;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.Services;

/// <summary>
/// Репозиторий для хранения промптов в JSON файле
/// </summary>
public class FilePromptRepository : IPromptRepository
{
    private readonly string _filePath;
    private readonly ILogger<FilePromptRepository> _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

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
            File.WriteAllText(_filePath, "[]");
        }
    }

    private async Task<List<PromptData>> ReadPromptsAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            return JsonSerializer.Deserialize<List<PromptData>>(json) ?? new List<PromptData>();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task WritePromptsAsync(List<PromptData> prompts)
    {
        await _fileLock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(prompts, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_filePath, json);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<IEnumerable<PromptData>> GetAllPromptsAsync()
    {
        return await ReadPromptsAsync();
    }

    public async Task<PromptData?> GetPromptByIdAsync(string id)
    {
        var prompts = await ReadPromptsAsync();
        return prompts.FirstOrDefault(p => p.Id == id);
    }

    public async Task AddPromptAsync(PromptData prompt)
    {
        var prompts = await ReadPromptsAsync();
        prompts.Add(prompt);
        await WritePromptsAsync(prompts);
        _logger.LogInformation("Добавлен промпт {Id}: {Text}", prompt.Id, prompt.Text[..Math.Min(50, prompt.Text.Length)]);
    }

    public async Task AddPromptsAsync(IEnumerable<PromptData> prompts)
    {
        var existingPrompts = await ReadPromptsAsync();
        existingPrompts.AddRange(prompts);
        await WritePromptsAsync(existingPrompts);
        _logger.LogInformation("Добавлено {Count} промптов", prompts.Count());
    }

    public async Task UpdatePromptAsync(PromptData prompt)
    {
        var prompts = await ReadPromptsAsync();
        var index = prompts.FindIndex(p => p.Id == prompt.Id);
        if (index >= 0)
        {
            prompts[index] = prompt;
            await WritePromptsAsync(prompts);
        }
    }

    public async Task UpdatePromptStatusAsync(string id, string status, string? outputPath = null, string? errorMessage = null)
    {
        var prompts = await ReadPromptsAsync();
        var prompt = prompts.FirstOrDefault(p => p.Id == id);
        if (prompt != null)
        {
            prompt.Status = status;
            if (outputPath != null) prompt.OutputPath = outputPath;
            if (errorMessage != null) prompt.ErrorMessage = errorMessage;
            if (status == "completed" || status == "failed")
                prompt.CompletedAt = DateTime.UtcNow;

            await WritePromptsAsync(prompts);
        }
    }

    public async Task DeletePromptAsync(string id)
    {
        var prompts = await ReadPromptsAsync();
        prompts.RemoveAll(p => p.Id == id);
        await WritePromptsAsync(prompts);
    }

    public async Task<IEnumerable<PromptData>> GetPendingPromptsAsync()
    {
        var prompts = await ReadPromptsAsync();
        return prompts.Where(p => p.Status == "pending" || p.Status == "processing");
    }
}
using ImageGenerator.Models;

namespace ImageGenerator.Services.Interfaces;

/// <summary>
/// Репозиторий для работы с промптами
/// </summary>
public interface IPromptRepository
{
    Task<IEnumerable<PromptData>> GetAllPromptsAsync();
    Task<PromptData?> GetPromptByIdAsync(string id);
    Task AddPromptAsync(PromptData prompt);
    Task AddPromptsAsync(IEnumerable<PromptData> prompts);
    Task UpdatePromptAsync(PromptData prompt);
    Task UpdatePromptStatusAsync(string id, string status, string? outputPath = null, string? errorMessage = null);
    Task DeletePromptAsync(string id);
    Task<IEnumerable<PromptData>> GetPendingPromptsAsync();
}
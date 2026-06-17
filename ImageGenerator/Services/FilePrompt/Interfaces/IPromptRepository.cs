using ImageGenerator.Services.FilePrompt.Models;

namespace ImageGenerator.Services.FilePrompt.Interfaces;

public interface IPromptRepository
{
    Task<PromptCollection> GetPromptCollectionAsync();
    Task<PromptData?> GetPromptByIdAsync(string id);
    Task AddPromptAsync(PromptData prompt);
    Task DeletePromptAsync(string id);
    Task<IEnumerable<PromptData>> GetPendingPromptsAsync();
    Task UpdatePromptStatusAsync(string id, string status, string? outputPath = null, string? errorMessage = null);
}

using MakeMeVideo.Api.Domain;

namespace MakeMeVideo.Api.Infrastructure;

public record GenerationExecutionResult(bool Success, bool LimitReached, string? ErrorMessage, string? SavedPath);

public interface IChatGptAutomationClient
{
    Task<GenerationExecutionResult> GenerateImageAsync(GeneratorAccount account, GenerationProject project, ImageTaskItem imageTask, CancellationToken ct);
}

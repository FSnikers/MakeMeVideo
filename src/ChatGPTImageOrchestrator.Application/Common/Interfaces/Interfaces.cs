using System;
using System.Threading;
using System.Threading.Tasks;
using ChatGPTImageOrchestrator.Application.DTOs;
using ChatGPTImageOrchestrator.Domain.Entities;

namespace ChatGPTImageOrchestrator.Application.Common.Interfaces;

public interface IGenerationOrchestrator
{
    Task<Guid> CreateProjectAsync(CreateProjectRequest request, CancellationToken ct = default);
    Task<ProjectStatusDto> GetProjectStatusAsync(Guid projectId, CancellationToken ct = default);
}

public interface IAccountPoolManager
{
    Task<GeneratorAccount?> AcquireAsync(CancellationToken ct);
    Task MarkLimitReachedAsync(GeneratorAccount account, CancellationToken ct);
}

public interface IChatGPTBrowserClient
{
    Task<(bool Success, bool LimitReached, string? Error, string? SavedPath)> GenerateAsync(
        GeneratorAccount account, GenerationProject project, ImageTask imageTask, CancellationToken ct);
}

public interface ILimitDetectorService
{
    bool IsLimitMessage(string text);
}

public interface IPromptBuilderService
{
    string Build(string description, string suffix);
}

public interface IImageDownloadService
{
    Task<string> SaveElementScreenshotAsync(object pageOrLocator, string projectName, string fileName, CancellationToken ct);
}

public interface IBackgroundTaskQueue
{
    ValueTask QueueAsync(Guid projectId, CancellationToken ct);
    ValueTask<Guid> DequeueAsync(CancellationToken ct);
}
using System.Threading;
using System.Threading.Tasks;
using ChatGPTImageOrchestrator.Application.Common.Interfaces;
using ChatGPTImageOrchestrator.Domain.Entities;

namespace ChatGPTImageOrchestrator.Infrastructure.BrowserAutomation;

public class ChatGPTBrowserClient : IChatGPTBrowserClient
{
    public Task<(bool Success, bool LimitReached, string? Error, string? SavedPath)> GenerateAsync(
        GeneratorAccount account, GenerationProject project, ImageTask imageTask, CancellationToken ct) =>
        Task.FromResult((false, false, "Not implemented: Playwright flow in infrastructure client", (string?)null));
}
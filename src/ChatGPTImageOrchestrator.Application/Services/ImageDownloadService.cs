using System.Threading;
using System.Threading.Tasks;
using ChatGPTImageOrchestrator.Application.Common.Interfaces;

namespace ChatGPTImageOrchestrator.Application.Services;

public class ImageDownloadService : IImageDownloadService
{
    public Task<string> SaveElementScreenshotAsync(object pageOrLocator, string projectName, string fileName, CancellationToken ct) =>
        Task.FromResult(string.Empty);
}
using ImageGenerator.Services.ImageGenerator.Models;

namespace ImageGenerator.Services.ImageGenerator.Abstractions;

public interface IImageGenerator : IDisposable
{
    Task<GenerationResult> GenerateImageAsync(GenerationRequest request, CancellationToken cancellationToken = default);
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
    Task<bool> SendBaseMessageAsync(string message, CancellationToken cancellationToken = default);
}
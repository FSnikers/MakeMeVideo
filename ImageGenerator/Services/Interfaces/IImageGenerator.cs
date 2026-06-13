using ImageGenerator.Models;

namespace ImageGenerator.Services.Interfaces;

public interface IImageGenerator
{
    Task<GenerationResult> GenerateImageAsync(GenerationRequest request, CancellationToken cancellationToken = default);
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}
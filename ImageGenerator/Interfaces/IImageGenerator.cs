using System.Threading.Tasks;
using ImageGenerator.Models;

namespace ImageGenerator.Interfaces;

/// <summary>
/// Интерфейс для генератора изображений
/// </summary>
public interface IImageGenerator
{
    Task<GenerationResult> GenerateImageAsync(GenerationRequest request);
    Task<bool> IsAvailableAsync();
}
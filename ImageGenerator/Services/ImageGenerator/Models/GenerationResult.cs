namespace ImageGenerator.Services.ImageGenerator.Models;

public class GenerationResult
{
    public bool IsSuccess { get; set; }
    public string? ImagePath { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorType { get; set; }
    public TimeSpan Duration { get; set; }
    public string? PromptId { get; set; }
    public string? RevisedPrompt { get; set; }

    public static GenerationResult Success(string imagePath, string? revisedPrompt = null)
    {
        return new GenerationResult
        {
            IsSuccess = true,
            ImagePath = imagePath,
            RevisedPrompt = revisedPrompt
        };
    }

    public static GenerationResult Failure(string errorMessage, string errorType)
    {
        return new GenerationResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage,
            ErrorType = errorType
        };
    }
}
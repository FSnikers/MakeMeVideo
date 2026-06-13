using ImageGenerator.Services.ImageGenerator.ChatGPT.Modules.Interfaces;
using ImageGenerator.Services.ImageGenerator.Models;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.Services.ImageGenerator.ChatGPT.Modules;

public class ChatGptErrorAnalyzerModule : IChatGptErrorAnalyzerModule
{
    private readonly ILogger<ChatGptErrorAnalyzerModule> _logger;

    private static readonly string[] LimitErrorPatterns =
    [
        "limit reached",
        "generation limit",
        "too many requests",
        "rate limit",
        "try again later",
        "usage limit",
        "you've reached"
    ];

    private static readonly string[] ProhibitedContentPatterns =
    [
        "cannot generate",
        "can't generate",
        "unable to generate",
        "violates our",
        "content policy",
        "safety system",
        "not allowed",
        "inappropriate",
        "harmful content",
        "prohibited",
        "does not comply"
    ];

    public ChatGptErrorAnalyzerModule(ILogger<ChatGptErrorAnalyzerModule> logger)
    {
        _logger = logger;
    }

    public GenerationResult? AnalyzePageBody(string pageBody)
    {
        var limitPattern = LimitErrorPatterns.FirstOrDefault(p =>
            pageBody.Contains(p, StringComparison.OrdinalIgnoreCase));
        if (limitPattern != null)
        {
            _logger.LogWarning("Limit detected: '{Pattern}'", limitPattern);
            return GenerationResult.Failure(
                "Достигнут лимит генерации", "LimitReached");
        }

        var prohibitedPattern = ProhibitedContentPatterns.FirstOrDefault(p =>
            pageBody.Contains(p, StringComparison.OrdinalIgnoreCase));
        if (prohibitedPattern != null)
        {
            _logger.LogWarning("Prohibited content detected: '{Pattern}'", prohibitedPattern);
            return GenerationResult.Failure(
                "ChatGPT не может сгенерировать изображение по данному промпту: " +
                "нарушение политики безопасности", "ProhibitedContent");
        }

        return null;
    }

    public GenerationResult? AnalyzeAssistantMessage(string message, TimeSpan elapsed)
    {
        if (string.IsNullOrEmpty(message))
            return null;

        if (message.Contains("Image") || message.Contains("image") ||
            message.Contains("picture") || message.Contains("generat"))
            return null;

        var textLower = message.ToLowerInvariant();
        var errorIndicators = new[]
        {
            "sorry", "cannot", "can't", "unable", "not able to",
            "i don't", "i'm not able", "doesn't work", "not supported",
            "not available", "currently unavailable", "having trouble"
        };

        if (!errorIndicators.Any(e => textLower.Contains(e)))
            return null;

        if (elapsed <= TimeSpan.FromSeconds(15))
            return null;

        _logger.LogWarning("ChatGPT responded with error text: {Text}",
            message[..Math.Min(200, message.Length)]);

        if (textLower.Contains("policy") || textLower.Contains("content") ||
            textLower.Contains("safety") || textLower.Contains("guideline") ||
            textLower.Contains("prohibit") || textLower.Contains("violat") ||
            textLower.Contains("not allow"))
        {
            return GenerationResult.Failure(
                "ChatGPT отклонил запрос: " + message[..Math.Min(300, message.Length)],
                "ProhibitedContent");
        }

        if (textLower.Contains("rate limit") || textLower.Contains("too many") ||
            textLower.Contains("try again later") || textLower.Contains("limit"))
        {
            return GenerationResult.Failure(
                "Достигнут лимит генерации", "LimitReached");
        }

        return GenerationResult.Failure(
            "ChatGPT не смог сгенерировать изображение: " +
            message[..Math.Min(300, message.Length)],
            "CantGenerate");
    }
}

using System.Text;
using System.Text.Json;
using ImageGenerator.Config.Interfaces;
using ImageGenerator.Services.ImageGenerator.Abstractions;
using ImageGenerator.Services.ImageGenerator.Models;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.Services.ImageGenerator.OpenAI;

public class OpenAiImageGenerator : IImageGenerator
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfigManager _configManager;
    private readonly ILogger<OpenAiImageGenerator> _logger;

    public OpenAiImageGenerator(
        IHttpClientFactory httpClientFactory,
        IConfigManager configManager,
        ILogger<OpenAiImageGenerator> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configManager = configManager;
        _logger = logger;
    }

    public async Task<GenerationResult> GenerateImageAsync(GenerationRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var config = _configManager.GetConfig();

            if (string.IsNullOrEmpty(config.ApiKey))
                return GenerationResult.Failure("API key is not configured", "ConfigError");

            var payload = new
            {
                model = request.Model,
                prompt = request.Prompt,
                n = request.N,
                size = $"{request.Width}x{request.Height}",
                quality = request.Quality,
                response_format = "url"
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var client = _httpClientFactory.CreateClient("OpenAi");
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, config.ServerUrl)
            {
                Content = content
            };
            requestMessage.Headers.Add("Authorization", $"Bearer {config.ApiKey}");

            var response = await client.SendAsync(requestMessage, cancellationToken);
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(responseJson);
                var data = doc.RootElement.GetProperty("data")[0];

                var imageUrl = data.GetProperty("url").GetString();
                if (string.IsNullOrEmpty(imageUrl))
                    return GenerationResult.Failure("API returned empty image URL", "ApiError");

                var revisedPrompt = data.TryGetProperty("revised_prompt", out var rp)
                    ? rp.GetString()
                    : null;

                var outputPath = await DownloadImageAsync(imageUrl, cancellationToken);

                return GenerationResult.Success(outputPath, revisedPrompt);
            }

            var error = $"API Error: {response.StatusCode} - {responseJson}";
            _logger.LogError("OpenAI API error: {Error}", error);
            return GenerationResult.Failure(error, "ApiError");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Image generation cancelled");
            return GenerationResult.Failure("Генерация отменена", "Cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при генерации изображения");
            return GenerationResult.Failure(ex.Message, "Exception");
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    private async Task<string> DownloadImageAsync(string imageUrl, CancellationToken cancellationToken = default)
    {
        var config = _configManager.GetConfig();
        Directory.CreateDirectory(config.OutputDirectory);

        var fileName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.png";
        var filePath = Path.Combine(config.OutputDirectory, fileName);

        var client = _httpClientFactory.CreateClient("OpenAi");
        var imageBytes = await client.GetByteArrayAsync(imageUrl, cancellationToken);

        await File.WriteAllBytesAsync(filePath, imageBytes, cancellationToken);

        return filePath;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var config = _configManager.GetConfig();
            if (string.IsNullOrEmpty(config.ApiKey))
                return false;

            var client = _httpClientFactory.CreateClient("OpenAi");
            using var request = new HttpRequestMessage(HttpMethod.Get, config.ServerUrl);
            request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.IsSuccessStatusCode
                || response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed
                || response.StatusCode == System.Net.HttpStatusCode.BadRequest;
        }
        catch
        {
            return false;
        }
    }
}

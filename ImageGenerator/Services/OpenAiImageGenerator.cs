using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ImageGenerator.Interfaces;
using ImageGenerator.Models;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.Services;

/// <summary>
/// Генератор изображений через OpenAI API (DALL-E)
/// </summary>
public class OpenAiImageGenerator : IImageGenerator
{
    private readonly HttpClient _httpClient;
    private readonly IConfigManager _configManager;
    private readonly ILogger<OpenAiImageGenerator> _logger;

    public OpenAiImageGenerator(
        HttpClient httpClient,
        IConfigManager configManager,
        ILogger<OpenAiImageGenerator> logger)
    {
        _httpClient = httpClient;
        _configManager = configManager;
        _logger = logger;
    }

    public async Task<GenerationResult> GenerateImageAsync(GenerationRequest request)
    {
        var result = new GenerationResult();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var config = _configManager.GetConfig();

            // Формируем запрос к OpenAI API
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

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");

            var response = await _httpClient.PostAsync(config.ServerUrl, content);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(responseJson);
                var imageUrl = doc.RootElement.GetProperty("data")[0].GetProperty("url").GetString();
                var revisedPrompt = doc.RootElement.GetProperty("data")[0].GetProperty("revised_prompt").GetString();

                // Скачиваем и сохраняем изображение
                var outputPath = await DownloadImageAsync(imageUrl!, request.Prompt);

                result.IsSuccess = true;
                result.ImagePath = outputPath;
                result.RevisedPrompt = revisedPrompt;
            }
            else
            {
                result.IsSuccess = false;
                result.ErrorMessage = $"API Error: {response.StatusCode} - {responseJson}";
                _logger.LogError(result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Ошибка при генерации изображения");
        }

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;

        return result;
    }

    private async Task<string> DownloadImageAsync(string imageUrl, string prompt)
    {
        var config = _configManager.GetConfig();
        Directory.CreateDirectory(config.OutputDirectory);

        var fileName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.png";
        var filePath = Path.Combine(config.OutputDirectory, fileName);

        using var httpClient = new HttpClient();
        var imageBytes = await httpClient.GetByteArrayAsync(imageUrl);
        await File.WriteAllBytesAsync(filePath, imageBytes);

        return filePath;
    }

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var config = _configManager.GetConfig();
            if (string.IsNullOrEmpty(config.ApiKey))
                return false;

            // Простой пинг-запрос
            var request = new HttpRequestMessage(HttpMethod.Head, config.ServerUrl);
            request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");
            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed;
        }
        catch
        {
            return false;
        }
    }
}
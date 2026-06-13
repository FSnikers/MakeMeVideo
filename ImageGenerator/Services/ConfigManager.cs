using System.IO;
using System.Text.Json;
using ImageGenerator.Interfaces;
using ImageGenerator.Models;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.Services;

/// <summary>
/// Менеджер конфигурации
/// </summary>
public class ConfigManager : IConfigManager
{
    private AppConfig _config;
    private readonly string _configPath;
    private readonly ILogger<ConfigManager> _logger;

    public ConfigManager(ILogger<ConfigManager> logger, string configPath = "appsettings.json")
    {
        _configPath = configPath;
        _logger = logger;
        LoadConfig();
    }

    private void LoadConfig()
    {
        if (!File.Exists(_configPath))
        {
            _logger.LogWarning("Файл конфигурации {ConfigPath} не найден, создаю с настройками по умолчанию", _configPath);
            _config = new AppConfig();
            SaveConfig();
        }
        else
        {
            var json = File.ReadAllText(_configPath);
            _config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
        }
    }

    private void SaveConfig()
    {
        var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_configPath, json);
    }

    public AppConfig GetConfig()
    {
        return _config;
    }

    public void ReloadConfig()
    {
        LoadConfig();
        _logger.LogInformation("Конфигурация перезагружена");
    }

    public bool ValidateConfig()
    {
        if (string.IsNullOrEmpty(_config.ApiKey))
        {
            _logger.LogError("API ключ не настроен в конфигурации");
            return false;
        }

        if (string.IsNullOrEmpty(_config.ServerUrl))
        {
            _logger.LogError("URL сервера не настроен в конфигурации");
            return false;
        }

        return true;
    }
}
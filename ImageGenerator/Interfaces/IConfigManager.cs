using ImageGenerator.Models;

namespace ImageGenerator.Interfaces;

/// <summary>
/// Менеджер конфигурации
/// </summary>
public interface IConfigManager
{
    AppConfig GetConfig();
    void ReloadConfig();
    bool ValidateConfig();
}
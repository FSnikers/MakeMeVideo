using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ImageGenerator.Interfaces;
using ImageGenerator.Models;
using ImageGenerator.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.Startup;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddImageGenerator(
        this IServiceCollection services,
        string? apiTypeOverride = null)
    {
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddSingleton<IConfigManager, ConfigManager>();
        services.AddSingleton<IPromptRepository, FilePromptRepository>();
        services.AddSingleton<IAccountStorage, JsonAccountStorage>();

        // Register HttpClient factory for internal use (downloads)
        services.AddHttpClient("Downloader")
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            });

        var apiType = apiTypeOverride ?? ReadConfigApiType();

        if (apiType == "ChatGPT")
        {
            services.AddSingleton<IImageGenerator>(sp =>
            {
                var accountStorage = sp.GetRequiredService<IAccountStorage>();
                var logger = sp.GetRequiredService<ILogger<ChatGptImageGenerator>>();
                var config = sp.GetRequiredService<IConfigManager>().GetConfig();
                return new ChatGptImageGenerator(accountStorage, logger, config.OutputDirectory, config.Headless);
            });
        }
        else
        {
            services.AddHttpClient("OpenAi")
                .ConfigureHttpClient(client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(120);
                });
            services.AddSingleton<IImageGenerator, OpenAiImageGenerator>();
        }

        return services;
    }

    public static async Task ValidateOrWarnAsync(this IServiceProvider services)
    {
        var configManager = services.GetRequiredService<IConfigManager>();
        var apiType = ReadConfigApiType();

        if (apiType != "ChatGPT")
        {
            if (!configManager.ValidateConfig())
            {
                Console.WriteLine("Ошибка: Проверьте настройки в appsettings.json");
                Console.WriteLine("Укажите ApiKey и ServerUrl");
                Environment.Exit(1);
            }
        }
        else
        {
            Console.WriteLine("Режим ChatGPT: убедитесь, что Chrome установлен");
            Console.WriteLine("и аккаунты настроены в chatgpt_accounts.json\n");
        }
    }

    private static string ReadConfigApiType()
    {
        try
        {
            var json = File.ReadAllText("appsettings.json");
            var config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(json);
            if (config != null && config.TryGetValue("ApiType", out var apiType))
                return apiType.GetString() ?? "ChatGPT";
        }
        catch { }
        return "ChatGPT";
    }
}

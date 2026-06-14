using ImageGenerator.Config;
using ImageGenerator.Config.Interfaces;
using ImageGenerator.Services;
using ImageGenerator.Services.AccountStorage;
using ImageGenerator.Services.AccountStorage.Interfaces;
using ImageGenerator.Services.Browser;
using ImageGenerator.Services.FilePrompt;
using ImageGenerator.Services.FilePrompt.Interfaces;
using ImageGenerator.Services.ImageGenerator.Abstractions;
using ImageGenerator.Services.ImageGenerator.ChatGPT;
using ImageGenerator.Services.ImageGenerator.ChatGPT.Actions;
using ImageGenerator.Services.ImageGenerator.ChatGPT.Modules;
using ImageGenerator.Services.ImageGenerator.ChatGPT.Modules.Interfaces;
using ImageGenerator.Services.ImageGenerator.OpenAI;
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

        services.AddSingleton<IBrowserDriverFactory>(sp =>
        {
            var config = sp.GetRequiredService<IConfigManager>().GetConfig();
            var factory = new BrowserDriverFactory(sp.GetRequiredService<ILogger<BrowserDriverFactory>>());
            factory.CreateDriver(config.Headless);
            return factory;
        });

        services.AddTransient<IChatGptPageDetectorModule, ChatGptPageDetectorModule>();
        services.AddTransient<IChatGptCredentialsModule, ChatGptCredentialsModule>();
        services.AddTransient<IChatGptPageInteractorModule, ChatGptPageInteractorModule>();

        services.AddTransient<IChatGptAuthAction, ChatGptAuthAction>();
        services.AddTransient<IChatGptSessionAction, ChatGptSessionAction>();
        services.AddTransient<IChatGptGenerateAction>(sp =>
        {
            var config = sp.GetRequiredService<IConfigManager>().GetConfig();
            return new ChatGptGenerateAction(
                sp.GetRequiredService<IBrowserDriverFactory>(),
                sp.GetRequiredService<IChatGptPageInteractorModule>(),
                sp.GetRequiredService<ILogger<ChatGptGenerateAction>>(),
                config.OutputDirectory);
        });

        // Register HttpClient factory for internal use (downloads)
        services.AddHttpClient("Downloader")
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            });

        var apiType = apiTypeOverride ?? ReadConfigApiType();

        if (apiType == "ChatGPT")
        {
            services.AddSingleton<IImageGenerator, ChatGptImageGenerator>();
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

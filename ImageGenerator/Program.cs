using ImageGenerator.Config.Interfaces;
using ImageGenerator.Services.FilePrompt.Interfaces;
using ImageGenerator.Services.ImageGenerator.Abstractions;
using ImageGenerator.Startup;
using Microsoft.Extensions.DependencyInjection;

ProcessKiller.KillChromeDrivers();

var services = new ServiceCollection();
services.AddImageGenerator();
var sp = services.BuildServiceProvider();

await sp.ValidateOrWarnAsync();

var generator = sp.GetRequiredService<IImageGenerator>();
var repository = sp.GetRequiredService<IPromptRepository>();
var configManager = sp.GetRequiredService<IConfigManager>();

while (true)
{
    Console.WriteLine("\n--- Главное меню ---");
    Console.WriteLine("1. Загрузить промпты из prompts.json");
    Console.WriteLine("2. Добавить новый промпт");
    Console.WriteLine("3. Запустить генерацию всех ожидающих промптов");
    Console.WriteLine("4. Запустить одиночную генерацию");
    Console.WriteLine("5. Выйти");
    Console.Write("\nВаш выбор: ");

    switch (Console.ReadLine()?.Trim())
    {
        case "1":
            await repository.LoadPromptsFromFileAsync(generator, configManager);
            break;
        case "2":
            await repository.AddNewPromptAsync();
            break;
        case "3":
            await generator.GenerateAllPendingPromptsAsync(repository, configManager);
            break;
        case "4":
            await generator.RunSingleGenerationAsync(configManager);
            break;
        case "5":
            Console.WriteLine("Выход...");
            return;
        default:
            Console.WriteLine("Неверный выбор");
            break;
    }
}

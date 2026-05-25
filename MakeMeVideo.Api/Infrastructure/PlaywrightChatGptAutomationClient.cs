using MakeMeVideo.Api.Domain;
using MakeMeVideo.Api.Options;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace MakeMeVideo.Api.Infrastructure;

public class PlaywrightChatGptAutomationClient(IOptions<PlaywrightOptions> options, ILogger<PlaywrightChatGptAutomationClient> logger) : IChatGptAutomationClient
{
    private readonly PlaywrightOptions _options = options.Value;

    public async Task<GenerationExecutionResult> GenerateImageAsync(GeneratorAccount account, GenerationProject project, ImageTaskItem imageTask, CancellationToken ct)
    {
        Directory.CreateDirectory(_options.StorageStateDirectory);
        var outputDir = Path.Combine("wwwroot", "GeneratedImages", project.Name);
        Directory.CreateDirectory(outputDir);

        for (var attempt = 1; attempt <= _options.MaxRetryAttempts; attempt++)
        {
            try
            {
                using var playwright = await Playwright.CreateAsync();
                await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = _options.Headless });
                var context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    StorageStatePath = account.StorageStatePath
                });
                var page = await context.NewPageAsync();
                page.SetDefaultTimeout(TimeSpan.FromSeconds(_options.ActionTimeoutSeconds).TotalMilliseconds);

                await page.GotoAsync(_options.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                var input = page.GetByPlaceholder("Message ChatGPT");
                if (!await input.IsVisibleAsync())
                    return new(false, false, "Account requires login or CAPTCHA", null);

                await page.GetByRole(AriaRole.Button, new() { Name = "New chat" }).ClickAsync();
                await input.FillAsync(project.GlobalPreCondition);
                await input.PressAsync("Enter");
                await input.WaitForAsync(new() { State = WaitForSelectorState.Visible });

                var prompt = $"{imageTask.Description} {project.TechnicalSuffix}".Trim();
                await input.FillAsync(prompt);
                await input.PressAsync("Enter");

                var limitText = page.GetByText("limit", new() { Exact = false });
                if (await limitText.IsVisibleAsync(new() { Timeout = 3000 }))
                    return new(false, true, "Image limit reached", null);

                var image = page.Locator("article img").Last;
                await image.WaitForAsync(new() { State = WaitForSelectorState.Visible });

                var resultPath = Path.Combine(outputDir, imageTask.FileName + ".png");
                await image.ScreenshotAsync(new() { Path = resultPath });
                await context.StorageStateAsync(new() { Path = account.StorageStatePath });

                return new(true, false, null, resultPath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Attempt {Attempt} failed for task {TaskId}", attempt, imageTask.Id);
                if (attempt == _options.MaxRetryAttempts)
                    return new(false, false, ex.Message, null);
            }
        }

        return new(false, false, "Unknown orchestration error", null);
    }
}

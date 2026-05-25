namespace MakeMeVideo.Api.Options;

public class PlaywrightOptions
{
    public const string SectionName = "Playwright";
    public bool Headless { get; set; } = true;
    public string BaseUrl { get; set; } = "https://chatgpt.com";
    public string StorageStateDirectory { get; set; } = "storage-states";
    public int MaxRetryAttempts { get; set; } = 3;
    public int ActionTimeoutSeconds { get; set; } = 120;
}

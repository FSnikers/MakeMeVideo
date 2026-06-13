using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using SeleniumUndetectedChromeDriver;

namespace ImageGenerator.Services.Browser;

public class BrowserDriverFactory : IBrowserDriverFactory
{
    private readonly ILogger<BrowserDriverFactory> _logger;
    private ChromeDriver? _driver;
    private bool _headless;
    private bool _disposed;

    private const int DefaultTimeoutSeconds = 300;

    public ChromeDriver? Driver => _driver;

    public BrowserDriverFactory(ILogger<BrowserDriverFactory> logger)
    {
        _logger = logger;
    }

    public void CreateDriver(bool headless)
    {
        _headless = headless;
        var userDataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chrome_profile");

        KillOrphanChromeProcesses(userDataDir);

        if (!Directory.Exists(userDataDir))
            Directory.CreateDirectory(userDataDir);

        var options = CreateChromeOptions(userDataDir, headless);

        var driverExecutablePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chromedriver.exe");

        _driver = UndetectedChromeDriver.Create(options, driverExecutablePath: driverExecutablePath, commandTimeout: TimeSpan.FromSeconds(90), configureService: service =>
        {
            service.HideCommandPromptWindow = true;
            service.SuppressInitialDiagnosticInformation = true;
        });

        _driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(90);
        ApplyAntiDetectionMeasures();
    }

    public void RestartDriver()
    {
        try
        {
            _driver?.Quit();
            _driver?.Dispose();
        }
        catch
        {
        }

        CreateDriver(_headless);
    }

    public void NavigateToUrl(string url)
    {
        if (_driver == null) return;
        _driver.Navigate().GoToUrl(url);
        WaitForPageLoad();
        BypassCloudflareIfNeeded(url);
    }

    public void EnsureOnChatPage()
    {
        if (_driver == null) return;

        var currentUrl = _driver.Url.ToLowerInvariant();
        if (!currentUrl.Contains("chatgpt.com") || currentUrl.Contains("auth") || currentUrl.Contains("login"))
        {
            NavigateToUrl("https://chatgpt.com/");
            Thread.Sleep(2000);
        }
    }

    public void NavigateToNewChat()
    {
        try
        {
            NavigateToUrl("https://chatgpt.com/");
            Thread.Sleep(2000);
        }
        catch
        {
            RestartDriver();
        }
    }

    public void WaitForPageLoad()
    {
        if (_driver == null) return;
        try
        {
            var tempWait = new WebDriverWait(_driver, TimeSpan.FromSeconds(30));
            tempWait.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").Equals("complete"));
            ApplyAntiDetectionMeasures();
        }
        catch
        {
        }
    }

    private static void KillOrphanChromeProcesses(string userDataDir)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -Command \"Get-CimInstance Win32_Process -Filter \\\"Name = 'chrome.exe' AND CommandLine LIKE '%{userDataDir.Replace("\\", "\\\\")}%'\\\" | ForEach-Object {{ Stop-Process -Id $_.ProcessId -Force }}\"",
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(10000);
        }
        catch { }
    }

    private static ChromeOptions CreateChromeOptions(string userDataDir, bool headless)
    {
        var options = new ChromeOptions();

        options.AddArgument("--no-first-run");
        options.AddArgument("--no-default-browser-check");
        options.AddArgument("--disable-search-engine-choice-screen");

        var userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                        "(KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";
        options.AddArgument($"--user-agent={userAgent}");

        options.AddArgument("--disable-blink-features=AutomationControlled");
        options.AddArgument("--disable-notifications");
        options.AddArgument("--lang=en-US");
        options.AddArgument("--disable-renderer-backgrounding");
        options.AddArgument("--disable-background-timer-throttling");
        options.AddArgument("--disable-backgrounding-occluded-windows");
        options.AddArgument("--remote-allow-origins=*");

        options.AddUserProfilePreference("credentials_enable_service", false);
        options.AddUserProfilePreference("profile.password_manager_enabled", false);
        options.AddUserProfilePreference("enable_do_not_track", true);
        options.AddUserProfilePreference("download_restrictions", 3);

        options.AddArgument($"--user-data-dir={userDataDir}");

        if (headless)
            options.AddArgument("--headless=new");

        return options;
    }

    private void ApplyAntiDetectionMeasures()
    {
        if (_driver == null) return;

        var script = @"
            Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
            Object.defineProperty(navigator, '__webdriver', { get: () => undefined });
            Object.defineProperty(navigator, '__driver_evaluate', { get: () => undefined });
            Object.defineProperty(navigator, '__selenium_evaluate', { get: () => undefined });
            Object.defineProperty(navigator, '__fxdriver_evaluate', { get: () => undefined });
            Object.defineProperty(navigator, '__webdriver_evaluate', { get: () => undefined });
            Object.defineProperty(navigator, '__lastWatirAlert', { get: () => undefined });
            Object.defineProperty(navigator, '__lastWatirConfirm', { get: () => undefined });
            Object.defineProperty(navigator, '__lastWatirPrompt', { get: () => undefined });

            window.chrome = { runtime: {} };
            Object.defineProperty(navigator, 'plugins', { get: () => [1, 2, 3, 4, 5] });
            Object.defineProperty(navigator, 'languages', { get: () => ['en-US', 'en', 'ru'] });

            const originalQuery = window.navigator.permissions.query;
            window.navigator.permissions.query = (parameters) => (
                parameters.name === 'notifications'
                    ? Promise.resolve({ state: Notification.permission })
                    : originalQuery(parameters)
            );

            Object.defineProperty(navigator, 'deviceMemory', { get: () => 8 });
            Object.defineProperty(navigator, 'hardwareConcurrency', { get: () => 8 });
            Object.defineProperty(navigator, 'maxTouchPoints', { get: () => 0 });
            Object.defineProperty(navigator, 'pdfViewerEnabled', { get: () => false });

            const getParameter = WebGLRenderingContext.prototype.getParameter;
            WebGLRenderingContext.prototype.getParameter = function(parameter) {
                if (parameter === 37445) return 'Intel Inc.';
                if (parameter === 37446) return 'Intel Iris OpenGL Engine';
                return getParameter(parameter);
            };
        ";

        _driver.ExecuteScript(script);
    }

    private void BypassCloudflareIfNeeded(string targetUrl)
    {
        if (_driver == null) return;

        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                var currentUrl = _driver.Url.ToLowerInvariant();
                var title = _driver.Title.ToLowerInvariant();
                var bodyText = _driver.FindElement(By.TagName("body")).Text.ToLowerInvariant();
                var pageSource = _driver.PageSource.ToLowerInvariant();

                var isCloudflare =
                    currentUrl.Contains("cloudflare") ||
                    currentUrl.Contains("challenge") ||
                    title.Contains("cloudflare") ||
                    title.Contains("just a moment") ||
                    bodyText.Contains("checking your browser") ||
                    bodyText.Contains("verifying you are human") ||
                    bodyText.Contains("enable javascript") ||
                    bodyText.Contains("cloudflare") ||
                    pageSource.Contains("cf-challenge") ||
                    pageSource.Contains("cf-browser-verification") ||
                    pageSource.Contains("_cf_chl_opt") ||
                    pageSource.Contains("__cf_chl_f_tk") ||
                    pageSource.Contains("cf-please-wait");

                if (!isCloudflare)
                {
                    var hasCfCookie = _driver.Manage().Cookies.AllCookies
                        .Any(c => c.Name.StartsWith("cf_clearance", StringComparison.OrdinalIgnoreCase));

                    if (hasCfCookie || !bodyText.Contains("checking your browser"))
                        return;
                }

                if (attempt == 0)
                {
                    Console.WriteLine("\n⚠️  Cloudflare блокирует доступ к ChatGPT.");
                    Console.WriteLine("⏳ Пожалуйста, решите капчу в открывшемся окне браузера...");
                    Console.WriteLine("🔍 После решения капчи страница загрузится автоматически.\n");
                }

                Thread.Sleep(2000);

                if (attempt > 0 && attempt % 10 == 0)
                {
                    Console.WriteLine($"⏳ Ожидание решения Cloudflare... ({attempt * 2} сек)");
                }
            }
            catch
            {
                Thread.Sleep(1000);
            }
        }

        _logger.LogWarning("Cloudflare challenge did not pass after ~120 seconds");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            try
            {
                _driver?.Quit();
                _driver?.Dispose();
            }
            catch
            {
            }

            _driver = null;
            _disposed = true;
        }
    }
}

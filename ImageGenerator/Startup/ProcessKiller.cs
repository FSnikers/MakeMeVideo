using System.Diagnostics;

namespace ImageGenerator.Startup;

public static class ProcessKiller
{
            
    public static void KillChromeDrivers()
    {
        try
        {
            var processes = Process.GetProcessesByName("chromedriver");
                
            if (processes.Length == 0)
            {
                Console.WriteLine("No ChromeDriver processes found by name.");
                return;
            }
                
            Console.WriteLine($"Found {processes.Length} ChromeDriver process(es) by name.");
                
            foreach (var process in processes)
            {
                try
                {
                    Console.Write($"Killing process ID {process.Id}... ");
                    process.Kill();
                    process.WaitForExit(3000);
                    Console.WriteLine("Done!");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed: {ex.Message}");
                }
                finally
                {
                    process?.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

}
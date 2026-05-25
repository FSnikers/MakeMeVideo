using MakeMeVideo.Api.Data;
using MakeMeVideo.Api.Domain;
using MakeMeVideo.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MakeMeVideo.Api.Workers;

public class GenerationWorker(IServiceProvider serviceProvider, ILogger<GenerationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var automation = scope.ServiceProvider.GetRequiredService<IChatGptAutomationClient>();

                var task = await db.ImageTasks
                    .Include(x => x.Project)
                    .Where(x => x.Status == ImageTaskStatus.Queued || x.Status == ImageTaskStatus.RetryPending)
                    .OrderBy(x => x.UpdatedAt)
                    .FirstOrDefaultAsync(stoppingToken);

                if (task is null)
                {
                    await Task.Delay(2000, stoppingToken);
                    continue;
                }

                var account = await db.Accounts
                    .Where(x => x.Status == GeneratorAccountStatus.Active)
                    .OrderByDescending(x => x.LastUsedDate)
                    .FirstOrDefaultAsync(stoppingToken);

                if (account is null)
                {
                    logger.LogWarning("No active account available for task {TaskId}", task.Id);
                    await Task.Delay(5000, stoppingToken);
                    continue;
                }

                task.Status = ImageTaskStatus.Processing;
                task.AccountId = account.Id;
                task.UpdatedAt = DateTimeOffset.UtcNow;
                task.Project.Status = GenerationProjectStatus.Processing;
                await db.SaveChangesAsync(stoppingToken);

                logger.LogInformation("Task {TaskId} started with account {Email}", task.Id, account.Email);
                var result = await automation.GenerateImageAsync(account, task.Project, task, stoppingToken);

                if (result.Success)
                {
                    task.Status = ImageTaskStatus.Completed;
                    task.ResultPath = result.SavedPath;
                    task.LastError = null;
                }
                else if (result.LimitReached)
                {
                    task.Status = ImageTaskStatus.RetryPending;
                    task.LastError = result.ErrorMessage;
                    account.Status = GeneratorAccountStatus.LimitReached;
                }
                else
                {
                    task.Status = ImageTaskStatus.Failed;
                    task.LastError = result.ErrorMessage;
                }

                account.LastUsedDate = DateTimeOffset.UtcNow;

                var hasPending = await db.ImageTasks.AnyAsync(x => x.ProjectId == task.ProjectId &&
                    (x.Status == ImageTaskStatus.Queued || x.Status == ImageTaskStatus.Processing || x.Status == ImageTaskStatus.RetryPending), stoppingToken);
                task.Project.Status = hasPending ? GenerationProjectStatus.Processing : GenerationProjectStatus.Completed;

                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Worker cycle failed");
                await Task.Delay(3000, stoppingToken);
            }
        }
    }
}

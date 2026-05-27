using System;
using System.Threading;
using System.Threading.Tasks;
using ChatGPTImageOrchestrator.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChatGPTImageOrchestrator.Web.BackgroundServices;

public class ImageGenerationWorker(IServiceProvider sp, IBackgroundTaskQueue queue, ILogger<ImageGenerationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var _ = await queue.DequeueAsync(stoppingToken);
            using var scope = sp.CreateScope();
            logger.LogInformation("Dequeued project for processing");
        }
    }
}
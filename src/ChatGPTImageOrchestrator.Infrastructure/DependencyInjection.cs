using ChatGPTImageOrchestrator.Application.Common.Interfaces;
using ChatGPTImageOrchestrator.Application.Services;
using ChatGPTImageOrchestrator.Domain.Interfaces;
using ChatGPTImageOrchestrator.Infrastructure.BrowserAutomation;
using ChatGPTImageOrchestrator.Infrastructure.Persistence;
using ChatGPTImageOrchestrator.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChatGPTImageOrchestrator.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string cs)
    {
        services.AddDbContext<AppDbContext>(x => x.UseSqlite(cs));
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IImageTaskRepository, ImageTaskRepository>();
        services.AddScoped<IChatGPTBrowserClient, ChatGPTBrowserClient>();
        services.AddScoped<IAccountPoolManager, AccountPoolManager>();
        services.AddScoped<IPromptBuilderService, PromptBuilderService>();
        services.AddScoped<ILimitDetectorService, LimitDetectorService>();
        services.AddScoped<IGenerationOrchestrator, GenerationOrchestrator>();
        return services;
    }
}
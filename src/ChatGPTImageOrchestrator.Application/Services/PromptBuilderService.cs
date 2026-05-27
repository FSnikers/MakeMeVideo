using ChatGPTImageOrchestrator.Application.Common.Interfaces;

namespace ChatGPTImageOrchestrator.Application.Services;

public class PromptBuilderService : IPromptBuilderService
{
    public string Build(string description, string suffix) => $"{description} {suffix}".Trim();
}
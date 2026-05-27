using System;
using System.Threading;
using System.Threading.Tasks;
using ChatGPTImageOrchestrator.Application.Common.Interfaces;
using ChatGPTImageOrchestrator.Application.DTOs;

namespace ChatGPTImageOrchestrator.Application.UseCases;

public class GetProjectStatusHandler(IGenerationOrchestrator orchestrator)
{
    public Task<ProjectStatusDto> Handle(Guid projectId, CancellationToken ct) => orchestrator.GetProjectStatusAsync(projectId, ct);
}
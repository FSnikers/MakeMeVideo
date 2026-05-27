using System;
using System.Threading;
using System.Threading.Tasks;
using ChatGPTImageOrchestrator.Application.Common.Interfaces;
using ChatGPTImageOrchestrator.Application.DTOs;

namespace ChatGPTImageOrchestrator.Application.UseCases;

public class CreateProjectHandler(IGenerationOrchestrator orchestrator)
{
    public Task<Guid> Handle(CreateProjectRequest request, CancellationToken ct) => orchestrator.CreateProjectAsync(request, ct);
}
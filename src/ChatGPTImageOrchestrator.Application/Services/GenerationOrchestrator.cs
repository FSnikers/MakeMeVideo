using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ChatGPTImageOrchestrator.Application.Common.Interfaces;
using ChatGPTImageOrchestrator.Application.DTOs;
using ChatGPTImageOrchestrator.Domain.Entities;
using ChatGPTImageOrchestrator.Domain.Enums;
using ChatGPTImageOrchestrator.Domain.Interfaces;

namespace ChatGPTImageOrchestrator.Application.Services;

public class GenerationOrchestrator(IProjectRepository projects, IImageTaskRepository tasks, IBackgroundTaskQueue queue) : IGenerationOrchestrator
{
    public async Task<Guid> CreateProjectAsync(CreateProjectRequest request, CancellationToken ct = default)
    {
        var p = new GenerationProject
        {
            Id = Guid.NewGuid(), Name = request.ProjectName, GlobalPreCondition = request.GlobalPreCondition, TechnicalSuffix = request.TechnicalPromptSuffix,
            Status = ProjectStatus.Queued
        };
        p.ImageTasks = request.Images.Select(i => new ImageTask { Id = Guid.NewGuid(), ProjectId = p.Id, FileName = i.FileName, Description = i.Description })
            .ToList();
        await projects.AddAsync(p, ct);
        await queue.QueueAsync(p.Id, ct);
        return p.Id;
    }
    public async Task<ProjectStatusDto> GetProjectStatusAsync(Guid projectId, CancellationToken ct = default)
    {
        var p = await projects.GetByIdAsync(projectId, ct) ?? throw new KeyNotFoundException();
        var items = await tasks.GetByProjectIdAsync(projectId, ct);
        return new ProjectStatusDto(p.Id, p.Name, p.Status,
            items.Select(x => new ImageTaskDto
                    { Id = x.Id, FileName = x.FileName, Description = x.Description, Status = x.Status, ResultPath = x.ResultPath, Error = x.LastError })
                .ToList());
    }
}
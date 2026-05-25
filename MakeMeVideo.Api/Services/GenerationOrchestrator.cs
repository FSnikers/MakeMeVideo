using MakeMeVideo.Api.Contracts;
using MakeMeVideo.Api.Data;
using MakeMeVideo.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace MakeMeVideo.Api.Services;

public class GenerationOrchestrator(AppDbContext db, ILogger<GenerationOrchestrator> logger) : IGenerationOrchestrator
{
    public async Task<Guid> CreateProjectAsync(CreateProjectRequest request)
    {
        var project = new GenerationProject
        {
            Id = Guid.NewGuid(),
            Name = request.ProjectName,
            GlobalPreCondition = request.GlobalPreCondition,
            TechnicalSuffix = request.TechnicalPromptSuffix,
            Status = GenerationProjectStatus.Queued,
            CreatedDate = DateTimeOffset.UtcNow,
            Images = request.Images.Select(x => new ImageTaskItem
            {
                Id = Guid.NewGuid(),
                FileName = x.FileName,
                Description = x.Description,
                Status = ImageTaskStatus.Queued,
                UpdatedAt = DateTimeOffset.UtcNow
            }).ToList()
        };

        db.Projects.Add(project);
        await db.SaveChangesAsync();
        logger.LogInformation("Project {ProjectId} created with {Count} images", project.Id, project.Images.Count);
        return project.Id;
    }

    public async Task<ProjectStatusDto> GetProjectStatusAsync(Guid projectId)
    {
        var project = await db.Projects.Include(x => x.Images).FirstOrDefaultAsync(x => x.Id == projectId)
                      ?? throw new KeyNotFoundException($"Project {projectId} not found");

        return new ProjectStatusDto(project.Id, project.Name, project.Status,
            project.Images.Select(x => new ImageTaskStatusDto(x.Id, x.FileName, x.Status, x.ResultPath, x.LastError)).ToList());
    }
}

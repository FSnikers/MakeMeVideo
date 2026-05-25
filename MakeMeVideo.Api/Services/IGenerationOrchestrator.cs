using MakeMeVideo.Api.Contracts;

namespace MakeMeVideo.Api.Services;

public interface IGenerationOrchestrator
{
    Task<Guid> CreateProjectAsync(CreateProjectRequest request);
    Task<ProjectStatusDto> GetProjectStatusAsync(Guid projectId);
}

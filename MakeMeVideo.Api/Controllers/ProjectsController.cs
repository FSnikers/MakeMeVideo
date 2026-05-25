using MakeMeVideo.Api.Contracts;
using MakeMeVideo.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace MakeMeVideo.Api.Controllers;

[ApiController]
[Route("api/projects")]
public class ProjectsController(IGenerationOrchestrator orchestrator) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<Guid>> Create([FromBody] CreateProjectRequest request)
    {
        var id = await orchestrator.CreateProjectAsync(request);
        return Accepted(new { projectId = id });
    }

    [HttpGet("{projectId:guid}")]
    public async Task<ActionResult<ProjectStatusDto>> GetStatus(Guid projectId)
    {
        var status = await orchestrator.GetProjectStatusAsync(projectId);
        return Ok(status);
    }
}

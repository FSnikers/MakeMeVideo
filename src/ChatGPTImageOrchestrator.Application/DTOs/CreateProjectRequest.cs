using System.Collections.Generic;

namespace ChatGPTImageOrchestrator.Application.DTOs;

public class CreateProjectRequest
{
    public string ProjectName { get; set; } = string.Empty;
    public string GlobalPreCondition { get; set; } = string.Empty;
    public string TechnicalPromptSuffix { get; set; } = string.Empty;
    public List<ImageTaskDto> Images { get; set; } = new();
}
namespace MakeMeVideo.Api.Contracts;

public class CreateProjectRequest
{
    public string ProjectName { get; set; } = string.Empty;
    public string GlobalPreCondition { get; set; } = string.Empty;
    public string TechnicalPromptSuffix { get; set; } = string.Empty;
    public List<CreateImageItemRequest> Images { get; set; } = new();
}

public class CreateImageItemRequest
{
    public string FileName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

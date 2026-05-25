namespace MakeMeVideo.Api.Domain;

public class GeneratorAccount
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Password { get; set; }
    public string? StorageStatePath { get; set; }
    public GeneratorAccountStatus Status { get; set; } = GeneratorAccountStatus.PendingLogin;
    public DateTimeOffset? LastUsedDate { get; set; }
    public ICollection<ImageTaskItem> ImageTasks { get; set; } = new List<ImageTaskItem>();
}

public class GenerationProject
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string GlobalPreCondition { get; set; } = string.Empty;
    public string TechnicalSuffix { get; set; } = string.Empty;
    public GenerationProjectStatus Status { get; set; } = GenerationProjectStatus.Queued;
    public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<ImageTaskItem> Images { get; set; } = new List<ImageTaskItem>();
}

public class ImageTaskItem
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public GenerationProject Project { get; set; } = default!;
    public string FileName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ImageTaskStatus Status { get; set; } = ImageTaskStatus.Queued;
    public string? ResultPath { get; set; }
    public string? LastError { get; set; }
    public Guid? AccountId { get; set; }
    public GeneratorAccount? Account { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

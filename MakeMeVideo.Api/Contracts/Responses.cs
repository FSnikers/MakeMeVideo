using MakeMeVideo.Api.Domain;

namespace MakeMeVideo.Api.Contracts;

public record ProjectStatusDto(Guid ProjectId, string ProjectName, GenerationProjectStatus Status, IReadOnlyList<ImageTaskStatusDto> Images);
public record ImageTaskStatusDto(Guid Id, string FileName, ImageTaskStatus Status, string? ResultPath, string? Error);

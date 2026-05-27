using System;
using System.Collections.Generic;
using ChatGPTImageOrchestrator.Domain.Enums;

namespace ChatGPTImageOrchestrator.Application.DTOs;

public record ProjectStatusDto(Guid ProjectId, string ProjectName, ProjectStatus Status, IReadOnlyList<ImageTaskDto> Images);
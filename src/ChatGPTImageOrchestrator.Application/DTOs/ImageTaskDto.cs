using System;
using ChatGPTImageOrchestrator.Domain.Enums;

namespace ChatGPTImageOrchestrator.Application.DTOs;

public class ImageTaskDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ImageTaskStatus Status { get; set; } = ImageTaskStatus.Queued;
    public string? ResultPath { get; set; }
    public string? Error { get; set; }
}
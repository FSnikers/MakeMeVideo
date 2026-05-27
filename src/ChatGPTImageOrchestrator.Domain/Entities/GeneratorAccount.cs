using System;
using ChatGPTImageOrchestrator.Domain.Enums;

namespace ChatGPTImageOrchestrator.Domain.Entities;

public class GeneratorAccount
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Password { get; set; }
    public string StorageStatePath { get; set; } = string.Empty;
    public AccountStatus Status { get; set; } = AccountStatus.PendingLogin;
    public DateTimeOffset? LastUsedDate { get; set; }
}
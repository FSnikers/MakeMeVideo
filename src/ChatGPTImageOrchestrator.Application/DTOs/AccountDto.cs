using System;
using ChatGPTImageOrchestrator.Domain.Enums;

namespace ChatGPTImageOrchestrator.Application.DTOs;

public record AccountDto(Guid Id, string Email, AccountStatus Status);
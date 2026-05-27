using System;
using System.Threading;
using System.Threading.Tasks;
using ChatGPTImageOrchestrator.Domain.Entities;

namespace ChatGPTImageOrchestrator.Domain.Interfaces;

public interface IProjectRepository
{
    Task AddAsync(GenerationProject project, CancellationToken ct);
    Task<GenerationProject?> GetByIdAsync(Guid id, CancellationToken ct);
    Task UpdateAsync(GenerationProject project, CancellationToken ct);
}
using System.Threading;
using System.Threading.Tasks;
using ChatGPTImageOrchestrator.Domain.Entities;

namespace ChatGPTImageOrchestrator.Domain.Interfaces;

public interface IAccountRepository
{
    Task<GeneratorAccount?> GetNextActiveAsync(CancellationToken ct);
    Task UpdateAsync(GeneratorAccount account, CancellationToken ct);
}
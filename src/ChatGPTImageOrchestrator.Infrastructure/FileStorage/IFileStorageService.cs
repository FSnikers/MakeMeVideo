using System.Threading;
using System.Threading.Tasks;

namespace ChatGPTImageOrchestrator.Infrastructure.FileStorage;

public interface IFileStorageService
{
    Task<string> SaveAsync(string projectName, string fileName, byte[] content, CancellationToken ct);
}
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ChatGPTImageOrchestrator.Infrastructure.FileStorage;

public class LocalFileStorageService : IFileStorageService
{
    public async Task<string> SaveAsync(string projectName, string fileName, byte[] content, CancellationToken ct)
    {
        var dir = Path.Combine("generated-images", projectName);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        await File.WriteAllBytesAsync(path, content, ct);
        return path;
    }
}
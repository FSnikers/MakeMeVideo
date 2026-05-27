using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ChatGPTImageOrchestrator.Application.Common.Interfaces;

namespace ChatGPTImageOrchestrator.Web.BackgroundServices;

public class InMemoryBackgroundTaskQueue(Channel<Guid> channel) : IBackgroundTaskQueue
{
    public ValueTask QueueAsync(Guid projectId, CancellationToken ct) => channel.Writer.WriteAsync(projectId, ct);
    public ValueTask<Guid> DequeueAsync(CancellationToken ct) => channel.Reader.ReadAsync(ct);
}
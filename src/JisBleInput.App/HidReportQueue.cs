using System.Collections.Concurrent;

namespace JisBleInput.App;

// Serialize notifications, including the release owed to a receiver after suspend.
internal sealed class HidReportQueue
{
    private readonly SemaphoreSlim writer = new(1, 1);
    private readonly ConcurrentDictionary<string, object> pending = new();

    public void RequireRelease(string id) => pending[id] = new object();
    public bool NeedsRelease(string id) => pending.ContainsKey(id);

    public async Task WriteAsync(string id, Func<Task> notify, CancellationToken cancellation)
    {
        await writer.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (NeedsRelease(id)) throw new InvalidOperationException("受信側のキー解放を待っています。");
            await notify().ConfigureAwait(false);
        }
        finally { writer.Release(); }
    }

    public async Task RecoverAsync(Func<string, Task<bool>> release)
    {
        foreach (var entry in pending)
        {
            await writer.WaitAsync().ConfigureAwait(false);
            try
            {
                if (pending.TryGetValue(entry.Key, out var ticket) && ReferenceEquals(ticket, entry.Value)
                    && await release(entry.Key).ConfigureAwait(false))
                    pending.TryRemove(entry); // A later suspend needs its own release.
            }
            finally { writer.Release(); }
        }
    }
}

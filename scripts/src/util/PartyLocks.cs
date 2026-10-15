using System.Collections.Concurrent;

namespace DiscordBot.scripts.src.util;

public static class PartyLocks
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public static SemaphoreSlim Get(string key) =>
        _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

    public static async Task<IDisposable> AcquireAsync(string key)
    {
        var slim = Get(key);
        await slim.WaitAsync();
        return new Releaser(slim);
    }

    private sealed class Releaser(SemaphoreSlim slim) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            slim.Release();
        }
    }
}

namespace WorkflowGallery.Api.Services;

public sealed class PlaygroundWriteGuard(TimeProvider clock)
{
    public const int AdmissionsPerMinute = 60;
    public const int MaxActiveRequests = 4;
    public const int MaxCatalogRequestBytes = 256 * 1024;
    public const int MaxTaskRequestBytes = 6 * 1024 * 1024;
    public const long MaxStoredAttachmentBytes = 512L * 1024 * 1024;
    public const long MaxCatalogRows = 5000;

    private readonly object _gate = new();
    private readonly Queue<DateTimeOffset> _admissions = new();
    private int _active;

    public static PlaygroundWriteGuard Worker { get; } = new(TimeProvider.System);

    public IDisposable? TryEnter(out int retryAfter)
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            while (_admissions.TryPeek(out var oldest) && now - oldest >= TimeSpan.FromMinutes(1))
                _admissions.Dequeue();
            retryAfter = _admissions.Count >= AdmissionsPerMinute
                ? Math.Max(1, (int)Math.Ceiling((_admissions.Peek().AddMinutes(1) - now).TotalSeconds))
                : 1;
            if (_active >= MaxActiveRequests || _admissions.Count >= AdmissionsPerMinute) return null;
            _admissions.Enqueue(now);
            _active++;
            return new Lease(this);
        }
    }

    private sealed class Lease(PlaygroundWriteGuard owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (owner._gate) owner._active--;
        }
    }
}

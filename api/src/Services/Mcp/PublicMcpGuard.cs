namespace WorkflowGallery.Api.Services.Mcp;

/// <summary>Worker-local protection only; does not reserve SQL spend or impose a global quota.</summary>
public sealed class PublicMcpGuard(TimeProvider clock)
{
    public const int MaxRequestBytes = 64 * 1024;
    public const int MaxResponseBytes = 512 * 1024;
    public const int MaxPageSize = 25;
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);
    public const int MaxActiveRequests = 2;
    public const int AdmissionsPerMinute = 60;
    private readonly object _gate = new();
    private readonly Queue<DateTimeOffset> _admissions = new();
    private int _active;

    // Used by direct function construction as well as DI: never allocate one limiter per request.
    public static PublicMcpGuard Worker { get; } = new(TimeProvider.System);

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

    private sealed class Lease(PublicMcpGuard owner) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (owner._gate) owner._active--;
        }
    }
}

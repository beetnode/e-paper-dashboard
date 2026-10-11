using EPaperDashboard.Models;

namespace EPaperDashboard.Services.Rendering;

/// <summary>One active render per owner, with bounded FIFO queues dispatched round-robin.</summary>
public sealed class UserRenderScheduler(IConfiguration configuration)
{
    private readonly object _gate = new();
    private readonly Dictionary<UserId, OwnerQueue> _owners = new();
    private readonly LinkedList<UserId> _ready = new();
    private readonly int _maximumActive = ReadLimit(configuration, "RENDERING_MAX_CONCURRENT_RENDERS", 2, 1, 16);
    private readonly int _maximumPending = ReadLimit(configuration, "RENDERING_MAX_QUEUED_RENDERS", 100, 0, 1000);
    private readonly int _maximumPendingPerUser = ReadLimit(configuration, "RENDERING_MAX_QUEUED_RENDERS_PER_USER", 5, 0, 100);
    private int _active;
    private int _pending;

    public async Task<T> RunAsync<T>(UserId owner, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        if (owner == UserId.Empty) throw new RenderingRequestException("A rendering owner is required.");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromMinutes(5));
        var job = new Job(lifetime);
        lock (_gate)
        {
            lifetime.Token.ThrowIfCancellationRequested();
            if (!_owners.TryGetValue(owner, out var queue)) queue = new OwnerQueue();
            var canStart = queue.Active is null && _active < _maximumActive && _ready.Count == 0;
            if (!canStart && (queue.Pending.Count >= _maximumPendingPerUser || _pending >= _maximumPending))
                throw new RenderingRequestException("The rendering queue is full. Please try again later.");
            _owners[owner] = queue;
            job.Node = queue.Pending.AddLast(job);
            _pending++;
            MakeReady(owner, queue);
            Dispatch();
        }
        using var registration = lifetime.Token.Register(() => CancelPending(owner, job));
        try
        {
            await job.Ready.Task;
            lifetime.Token.ThrowIfCancellationRequested();
            return await operation(lifetime.Token);
        }
        finally
        {
            lock (_gate)
            {
                if (_owners.TryGetValue(owner, out var queue) && queue.Active == job)
                {
                    queue.Active = null;
                    _active--;
                    MakeReady(owner, queue);
                    RemoveEmpty(owner, queue);
                    Dispatch();
                }
            }
        }
    }

    public void CancelUser(UserId owner)
    {
        Job[] jobs;
        lock (_gate)
        {
            if (!_owners.TryGetValue(owner, out var queue)) return;
            jobs = queue.Pending.Concat(queue.Active is null ? [] : new[] { queue.Active }).ToArray();
        }
        foreach (var job in jobs)
        {
            try { job.Lifetime.Cancel(); }
            catch (ObjectDisposedException) { /* The job completed after the snapshot. */ }
        }
    }

    private void CancelPending(UserId owner, Job job)
    {
        lock (_gate)
        {
            if (job.Node?.List is null) return;
            var queue = _owners[owner];
            queue.Pending.Remove(job.Node);
            _pending--;
            job.Ready.TrySetCanceled(job.Lifetime.Token);
            if (queue.Pending.Count == 0 && queue.ReadyNode is not null)
            {
                _ready.Remove(queue.ReadyNode);
                queue.ReadyNode = null;
            }
            RemoveEmpty(owner, queue);
            Dispatch();
        }
    }

    private void MakeReady(UserId owner, OwnerQueue queue)
    {
        if (queue.Active is null && queue.Pending.Count > 0 && queue.ReadyNode is null)
            queue.ReadyNode = _ready.AddLast(owner);
    }

    private void RemoveEmpty(UserId owner, OwnerQueue queue)
    {
        if (queue.Active is null && queue.Pending.Count == 0) _owners.Remove(owner);
    }

    private void Dispatch()
    {
        while (_active < _maximumActive && _ready.First is not null)
        {
            var owner = _ready.First.Value;
            _ready.RemoveFirst();
            var queue = _owners[owner];
            queue.ReadyNode = null;
            var job = queue.Pending.First!.Value;
            queue.Pending.RemoveFirst();
            queue.Active = job;
            _pending--;
            _active++;
            job.Ready.TrySetResult();
        }
    }

    private static int ReadLimit(IConfiguration configuration, string key, int fallback, int minimum, int maximum) =>
        configuration[key] is not { } value ? fallback
            : int.TryParse(value, out var limit) && limit >= minimum && limit <= maximum ? limit
            : throw new InvalidOperationException($"{key} must be between {minimum} and {maximum}.");

    private sealed class OwnerQueue
    {
        public LinkedList<Job> Pending { get; } = new();
        public Job? Active { get; set; }
        public LinkedListNode<UserId>? ReadyNode { get; set; }
    }

    private sealed class Job(CancellationTokenSource lifetime)
    {
        public CancellationTokenSource Lifetime { get; } = lifetime;
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LinkedListNode<Job>? Node { get; set; }
    }
}

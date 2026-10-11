namespace EPaperDashboard.Services.Components.Playwright;

/// <summary>Tracks one remote job, including cancellation arriving before submission.</summary>
internal sealed class RemoteRenderJobs : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Job> _jobs = new();

    internal Job? Start(Guid id, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Prune();
            if (id == Guid.Empty || _jobs.ContainsKey(id) || _jobs.Count >= 4096) return null;
            var job = new Job(cancellationToken);
            _jobs.Add(id, job);
            return job;
        }
    }

    internal Task CancelAsync(Guid id)
    {
        Job job;
        lock (_gate)
        {
            Prune();
            if (!_jobs.TryGetValue(id, out job!))
            {
                if (id == Guid.Empty || _jobs.Count >= 4096)
                    throw new InvalidOperationException("Remote job registry is full or the job ID is invalid.");
                // A delayed submission cannot start after cancellation was acknowledged.
                job = new Job(CancellationToken.None);
                job.Complete();
                _jobs.Add(id, job);
            }
        }
        job.Cancel();
        return job.Completion.Task;
    }

    private void Prune()
    {
        foreach (var (id, job) in _jobs.ToArray())
            if (job.CompletedAt is { } completed && completed < DateTimeOffset.UtcNow.AddMinutes(-5))
            {
                _jobs.Remove(id);
                job.Dispose();
            }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var job in _jobs.Values) job.Dispose();
            _jobs.Clear();
        }
    }

    internal sealed class Job(CancellationToken cancellationToken) : IDisposable
    {
        private readonly CancellationTokenSource _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        private readonly object _gate = new();
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private DateTimeOffset? _completedAt;
        internal DateTimeOffset? CompletedAt { get { lock (_gate) return _completedAt; } }
        internal CancellationToken Token => _lifetime.Token;
        internal void Cancel()
        {
            lock (_gate)
                if (CompletedAt is null) _lifetime.Cancel();
        }
        internal void Complete()
        {
            lock (_gate)
            {
                if (CompletedAt is not null) return;
                _completedAt = DateTimeOffset.UtcNow;
                Completion.TrySetResult();
                _lifetime.Dispose();
            }
        }
        public void Dispose() { Cancel(); Complete(); }
    }
}

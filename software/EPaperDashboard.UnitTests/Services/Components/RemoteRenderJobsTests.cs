using EPaperDashboard.Services.Components.Playwright;
using FluentAssertions;
using Xunit;

namespace EPaperDashboard.UnitTests.Services.Components;

public sealed class RemoteRenderJobsTests
{
    [Fact]
    public async Task CancellationWaitsForOnlyTheRequestedWorker()
    {
        using var jobs = new RemoteRenderJobs();
        var firstId = Guid.NewGuid(); var secondId = Guid.NewGuid();
        var first = jobs.Start(firstId, default)!;
        var second = jobs.Start(secondId, default)!;
        var cancelled = jobs.CancelAsync(firstId);
        first.Token.IsCancellationRequested.Should().BeTrue();
        second.Token.IsCancellationRequested.Should().BeFalse();
        cancelled.IsCompleted.Should().BeFalse();
        first.Complete();
        await cancelled.WaitAsync(TimeSpan.FromSeconds(1));
        second.Completion.Task.IsCompleted.Should().BeFalse();
        second.Complete();
    }

    [Fact]
    public async Task CancellationBeforeSubmissionPreventsDelayedWorkerStart()
    {
        using var jobs = new RemoteRenderJobs(); var id = Guid.NewGuid();
        await jobs.CancelAsync(id);
        jobs.Start(id, default).Should().BeNull();
    }

    [Fact]
    public void DuplicateOrEmptyJobIdsAreRejected()
    {
        using var jobs = new RemoteRenderJobs(); var id = Guid.NewGuid();
        jobs.Start(id, default).Should().NotBeNull();
        jobs.Start(id, default).Should().BeNull();
        jobs.Start(Guid.Empty, default).Should().BeNull();
    }
}

using EPaperDashboard.Models;
using EPaperDashboard.Services.Rendering;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EPaperDashboard.UnitTests.Services.Rendering;

public sealed class UserRenderSchedulerTests
{
    private static UserRenderScheduler Create(int concurrent = 2, int pendingPerUser = 5) => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RENDERING_MAX_CONCURRENT_RENDERS"] = concurrent.ToString(),
            ["RENDERING_MAX_QUEUED_RENDERS_PER_USER"] = pendingPerUser.ToString()
        }).Build());

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task OneUserCannotOccupyBothSlots_AndTheirJobsStayInOrder()
    {
        var scheduler = Create();
        var alice = UserId.New(); var bob = UserId.New();
        var hold = Gate(); var bobStarted = Gate();
        var order = new List<int>();
        var first = scheduler.RunAsync(alice, async _ => { await hold.Task; return 1; }, default);
        var second = scheduler.RunAsync(alice, _ => { order.Add(2); return Task.FromResult(2); }, default);
        var third = scheduler.RunAsync(alice, _ => { order.Add(3); return Task.FromResult(3); }, default);
        var other = scheduler.RunAsync(bob, _ => { bobStarted.SetResult(); return Task.FromResult(4); }, default);
        await bobStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        second.IsCompleted.Should().BeFalse();
        hold.SetResult();
        await Task.WhenAll(first, second, third, other).WaitAsync(TimeSpan.FromSeconds(5));
        order.Should().Equal(2, 3);
    }

    [Fact]
    public async Task PendingOwnersGetATurnBeforeTheSameOwnerRunsAgain()
    {
        var scheduler = Create(1);
        var alice = UserId.New(); var bob = UserId.New();
        var hold = Gate(); var order = new List<string>();
        var first = scheduler.RunAsync(alice, async _ => { await hold.Task; return 0; }, default);
        var second = scheduler.RunAsync(alice, _ => { order.Add("alice"); return Task.FromResult(0); }, default);
        var other = scheduler.RunAsync(bob, _ => { order.Add("bob"); return Task.FromResult(0); }, default);
        hold.SetResult();
        await Task.WhenAll(first, second, other).WaitAsync(TimeSpan.FromSeconds(5));
        order.Should().Equal("bob", "alice");
    }

    [Fact]
    public async Task CancelUserCancelsOnlyTheirActiveAndPendingJobs()
    {
        var scheduler = Create();
        var alice = UserId.New(); var bob = UserId.New();
        var holdBob = Gate();
        var active = scheduler.RunAsync(alice, async token => { await Task.Delay(Timeout.Infinite, token); return 0; }, default);
        var pending = scheduler.RunAsync(alice, _ => Task.FromResult(0), default);
        var other = scheduler.RunAsync(bob, async _ => { await holdBob.Task; return 1; }, default);
        scheduler.CancelUser(alice);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active.WaitAsync(TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        other.IsCompleted.Should().BeFalse();
        holdBob.SetResult();
        (await other).Should().Be(1);
        (await scheduler.RunAsync(alice, _ => Task.FromResult(2), default)).Should().Be(2);
    }

    [Fact]
    public async Task ZeroQueueBudgetAllowsImmediateWorkButRejectsWaitingWork()
    {
        var scheduler = Create(1, 0); var owner = UserId.New(); var hold = Gate();
        var active = scheduler.RunAsync(owner, async _ => { await hold.Task; return 1; }, default);
        await Assert.ThrowsAsync<RenderingRequestException>(() => scheduler.RunAsync(owner, _ => Task.FromResult(0), default));
        hold.SetResult();
        (await active).Should().Be(1);
    }

    [Fact]
    public async Task CancelledQueuedRequestReleasesItsQueueBudget()
    {
        var scheduler = Create(1, 1); var owner = UserId.New(); var hold = Gate();
        var active = scheduler.RunAsync(owner, async _ => { await hold.Task; return 0; }, default);
        using var cancellation = new CancellationTokenSource();
        var pending = scheduler.RunAsync(owner, _ => Task.FromResult(0), cancellation.Token);
        await Assert.ThrowsAsync<RenderingRequestException>(() => scheduler.RunAsync(owner, _ => Task.FromResult(0), default));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        var replacement = scheduler.RunAsync(owner, _ => Task.FromResult(1), default);
        hold.SetResult();
        await active;
        (await replacement).Should().Be(1);
    }
}

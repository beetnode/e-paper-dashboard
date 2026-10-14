using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using EPaperDashboard.Services;
using EPaperDashboard.Services.Rendering;
using EPaperDashboard.Utilities;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services.Rendering;

public sealed class RenderingAccessServiceTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IDashboardRepository> _dashboards = new();
    private readonly Mock<IDeploymentStrategy> _deployment = new();
    public RenderingAccessServiceTests() => _deployment.SetupGet(d => d.Mode).Returns(DeploymentMode.Standalone);

    private RenderingAccessService Create() => new(
        new UserService(_users.Object, _dashboards.Object, deployment: _deployment.Object), _dashboards.Object,
        new UserRenderScheduler(new ConfigurationBuilder().Build()));

    [Fact]
    public void UsersWithoutAnExplicitOptInHaveNoAccess()
    {
        var fresh = new User { Id = UserId.New(), RenderingEnabled = false };
        var legacy = new User { Id = UserId.New() };
        var optedIn = new User { Id = UserId.New() };
        _users.Setup(r => r.FindById(fresh.Id)).Returns(fresh);
        _users.Setup(r => r.FindById(legacy.Id)).Returns(legacy);
        _users.Setup(r => r.FindById(optedIn.Id)).Returns(optedIn);
        _users.Setup(r => r.GetRenderingEnabled(optedIn.Id)).Returns(true);
        var access = Create();
        access.IsEnabled(fresh.Id).Should().BeFalse();
        access.IsEnabled(legacy.Id).Should().BeFalse();
        access.IsEnabled(optedIn.Id).Should().BeTrue();
        access.IsEnabled(UserId.New()).Should().BeFalse();
    }

    [Fact]
    public async Task OtherOwnersDashboardNeverReachesWorker()
    {
        var user = new User { Id = UserId.New(), RenderingEnabled = true };
        var dashboard = new Dashboard { Id = DashboardId.New(), UserId = UserId.New() };
        _users.Setup(r => r.FindById(user.Id)).Returns(user);
        _dashboards.Setup(r => r.FindById(dashboard.Id)).Returns(dashboard);
        var invoked = false;
        await Assert.ThrowsAsync<RenderingRequestException>(() => Create().RunAsync(user.Id, dashboard.Id, _ =>
        {
            invoked = true;
            return Task.FromResult(1);
        }, default));
        invoked.Should().BeFalse();
    }

    [Fact]
    public async Task DisablingOneAccountCancelsOnlyThatAccount()
    {
        var alice = new User { Id = UserId.New(), RenderingEnabled = true };
        var bob = new User { Id = UserId.New(), RenderingEnabled = true };
        _users.Setup(r => r.FindById(alice.Id)).Returns(alice);
        _users.Setup(r => r.FindById(bob.Id)).Returns(bob);
        _users.Setup(r => r.SetRenderingEnabled(alice.Id, false)).Callback(() =>
            _users.Setup(r => r.GetRenderingEnabled(alice.Id)).Returns(false));
        var access = Create();
        var aliceJob = access.RunAsync(alice.Id, null, async token =>
        {
            await Task.Delay(Timeout.Infinite, token); return 1;
        }, default);
        var bobHold = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bobJob = access.RunAsync(bob.Id, null, _ => bobHold.Task, default);
        access.SetEnabled(alice.Id, false);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => aliceJob.WaitAsync(TimeSpan.FromSeconds(5)));
        bobJob.IsCompleted.Should().BeFalse();
        access.IsEnabled(alice.Id).Should().BeFalse();
        access.IsEnabled(bob.Id).Should().BeTrue();
        bobHold.SetResult(2);
        (await bobJob).Should().Be(2);
    }

    [Fact]
    public async Task PreferenceChangedOnAnotherReplicaCancelsActiveJob()
    {
        var user = new User { Id = UserId.New() };
        _users.Setup(r => r.FindById(user.Id)).Returns(user);
        var enabled = 1;
        _users.Setup(r => r.GetRenderingEnabled(user.Id)).Returns(() => Volatile.Read(ref enabled) == 1);
        var job = Create().RunAsync(user.Id, null, async token =>
        {
            await Task.Delay(Timeout.Infinite, token); return 0;
        }, default);
        Interlocked.Exchange(ref enabled, 0);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => job.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void AddonAllowsOnlyItsIngressIdentity()
    {
        _deployment.SetupGet(d => d.Mode).Returns(DeploymentMode.Addon);
        var access = Create();
        access.IsEnabled(Constants.HomeAssistantVirtualUserId).Should().BeFalse();
        _users.Setup(r => r.GetRenderingEnabled(Constants.HomeAssistantVirtualUserId)).Returns(true);
        access.IsEnabled(Constants.HomeAssistantVirtualUserId).Should().BeTrue();
        access.IsEnabled(UserId.New()).Should().BeFalse();
        access.SetEnabled(Constants.HomeAssistantVirtualUserId, false);
        _users.Verify(r => r.SetRenderingEnabled(Constants.HomeAssistantVirtualUserId, false), Times.Once);
    }
}

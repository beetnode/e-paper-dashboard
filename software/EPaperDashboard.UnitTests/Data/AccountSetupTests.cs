using EPaperDashboard.Data.LiteDb;
using EPaperDashboard.Models;
using EPaperDashboard.Services;
using EPaperDashboard.Utilities;
using FluentAssertions;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Data;

public sealed class AccountSetupTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "izboard-setup-" + Guid.NewGuid().ToString("N"));
    private LiteDbContext Open()
    {
        Directory.CreateDirectory(_directory);
        var environment = new Mock<IEnvironmentConfiguration>();
        environment.SetupGet(e => e.ConfigDir).Returns(_directory);
        return new LiteDbContext(environment.Object);
    }

    [Fact]
    public async Task ConcurrentSetupCreatesExactlyOneAdministrator()
    {
        using var context = Open();
        var users = new LiteDbUserRepository(context);
        users.IsSetupComplete().Should().BeFalse();
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => Task.Run(() =>
            users.TryCreateAdministrator(new User { Username = "admin-" + i }))));
        results.Count(result => result).Should().Be(1);
        users.GetAll().Should().ContainSingle().Which.IsSuperUser.Should().BeTrue();
        users.IsSetupComplete().Should().BeTrue();
    }

    [Fact]
    public void AiConfigOfAccountlessOwnerSurvivesRestart()
    {
        using (var context = Open())
        {
            new LiteDbUserRepository(context).SetAiConfig(Constants.HomeAssistantVirtualUserId,
                new AiConfig { ConnectionMode = AiConnectionMode.Direct, DirectEndpoint = "http://llm", DirectModel = "m" });
        }

        using (var context = Open())
        {
            var users = new LiteDbUserRepository(context);
            users.GetAiConfig(Constants.HomeAssistantVirtualUserId).Should().BeEquivalentTo(
                new AiConfig { ConnectionMode = AiConnectionMode.Direct, DirectEndpoint = "http://llm", DirectModel = "m" });
            users.GetAiConfig(UserId.New()).Should().BeNull();
            users.IsSetupComplete().Should().BeFalse();
        }
    }

    [Fact]
    public void RemovingAllAccountsDoesNotReopenSetupAfterRestart()
    {
        using (var context = Open())
        {
            var users = new LiteDbUserRepository(context);
            var admin = new User { Username = "admin" };
            users.TryCreateAdministrator(admin).Should().BeTrue();
            users.Delete(admin.Id);
        }
        using var restarted = Open();
        var repository = new LiteDbUserRepository(restarted);
        repository.IsSetupComplete().Should().BeTrue();
        repository.TryCreateAdministrator(new User { Username = "attacker" }).Should().BeFalse();
    }

    [Fact]
    public void ExistingRegularAccountClosesSetupAndPreferencesAreIndependent()
    {
        var alice = new User { Username = "alice" }; var bob = new User { Username = "bob" };
        using (var context = Open())
        {
            var users = new LiteDbUserRepository(context);
            users.Insert(alice); users.Insert(bob);
        }
        using var restarted = Open();
        var repository = new LiteDbUserRepository(restarted);
        repository.TryCreateAdministrator(new User { Username = "attacker" }).Should().BeFalse();
        repository.SetRenderingEnabled(alice.Id, false);
        repository.SetRenderingEnabled(bob.Id, true);
        alice.Nickname = "Updated";
        repository.Update(alice);
        repository.GetRenderingEnabled(alice.Id).Should().BeFalse();
        repository.GetRenderingEnabled(bob.Id).Should().BeTrue();
        repository.Delete(alice.Id); repository.Delete(bob.Id);
        repository.IsSetupComplete().Should().BeTrue();
    }

    [Fact]
    public async Task ConcurrentRegistrationCannotCreateDuplicateUsernames()
    {
        using var context = Open();
        var users = new LiteDbUserRepository(context);
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
            users.TryInsert(new User { Username = "alice" }))));
        results.Count(result => result).Should().Be(1);
        users.GetAll().Should().ContainSingle().Which.Username.Should().Be("alice");
    }

    [Fact]
    public void DeleteByUserIdRemovesOnlyThatUsersDevicesAndPairingSessions()
    {
        using var context = Open();
        var devices = new LiteDbDeviceRepository(context);
        var sessions = new LiteDbPairingSessionRepository(context);
        var (alice, bob) = (UserId.New(), UserId.New());
        devices.Insert(new Device { UserId = alice, DeviceIdentifier = "a", ApiKey = "ka" });
        devices.Insert(new Device { UserId = bob, DeviceIdentifier = "b", ApiKey = "kb" });
        sessions.Insert(new PairingSession { UserId = alice, Code = "AAAAAA" });
        sessions.Insert(new PairingSession { UserId = bob, Code = "BBBBBB" });

        devices.DeleteByUserId(alice);
        sessions.DeleteByUserId(alice);

        devices.FindByUserId(alice).Should().BeEmpty();
        devices.FindByUserId(bob).Should().ContainSingle();
        sessions.FindByCode("AAAAAA").Should().BeNull();
        sessions.FindByCode("BBBBBB").Should().NotBeNull();
    }

    [Fact]
    public async Task ConcurrentDemotionsLeaveAnActiveAdministrator()
    {
        using var context = Open();
        var users = new LiteDbUserRepository(context);
        var service = new UserService(users, new LiteDbDashboardRepository(context), unitOfWork: new LiteDbUnitOfWork(context));
        var (alice, bob) = (new User { Username = "alice", IsSuperUser = true }, new User { Username = "bob", IsSuperUser = true });
        users.Insert(alice); users.Insert(bob);

        await Task.WhenAll(Enumerable.Range(0, 12).Select(i => Task.Run(() =>
            i % 2 == 0 ? service.SetSuperUser(alice.Id, bob.Id, false) : service.SetSuperUser(bob.Id, alice.Id, false))));

        users.GetAll().Count(u => u.IsSuperUser).Should().Be(1);
    }

    [Fact]
    public void ExistsOtherActiveSuperUserIgnoresSuspendedAndRegularAccounts()
    {
        using var context = Open();
        var users = new LiteDbUserRepository(context);
        var admin = new User { Username = "admin", IsSuperUser = true };
        users.Insert(admin);
        users.Insert(new User { Username = "regular" });
        users.Insert(new User { Username = "suspended", IsSuperUser = true, IsSuspended = true });

        users.ExistsOtherActiveSuperUser(admin.Id).Should().BeFalse();
        var second = new User { Username = "second", IsSuperUser = true };
        users.Insert(second);
        users.ExistsOtherActiveSuperUser(admin.Id).Should().BeTrue();
    }

    [Fact]
    public void SetupCodeIsSharedUntilSetupCompletes()
    {
        using var context = Open();
        var users = new LiteDbUserRepository(context);

        users.GetOrCreateSetupCode("FIRST111").Should().Be("FIRST111");
        users.GetOrCreateSetupCode("SECOND22").Should().Be("FIRST111");

        users.TryCreateAdministrator(new User { Username = "admin" }).Should().BeTrue();
        users.GetOrCreateSetupCode("THIRD333").Should().Be("THIRD333");
    }

    [Fact]
    public void ExistsByUsernameIgnoreCaseMatchesAnyCase()
    {
        using var context = Open();
        var users = new LiteDbUserRepository(context);
        users.Insert(new User { Username = "Alice" });

        users.ExistsByUsernameIgnoreCase("alice").Should().BeTrue();
        users.ExistsByUsernameIgnoreCase("ALICE").Should().BeTrue();
        users.ExistsByUsernameIgnoreCase("bob").Should().BeFalse();
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}

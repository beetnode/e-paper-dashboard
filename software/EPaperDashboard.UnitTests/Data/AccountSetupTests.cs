using EPaperDashboard.Data.LiteDb;
using EPaperDashboard.Models;
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

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}

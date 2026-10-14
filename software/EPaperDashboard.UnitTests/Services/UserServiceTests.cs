using CSharpFunctionalExtensions;
using EPaperDashboard.Authentication;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using EPaperDashboard.Services;
using EPaperDashboard.UnitTests.TestSupport;
using EPaperDashboard.Utilities;
using FluentAssertions;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services;

public class UserServiceTests
{
    private static (UserService sut, Mock<IUserRepository> users, Mock<IDashboardRepository> dashboards) CreateSut()
    {
        var users = new Mock<IUserRepository>();
        var dashboards = new Mock<IDashboardRepository>();
        return (new UserService(users.Object, dashboards.Object), users, dashboards);
    }

    [Fact]
    public void VerifyCredentials_CorrectPassword_ReturnsUser()
    {
        var (sut, users, _) = CreateSut();
        var user = new User { Username = "alice", PasswordHash = PasswordHashing.Hash("secret") };
        users.Setup(r => r.FindByUsername("alice")).Returns(user);

        sut.VerifyCredentials("alice", "secret").Should().Be(Maybe.From(user));
        users.Verify(r => r.Update(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public void VerifyCredentials_WrongPassword_ReturnsNone()
    {
        var (sut, users, _) = CreateSut();
        var user = new User { Username = "alice", PasswordHash = PasswordHashing.Hash("secret") };
        users.Setup(r => r.FindByUsername("alice")).Returns(user);

        sut.VerifyCredentials("alice", "wrong").HasValue.Should().BeFalse();
    }

    [Fact]
    public void VerifyCredentials_UnknownUsername_ReturnsNone()
    {
        var (sut, users, _) = CreateSut();
        users.Setup(r => r.FindByUsername("ghost")).Returns(Maybe<User>.None);

        sut.VerifyCredentials("ghost", "anything").HasValue.Should().BeFalse();
    }

    [Fact]
    public void VerifyCredentials_LegacyHash_SignsInAndUpgradesTheStoredHash()
    {
        var (sut, users, _) = CreateSut();
        var user = new User { Username = "alice", PasswordHash = PasswordHashing.LegacySha256("secret") };
        users.Setup(r => r.FindByUsername("alice")).Returns(user);

        sut.VerifyCredentials("alice", "secret").HasValue.Should().BeTrue();

        PasswordHashing.Verify(user.PasswordHash, "secret").Should().Be(PasswordCheck.Valid);
        users.Verify(r => r.Update(user), Times.Once);
    }

    [Fact]
    public void VerifyCredentials_LegacyHashWithWrongPassword_KeepsTheStoredHash()
    {
        var (sut, users, _) = CreateSut();
        var legacy = PasswordHashing.LegacySha256("secret");
        var user = new User { Username = "alice", PasswordHash = legacy };
        users.Setup(r => r.FindByUsername("alice")).Returns(user);

        sut.VerifyCredentials("alice", "wrong").HasValue.Should().BeFalse();

        user.PasswordHash.Should().Be(legacy);
        users.Verify(r => r.Update(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public void TryCreateUser_UsernameAlreadyExists_ReturnsFalseAndDoesNotInsert()
    {
        var (sut, users, _) = CreateSut();
        users.Setup(r => r.ExistsByUsername("alice")).Returns(true);

        var result = sut.TryCreateUser("alice", "pw");

        result.Should().BeFalse();
        users.Verify(r => r.TryInsert(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public void TryCreateUser_NewUsername_InsertsHashedPasswordAndReturnsTrue()
    {
        var (sut, users, _) = CreateSut();
        users.Setup(r => r.ExistsByUsername("bob")).Returns(false);
        User? inserted = null;
        users.Setup(r => r.TryInsert(It.IsAny<User>())).Callback<User>(u => inserted = u).Returns(true);

        var result = sut.TryCreateUser("bob", "pw123");

        result.Should().BeTrue();
        inserted.Should().NotBeNull();
        inserted!.Username.Should().Be("bob");
        PasswordHashing.Verify(inserted.PasswordHash, "pw123").Should().Be(PasswordCheck.Valid);
    }

    [Fact]
    public void TryDeleteUser_LastSuperUser_ReturnsFalseAndDoesNotDelete()
    {
        var (sut, users, dashboards) = CreateSut();
        var user = new User { Id = UserId.New(), IsSuperUser = true };
        users.Setup(r => r.FindById(user.Id)).Returns(user);
        users.Setup(r => r.ExistsOtherActiveSuperUser(user.Id)).Returns(false);

        var result = sut.TryDeleteUser(user.Id);

        result.Should().BeFalse();
        users.Verify(r => r.Delete(It.IsAny<UserId>()), Times.Never);
        dashboards.Verify(r => r.DeleteByUserId(It.IsAny<UserId>()), Times.Never);
    }

    [Fact]
    public void TryDeleteUser_RegularUser_DeletesUserAndTheirDashboards()
    {
        var (sut, users, dashboards) = CreateSut();
        var user = new User { Id = UserId.New(), IsSuperUser = false };
        users.Setup(r => r.FindById(user.Id)).Returns(user);

        var result = sut.TryDeleteUser(user.Id);

        result.Should().BeTrue();
        dashboards.Verify(r => r.DeleteByUserId(user.Id), Times.Once);
        users.Verify(r => r.Delete(user.Id), Times.Once);
    }

    [Fact]
    public void TryDeleteUser_RegularUser_RemovesTheirDevicesAndPairingSessions()
    {
        var users = new Mock<IUserRepository>();
        var devices = new Mock<IDeviceRepository>();
        var pairingSessions = new Mock<IPairingSessionRepository>();
        var sut = new UserService(users.Object, new Mock<IDashboardRepository>().Object,
            devices: devices.Object, pairingSessions: pairingSessions.Object, unitOfWork: ImmediateUnitOfWork.Instance);
        var user = new User { Id = UserId.New(), IsSuperUser = false };
        users.Setup(r => r.FindById(user.Id)).Returns(user);

        sut.TryDeleteUser(user.Id).Should().BeTrue();

        devices.Verify(r => r.DeleteByUserId(user.Id), Times.Once);
        pairingSessions.Verify(r => r.DeleteByUserId(user.Id), Times.Once);
    }

    [Fact]
    public void TryDeleteUser_RunsUnderTheUsersLock()
    {
        var users = new Mock<IUserRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.ExecuteExclusive("users", It.IsAny<Func<bool>>())).Returns(false);
        var sut = new UserService(users.Object, new Mock<IDashboardRepository>().Object, unitOfWork: unitOfWork.Object);

        sut.TryDeleteUser(UserId.New()).Should().BeFalse();

        unitOfWork.Verify(u => u.ExecuteExclusive("users", It.IsAny<Func<bool>>()), Times.Once);
        users.Verify(r => r.FindById(It.IsAny<UserId>()), Times.Never);
    }

    [Fact]
    public void TryChangePassword_WrongOldPassword_ReturnsFalseAndDoesNotUpdate()
    {
        var (sut, users, _) = CreateSut();
        var user = new User { Id = UserId.New(), PasswordHash = PasswordHashing.LegacySha256("original") };
        users.Setup(r => r.FindById(user.Id)).Returns(user);

        var result = sut.TryChangePassword(user.Id, "wrong-old", "new-password");

        result.Should().BeFalse();
        users.Verify(r => r.Update(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public void TryChangePassword_NewPasswordSameAsOld_ReturnsFalseAndDoesNotUpdate()
    {
        var (sut, users, _) = CreateSut();
        var user = new User { Id = UserId.New(), PasswordHash = PasswordHashing.LegacySha256("same") };
        users.Setup(r => r.FindById(user.Id)).Returns(user);

        var result = sut.TryChangePassword(user.Id, "same", "same");

        result.Should().BeFalse();
        users.Verify(r => r.Update(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public void TryChangePassword_ValidChange_UpdatesHashAndReturnsTrue()
    {
        var (sut, users, _) = CreateSut();
        var user = new User { Id = UserId.New(), PasswordHash = PasswordHashing.LegacySha256("original") };
        users.Setup(r => r.FindById(user.Id)).Returns(user);

        var result = sut.TryChangePassword(user.Id, "original", "brand-new");

        result.Should().BeTrue();
        PasswordHashing.Verify(user.PasswordHash, "brand-new").Should().Be(PasswordCheck.Valid);
        users.Verify(r => r.Update(user), Times.Once);
    }

    [Fact]
    public void TryChangePassword_CurrentHash_AcceptsOldPasswordAndRejectsReuse()
    {
        var (sut, users, _) = CreateSut();
        var user = new User { Id = UserId.New(), PasswordHash = PasswordHashing.Hash("original") };
        users.Setup(r => r.FindById(user.Id)).Returns(user);

        sut.TryChangePassword(user.Id, "original", "original").Should().BeFalse();
        sut.TryChangePassword(user.Id, "original", "brand-new").Should().BeTrue();

        PasswordHashing.Verify(user.PasswordHash, "brand-new").Should().Be(PasswordCheck.Valid);
    }

    [Fact]
    public void TryCreateAdministrator_StoresCurrentHash()
    {
        var (sut, users, _) = CreateSut();
        User? created = null;
        users.Setup(r => r.TryCreateAdministrator(It.IsAny<User>())).Callback<User>(u => created = u).Returns(true);

        sut.TryCreateAdministrator("admin", "secret").Should().BeTrue();

        PasswordHashing.Verify(created!.PasswordHash, "secret").Should().Be(PasswordCheck.Valid);
    }

    [Fact]
    public void TryCreateUser_GivesTheAccountAStampAndCreationTime()
    {
        var (sut, users, _) = CreateSut();
        User? inserted = null;
        users.Setup(r => r.TryInsert(It.IsAny<User>())).Callback<User>(u => inserted = u).Returns(true);

        sut.TryCreateUser("bob", "password").Should().BeTrue();

        inserted!.SecurityStamp.Should().NotBeNullOrEmpty();
        inserted.CreatedAt.Should().NotBeNull();
    }

    [Fact]
    public void TryChangePassword_RotatesTheStampAndDropsCachedSessions()
    {
        var users = new Mock<IUserRepository>();
        var sessions = new UserSessionValidator(users.Object, TimeProvider.System);
        var sut = new UserService(users.Object, new Mock<IDashboardRepository>().Object, sessions: sessions);
        var user = new User { Id = UserId.New(), Username = "alice", PasswordHash = PasswordHashing.Hash("original"), SecurityStamp = "old" };
        users.Setup(r => r.FindById(user.Id)).Returns(user);
        var previousSession = UserClaims.Create(user);
        sessions.Validate(previousSession);

        sut.TryChangePassword(user.Id, "original", "brand-new").Should().BeTrue();

        user.SecurityStamp.Should().NotBe("old").And.NotBeNullOrEmpty();
        sessions.Validate(previousSession).State.Should().Be(SessionState.Reject);
    }

    [Fact]
    public void TryDeleteUser_DropsCachedSessions()
    {
        var users = new Mock<IUserRepository>();
        var sessions = new UserSessionValidator(users.Object, TimeProvider.System);
        var sut = new UserService(users.Object, new Mock<IDashboardRepository>().Object, sessions: sessions);
        var user = new User { Id = UserId.New(), Username = "bob", SecurityStamp = "s" };
        users.Setup(r => r.FindById(user.Id)).Returns(user);
        var session = UserClaims.Create(user);
        sessions.Validate(session);

        sut.TryDeleteUser(user.Id).Should().BeTrue();
        users.Setup(r => r.FindById(user.Id)).Returns(Maybe<User>.None);

        sessions.Validate(session).State.Should().Be(SessionState.Reject);
    }

    [Fact]
    public void GetAiConfig_HomeAssistantAddonUser_ReadsServiceStateWithoutAccount()
    {
        var (sut, users, _) = CreateSut();
        var config = new AiConfig { ConnectionMode = AiConnectionMode.HomeAssistant };
        users.Setup(r => r.GetAiConfig(Constants.HomeAssistantVirtualUserId)).Returns(config);

        sut.HasAccount(Constants.HomeAssistantVirtualUserId).Should().BeTrue();
        sut.GetAiConfig(Constants.HomeAssistantVirtualUserId).Should().BeSameAs(config);
        users.Verify(r => r.FindById(It.IsAny<UserId>()), Times.Never);
    }

    [Fact]
    public void TryUpdateAiConfig_HomeAssistantAddonUser_StoresServiceState()
    {
        var (sut, users, _) = CreateSut();
        var config = new AiConfig { ConnectionMode = AiConnectionMode.HomeAssistant };

        sut.TryUpdateAiConfig(Constants.HomeAssistantVirtualUserId, config).Should().BeTrue();

        users.Verify(r => r.SetAiConfig(Constants.HomeAssistantVirtualUserId, config), Times.Once);
        users.Verify(r => r.Update(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public void TryUpdateAiConfig_ExistingUser_UpdatesAccount()
    {
        var (sut, users, _) = CreateSut();
        var user = new User { Id = UserId.New(), Username = "alice" };
        var config = new AiConfig { ConnectionMode = AiConnectionMode.Direct };
        users.Setup(r => r.FindById(user.Id)).Returns(user);

        sut.TryUpdateAiConfig(user.Id, config).Should().BeTrue();

        sut.GetAiConfig(user.Id).Should().BeSameAs(config);
        users.Verify(r => r.Update(user), Times.Once);
        users.Verify(r => r.SetAiConfig(It.IsAny<UserId>(), It.IsAny<AiConfig>()), Times.Never);
    }

    [Fact]
    public void TryUpdateAiConfig_UnknownUser_ReturnsFalse()
    {
        var (sut, users, _) = CreateSut();
        var id = UserId.New();
        users.Setup(r => r.FindById(id)).Returns(Maybe<User>.None);

        sut.HasAccount(id).Should().BeFalse();
        sut.GetAiConfig(id).Should().BeNull();
        sut.TryUpdateAiConfig(id, new AiConfig()).Should().BeFalse();
        users.Verify(r => r.Update(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public void HasAccount_AddonMode_AcceptsOnlyIngressUser()
    {
        var users = new Mock<IUserRepository>();
        var deployment = new Mock<IDeploymentStrategy>();
        deployment.SetupGet(d => d.IsUserManagementEnabled).Returns(false);
        var stored = new User { Id = UserId.New(), Username = "left-over", AiConfig = new AiConfig() };
        users.Setup(r => r.FindById(stored.Id)).Returns(stored);
        var sut = new UserService(users.Object, Mock.Of<IDashboardRepository>(), deployment: deployment.Object);

        sut.HasAccount(Constants.HomeAssistantVirtualUserId).Should().BeTrue();
        sut.HasAccount(stored.Id).Should().BeFalse();
        sut.GetAiConfig(stored.Id).Should().BeNull();
        sut.TryUpdateAiConfig(stored.Id, new AiConfig()).Should().BeFalse();
        sut.TrySetRenderingEnabled(stored.Id, true).Should().BeFalse();
        users.Verify(r => r.SetRenderingEnabled(It.IsAny<UserId>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void HasAccount_EmptyId_ReturnsFalseWithoutLookup()
    {
        var (sut, users, _) = CreateSut();

        sut.HasAccount(UserId.Empty).Should().BeFalse();

        users.Verify(r => r.FindById(It.IsAny<UserId>()), Times.Never);
    }
}

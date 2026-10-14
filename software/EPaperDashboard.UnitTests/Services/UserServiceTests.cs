using CSharpFunctionalExtensions;
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
    public void IsUserValid_CorrectPassword_ReturnsTrue()
    {
        var (sut, users, _) = CreateSut();
        var user = new User { Username = "alice", PasswordHash = UserService.ComputeSha256Hash("secret") };
        users.Setup(r => r.FindByUsername("alice")).Returns(user);

        sut.IsUserValid("alice", "secret").Should().BeTrue();
    }

    [Fact]
    public void IsUserValid_WrongPassword_ReturnsFalse()
    {
        var (sut, users, _) = CreateSut();
        var user = new User { Username = "alice", PasswordHash = UserService.ComputeSha256Hash("secret") };
        users.Setup(r => r.FindByUsername("alice")).Returns(user);

        sut.IsUserValid("alice", "wrong").Should().BeFalse();
    }

    [Fact]
    public void IsUserValid_UnknownUsername_ReturnsFalse()
    {
        var (sut, users, _) = CreateSut();
        users.Setup(r => r.FindByUsername("ghost")).Returns(Maybe<User>.None);

        sut.IsUserValid("ghost", "anything").Should().BeFalse();
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
        inserted.PasswordHash.Should().Be(UserService.ComputeSha256Hash("pw123"));
    }

    [Fact]
    public void TryDeleteUser_UserIsSuperUser_ReturnsFalseAndDoesNotDelete()
    {
        var (sut, users, dashboards) = CreateSut();
        var user = new User { Id = UserId.New(), IsSuperUser = true };
        users.Setup(r => r.FindById(user.Id)).Returns(user);

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
        var user = new User { Id = UserId.New(), PasswordHash = UserService.ComputeSha256Hash("original") };
        users.Setup(r => r.FindById(user.Id)).Returns(user);

        var result = sut.TryChangePassword(user.Id, "wrong-old", "new-password");

        result.Should().BeFalse();
        users.Verify(r => r.Update(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public void TryChangePassword_NewPasswordSameAsOld_ReturnsFalseAndDoesNotUpdate()
    {
        var (sut, users, _) = CreateSut();
        var user = new User { Id = UserId.New(), PasswordHash = UserService.ComputeSha256Hash("same") };
        users.Setup(r => r.FindById(user.Id)).Returns(user);

        var result = sut.TryChangePassword(user.Id, "same", "same");

        result.Should().BeFalse();
        users.Verify(r => r.Update(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public void TryChangePassword_ValidChange_UpdatesHashAndReturnsTrue()
    {
        var (sut, users, _) = CreateSut();
        var user = new User { Id = UserId.New(), PasswordHash = UserService.ComputeSha256Hash("original") };
        users.Setup(r => r.FindById(user.Id)).Returns(user);

        var result = sut.TryChangePassword(user.Id, "original", "brand-new");

        result.Should().BeTrue();
        user.PasswordHash.Should().Be(UserService.ComputeSha256Hash("brand-new"));
        users.Verify(r => r.Update(user), Times.Once);
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

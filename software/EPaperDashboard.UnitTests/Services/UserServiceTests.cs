using CSharpFunctionalExtensions;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using EPaperDashboard.Services;
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
}

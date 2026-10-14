using EPaperDashboard.Controllers;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using EPaperDashboard.Services;
using EPaperDashboard.UnitTests.TestSupport;
using FluentAssertions;
using System.Security.Claims;
using EPaperDashboard.Utilities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Controllers;

public class UsersApiControllerTests
{
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IDashboardRepository> _dashboardRepository = new();

    private UsersApiController CreateSut() => new(new UserService(_userRepository.Object, _dashboardRepository.Object));

    [Fact]
    public void ChangeNickname_UserExists_ReturnsOk()
    {
        var userId = UserId.New();
        _userRepository.Setup(r => r.FindById(userId)).Returns(new User { Id = userId });
        var sut = CreateSut().WithUser(userId);

        var result = sut.ChangeNickname(new ChangeNicknameRequest("New Nick"));

        result.Should().BeOfType<OkObjectResult>();
        _userRepository.Verify(r => r.Update(It.Is<User>(u => u.Nickname == "New Nick")), Times.Once);
    }

    [Fact]
    public void ChangeNickname_UserDoesNotExist_ReturnsBadRequest()
    {
        var userId = UserId.New();
        _userRepository.Setup(r => r.FindById(userId)).Returns(CSharpFunctionalExtensions.Maybe<User>.None);
        var sut = CreateSut().WithUser(userId);

        var result = sut.ChangeNickname(new ChangeNicknameRequest("New Nick"));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ChangePassword_MissingFields_ReturnsBadRequest()
    {
        var sut = CreateSut().WithUser(UserId.New());

        var result = await sut.ChangePassword(new ChangePasswordRequest("", "new", "new"));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ChangePassword_ConfirmationDoesNotMatch_ReturnsBadRequest()
    {
        var sut = CreateSut().WithUser(UserId.New());

        var result = await sut.ChangePassword(new ChangePasswordRequest("old", "new1", "new2"));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_ReturnsBadRequest()
    {
        var userId = UserId.New();
        var user = new User { Id = userId, PasswordHash = PasswordHashing.LegacySha256("correct") };
        _userRepository.Setup(r => r.FindById(userId)).Returns(user);
        var sut = CreateSut().WithUser(userId);

        var result = await sut.ChangePassword(new ChangePasswordRequest("wrong", "newpass", "newpass"));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ChangePassword_ValidChange_ReturnsOkAndUpdatesHash()
    {
        var userId = UserId.New();
        var user = new User { Id = userId, PasswordHash = PasswordHashing.LegacySha256("correct") };
        _userRepository.Setup(r => r.FindById(userId)).Returns(user);
        var sut = CreateSut().WithUser(userId);

        var authentication = new Mock<IAuthenticationService>();
        sut.HttpContext.RequestServices = new ServiceCollection().AddSingleton(authentication.Object).BuildServiceProvider();

        var result = await sut.ChangePassword(new ChangePasswordRequest("correct", "newpass", "newpass"));

        result.Should().BeOfType<OkObjectResult>();
        PasswordHashing.Verify(user.PasswordHash, "newpass").Should().Be(PasswordCheck.Valid);
        authentication.Verify(a => a.SignInAsync(It.IsAny<HttpContext>(), CookieAuthenticationDefaults.AuthenticationScheme,
            It.Is<ClaimsPrincipal>(p => p.FindFirst(Constants.SecurityStampClaim)!.Value == user.SecurityStamp),
            It.IsAny<AuthenticationProperties>()), Times.Once);
    }

    [Fact]
    public void DeleteProfile_RegularUser_DeletesAndReturnsOk()
    {
        var userId = UserId.New();
        _userRepository.Setup(r => r.FindById(userId)).Returns(new User { Id = userId, IsSuperUser = false });
        var sut = CreateSut().WithUser(userId);

        var result = sut.DeleteProfile();

        result.Should().BeOfType<OkObjectResult>();
        _userRepository.Verify(r => r.Delete(userId), Times.Once);
    }

    [Fact]
    public void DeleteProfile_SuperUser_ReturnsBadRequestAndDoesNotDelete()
    {
        var userId = UserId.New();
        _userRepository.Setup(r => r.FindById(userId)).Returns(new User { Id = userId, IsSuperUser = true });
        var sut = CreateSut().WithUser(userId);

        var result = sut.DeleteProfile();

        result.Should().BeOfType<BadRequestObjectResult>();
        _userRepository.Verify(r => r.Delete(It.IsAny<UserId>()), Times.Never);
    }

    [Fact]
    public void GetAllUsers_ReturnsProjectedUserList()
    {
        _userRepository.Setup(r => r.GetAll()).Returns([new User { Username = "alice" }]);
        var sut = CreateSut();

        var result = sut.GetAllUsers();

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public void AddUser_MissingCredentials_ReturnsBadRequest()
    {
        var sut = CreateSut();

        var result = sut.AddUser(new AddUserRequest("", ""));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void AddUser_UsernameAlreadyExists_ReturnsBadRequest()
    {
        _userRepository.Setup(r => r.ExistsByUsername("bob")).Returns(true);
        var sut = CreateSut();

        var result = sut.AddUser(new AddUserRequest("bob", "password"));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void AddUser_NewUsername_CreatesUserAndReturnsOk()
    {
        _userRepository.Setup(r => r.ExistsByUsername("bob")).Returns(false);
        _userRepository.Setup(r => r.TryInsert(It.IsAny<User>())).Returns(true);
        var sut = CreateSut();

        var result = sut.AddUser(new AddUserRequest("bob", "password"));

        result.Should().BeOfType<OkObjectResult>();
        _userRepository.Verify(r => r.TryInsert(It.Is<User>(u => u.Username == "bob")), Times.Once);
    }

    [Fact]
    public void DeleteUser_InvalidId_ReturnsBadRequest()
    {
        var sut = CreateSut();

        var result = sut.DeleteUser("not-a-valid-id");

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void DeleteUser_TargetIsSuperUser_ReturnsBadRequest()
    {
        var targetId = UserId.New();
        _userRepository.Setup(r => r.FindById(targetId)).Returns(new User { Id = targetId, IsSuperUser = true });
        var sut = CreateSut();

        var result = sut.DeleteUser(targetId.Value);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void DeleteUser_ValidTarget_DeletesAndReturnsOk()
    {
        var targetId = UserId.New();
        _userRepository.Setup(r => r.FindById(targetId)).Returns(new User { Id = targetId, IsSuperUser = false });
        var sut = CreateSut();

        var result = sut.DeleteUser(targetId.Value);

        result.Should().BeOfType<OkObjectResult>();
        _userRepository.Verify(r => r.Delete(targetId), Times.Once);
    }
}

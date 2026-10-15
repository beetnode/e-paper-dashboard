using EPaperDashboard.Utilities;
using EPaperDashboard.Authentication;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using EPaperDashboard.Services;
using EPaperDashboard.UnitTests.TestSupport;
using FluentAssertions;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services;

public class UserAdministrationTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly User _admin = new() { Id = UserId.New(), Username = "admin", IsSuperUser = true, SecurityStamp = "a" };
    private readonly User _bob = new() { Id = UserId.New(), Username = "bob", SecurityStamp = "b" };
    private readonly User _carol = new() { Id = UserId.New(), Username = "carol", IsSuperUser = true, SecurityStamp = "c" };

    public UserAdministrationTests()
    {
        foreach (var user in new[] { _admin, _bob, _carol })
        {
            _users.Setup(r => r.FindById(user.Id)).Returns(user);
        }
        _unitOfWork.Setup(u => u.ExecuteExclusive("users", It.IsAny<Func<AdminActionResult>>()))
            .Returns((string _, Func<AdminActionResult> operation) => operation());
        _unitOfWork.Setup(u => u.ExecuteExclusive("users", It.IsAny<Func<bool>>()))
            .Returns((string _, Func<bool> operation) => operation());
    }

    private UserService CreateSut(UserSessionValidator? sessions = null) =>
        new(_users.Object, new Mock<IDashboardRepository>().Object, unitOfWork: _unitOfWork.Object, sessions: sessions);

    private void OtherActiveAdmins(bool exist) =>
        _users.Setup(r => r.ExistsOtherActiveSuperUser(It.IsAny<UserId>())).Returns(exist);

    [Fact]
    public void SetSuperUser_PromotesARegularAccount()
    {
        CreateSut().SetSuperUser(_admin.Id, _bob.Id, true).Should().Be(AdminActionResult.Done);

        _bob.IsSuperUser.Should().BeTrue();
        _users.Verify(r => r.Update(_bob), Times.Once);
        _unitOfWork.Verify(u => u.ExecuteExclusive("users", It.IsAny<Func<AdminActionResult>>()), Times.Once);
    }

    [Fact]
    public void SetSuperUser_DemotesAnAdminWhileAnotherRemains()
    {
        OtherActiveAdmins(true);

        CreateSut().SetSuperUser(_admin.Id, _carol.Id, false).Should().Be(AdminActionResult.Done);

        _carol.IsSuperUser.Should().BeFalse();
        _carol.SecurityStamp.Should().NotBe("c");
    }

    [Fact]
    public void SetSuperUser_RefusesToDemoteTheLastActiveAdmin()
    {
        OtherActiveAdmins(false);

        CreateSut().SetSuperUser(_admin.Id, _carol.Id, false).Should().Be(AdminActionResult.LastAdministrator);

        _carol.IsSuperUser.Should().BeTrue();
        _users.Verify(r => r.Update(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public void AdminActions_RefuseToTargetTheActingAccount()
    {
        var sut = CreateSut();

        sut.SetSuperUser(_admin.Id, _admin.Id, false).Should().Be(AdminActionResult.SelfNotAllowed);
        sut.SetSuspended(_admin.Id, _admin.Id, true).Should().Be(AdminActionResult.SelfNotAllowed);
        sut.ResetPassword(_admin.Id, _admin.Id).Result.Should().Be(AdminActionResult.SelfNotAllowed);
        sut.DeleteUserAsAdmin(_admin.Id, _admin.Id).Should().Be(AdminActionResult.SelfNotAllowed);
        _users.Verify(r => r.Update(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public void AdminActions_ReportUnknownAccounts()
    {
        var ghost = UserId.New();
        _users.Setup(r => r.FindById(ghost)).Returns((User?)null);
        var sut = CreateSut();

        sut.SetSuperUser(_admin.Id, ghost, true).Should().Be(AdminActionResult.NotFound);
        sut.SetSuspended(_admin.Id, ghost, true).Should().Be(AdminActionResult.NotFound);
        sut.ResetPassword(_admin.Id, ghost).Result.Should().Be(AdminActionResult.NotFound);
        sut.DeleteUserAsAdmin(_admin.Id, ghost).Should().Be(AdminActionResult.NotFound);
    }

    [Fact]
    public void SetSuspended_SuspendsAndEndsSessions()
    {
        _users.Setup(r => r.FindById(_bob.Id)).Returns(_bob);
        var sessions = new UserSessionValidator(_users.Object, TimeProvider.System);
        var session = UserClaims.Create(_bob);
        sessions.Validate(session);

        CreateSut(sessions).SetSuspended(_admin.Id, _bob.Id, true).Should().Be(AdminActionResult.Done);

        _bob.IsSuspended.Should().BeTrue();
        sessions.Validate(session).State.Should().Be(SessionState.Reject);
    }

    [Fact]
    public void SetSuspended_RefusesToSuspendTheLastActiveAdmin()
    {
        OtherActiveAdmins(false);

        CreateSut().SetSuspended(_admin.Id, _carol.Id, true).Should().Be(AdminActionResult.LastAdministrator);

        _carol.IsSuspended.Should().BeFalse();
    }

    [Fact]
    public void SuspendedAccount_CannotSignInOrOwnData()
    {
        _bob.IsSuspended = true;
        _bob.PasswordHash = PasswordHashing.Hash("secret");
        _users.Setup(r => r.FindByUsername("bob")).Returns(_bob);
        var sut = CreateSut();

        sut.HasAccount(_bob.Id).Should().BeFalse();
        sut.VerifyCredentials("bob", "secret")!.IsSuspended.Should().BeTrue();
    }

    [Fact]
    public void ResetPassword_IssuesATemporaryPasswordThatMustBeChanged()
    {
        var (result, temporaryPassword) = CreateSut().ResetPassword(_admin.Id, _bob.Id);

        result.Should().Be(AdminActionResult.Done);
        temporaryPassword.Should().NotBeNull().And.HaveLength(UserService.TemporaryPasswordLength);
        PasswordHashing.Verify(_bob.PasswordHash, temporaryPassword!).Should().Be(PasswordCheck.Valid);
        _bob.MustChangePassword.Should().BeTrue();
        _bob.SecurityStamp.Should().NotBe("b");
    }

    [Fact]
    public void TryChangePassword_ClearsThePendingPasswordChange()
    {
        _bob.PasswordHash = PasswordHashing.Hash("temporary");
        _bob.MustChangePassword = true;

        CreateSut().TryChangePassword(_bob.Id, "temporary", "chosen-password").Should().BeTrue();

        _bob.MustChangePassword.Should().BeFalse();
    }

    [Fact]
    public void DeleteUserAsAdmin_DeletesAnAdminWhileAnotherRemains()
    {
        OtherActiveAdmins(true);

        CreateSut().DeleteUserAsAdmin(_admin.Id, _carol.Id).Should().Be(AdminActionResult.Done);

        _users.Verify(r => r.Delete(_carol.Id), Times.Once);
    }

    [Fact]
    public void DeleteUserAsAdmin_RefusesToDeleteTheLastActiveAdmin()
    {
        OtherActiveAdmins(false);

        CreateSut().DeleteUserAsAdmin(_admin.Id, _carol.Id).Should().Be(AdminActionResult.LastAdministrator);

        _users.Verify(r => r.Delete(It.IsAny<UserId>()), Times.Never);
    }

    [Fact]
    public void TryDeleteUser_AdminMayLeaveWhileAnotherAdminRemains()
    {
        OtherActiveAdmins(true);

        CreateSut().TryDeleteUser(_admin.Id).Should().BeTrue();

        _users.Verify(r => r.Delete(_admin.Id), Times.Once);
    }

    [Fact]
    public void TryDeleteUser_LastAdminCannotLeave()
    {
        OtherActiveAdmins(false);

        CreateSut().TryDeleteUser(_admin.Id).Should().BeFalse();

        _users.Verify(r => r.Delete(It.IsAny<UserId>()), Times.Never);
    }

    [Fact]
    public void TryCreateUser_CanRequireAPasswordChange()
    {
        User? inserted = null;
        _users.Setup(r => r.TryInsert(It.IsAny<User>())).Callback<User>(u => inserted = u).Returns(true);

        CreateSut().TryCreateUser("dave", "password", mustChangePassword: true).Should().BeTrue();

        inserted!.MustChangePassword.Should().BeTrue();
        inserted.IsSuperUser.Should().BeFalse();
    }

    [Fact]
    public void SetRenderingAllowed_RevokesAndRestores()
    {
        var sut = CreateSut();

        sut.SetRenderingAllowed(_admin.Id, _bob.Id, false).Should().Be(AdminActionResult.Done);
        _bob.RenderingAllowed.Should().BeFalse();

        sut.SetRenderingAllowed(_admin.Id, _bob.Id, true).Should().Be(AdminActionResult.Done);
        _bob.RenderingAllowed.Should().BeNull();
        _users.Verify(r => r.Update(_bob), Times.Exactly(2));
    }

    [Fact]
    public void TryCreateUser_RejectsAUsernameThatDiffersOnlyInCase()
    {
        _users.Setup(r => r.ExistsByUsernameIgnoreCase("Alice")).Returns(true);

        CreateSut().TryCreateUser("Alice", "password").Should().BeFalse();

        _users.Verify(r => r.TryInsert(It.IsAny<User>()), Times.Never);
    }
}

using System.Security.Claims;
using EPaperDashboard.Authentication;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using EPaperDashboard.Utilities;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Authentication;

public class UserSessionValidatorTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly FakeTimeProvider _time = new();
    private readonly User _alice = new() { Id = UserId.New(), Username = "alice", SecurityStamp = "stamp-1" };

    private UserSessionValidator CreateSut() => new(_users.Object, _time);

    [Fact]
    public void Validate_UnchangedAccount_IsValid()
    {
        _users.Setup(r => r.FindById(_alice.Id)).Returns(_alice);

        CreateSut().Validate(UserClaims.Create(_alice)).State.Should().Be(SessionState.Valid);
    }

    [Fact]
    public void Validate_DeletedAccount_IsRejected()
    {
        var principal = UserClaims.Create(_alice);
        _users.Setup(r => r.FindById(_alice.Id)).Returns((User?)null);

        CreateSut().Validate(principal).State.Should().Be(SessionState.Reject);
    }

    [Fact]
    public void Validate_RotatedStamp_IsRejected()
    {
        var principal = UserClaims.Create(_alice);
        _users.Setup(r => r.FindById(_alice.Id)).Returns(new User { Id = _alice.Id, Username = "alice", SecurityStamp = "stamp-2" });

        CreateSut().Validate(principal).State.Should().Be(SessionState.Reject);
    }

    [Fact]
    public void Validate_AccountWithoutStamp_AcceptsSessionsWithoutStampClaim()
    {
        var legacy = new User { Id = UserId.New(), Username = "old" };
        _users.Setup(r => r.FindById(legacy.Id)).Returns(legacy);

        CreateSut().Validate(UserClaims.Create(legacy)).State.Should().Be(SessionState.Valid);
    }

    [Fact]
    public void Validate_StampedAccount_RejectsSessionsIssuedBeforeTheStamp()
    {
        var principal = UserClaims.Create(new User { Id = _alice.Id, Username = "alice" });
        _users.Setup(r => r.FindById(_alice.Id)).Returns(_alice);

        CreateSut().Validate(principal).State.Should().Be(SessionState.Reject);
    }

    [Fact]
    public void Validate_ChangedRole_RefreshesTheClaims()
    {
        var principal = UserClaims.Create(_alice);
        _users.Setup(r => r.FindById(_alice.Id)).Returns(new User { Id = _alice.Id, Username = "alice", SecurityStamp = "stamp-1", IsSuperUser = true });

        var check = CreateSut().Validate(principal);

        check.State.Should().Be(SessionState.Refresh);
        check.Principal!.FindFirst(Constants.IsSuperUserClaim)!.Value.Should().Be("true");
    }

    [Fact]
    public void Validate_MissingUserId_IsRejected() =>
        CreateSut().Validate(new ClaimsPrincipal(new ClaimsIdentity("Cookies"))).State.Should().Be(SessionState.Reject);

    [Fact]
    public void Validate_ReusesTheAccountUntilTheCacheExpires()
    {
        _users.Setup(r => r.FindById(_alice.Id)).Returns(_alice);
        var sut = CreateSut();
        var principal = UserClaims.Create(_alice);

        sut.Validate(principal);
        sut.Validate(principal);
        _users.Verify(r => r.FindById(_alice.Id), Times.Once);

        _time.Advance(UserSessionValidator.CacheDuration + TimeSpan.FromSeconds(1));
        sut.Validate(principal);
        _users.Verify(r => r.FindById(_alice.Id), Times.Exactly(2));
    }

    [Fact]
    public void Invalidate_ReloadsTheAccountOnTheNextCheck()
    {
        _users.Setup(r => r.FindById(_alice.Id)).Returns(_alice);
        var sut = CreateSut();
        var principal = UserClaims.Create(_alice);
        sut.Validate(principal);

        _users.Setup(r => r.FindById(_alice.Id)).Returns((User?)null);
        sut.Invalidate(_alice.Id);

        sut.Validate(principal).State.Should().Be(SessionState.Reject);
    }

    [Fact]
    public void Validate_SuspendedAccount_IsRejected()
    {
        var principal = UserClaims.Create(_alice);
        _users.Setup(r => r.FindById(_alice.Id)).Returns(new User { Id = _alice.Id, Username = "alice", SecurityStamp = "stamp-1", IsSuspended = true });

        CreateSut().Validate(principal).State.Should().Be(SessionState.Reject);
    }

    [Fact]
    public void Validate_PendingPasswordChange_RefreshesTheClaims()
    {
        var principal = UserClaims.Create(_alice);
        _users.Setup(r => r.FindById(_alice.Id)).Returns(new User { Id = _alice.Id, Username = "alice", SecurityStamp = "stamp-1", MustChangePassword = true });

        var check = CreateSut().Validate(principal);

        check.State.Should().Be(SessionState.Refresh);
        check.Principal!.FindFirst(Constants.MustChangePasswordClaim)!.Value.Should().Be("true");
    }
}

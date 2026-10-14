using CSharpFunctionalExtensions;
using EPaperDashboard.Data;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using EPaperDashboard.Services;
using EPaperDashboard.UnitTests.TestSupport;
using FluentAssertions;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Data;

public class UserAdminCommandTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly User _alice = new() { Id = UserId.New(), Username = "alice", IsSuspended = true, SecurityStamp = "s" };
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public UserAdminCommandTests()
    {
        _users.Setup(r => r.FindByUsername("alice")).Returns(_alice);
        _users.Setup(r => r.FindByUsername("ghost")).Returns(Maybe<User>.None);
    }

    private int Run(params string[] args) => UserAdminCommand.Execute(args,
        new UserService(_users.Object, new Mock<IDashboardRepository>().Object, unitOfWork: ImmediateUnitOfWork.Instance),
        _output, _error);

    [Fact]
    public void Promote_MakesAnActiveAdministrator()
    {
        Run("--users", "promote", "alice").Should().Be(0);

        _alice.IsSuperUser.Should().BeTrue();
        _alice.IsSuspended.Should().BeFalse();
        _users.Verify(r => r.Update(_alice), Times.Once);
    }

    [Fact]
    public void ResetPassword_PrintsATemporaryPasswordThatMustBeChanged()
    {
        Run("--users", "reset-password", "alice").Should().Be(0);

        var temporary = _output.ToString().Trim().Split(' ').Last();
        PasswordHashing.Verify(_alice.PasswordHash, temporary).Should().Be(PasswordCheck.Valid);
        _alice.MustChangePassword.Should().BeTrue();
        _alice.SecurityStamp.Should().NotBe("s");
    }

    [Fact]
    public void UnknownAccount_Fails()
    {
        Run("--users", "promote", "ghost").Should().Be(1);

        _error.ToString().Should().Contain("ghost");
    }

    [Theory]
    [InlineData("--users")]
    [InlineData("--users", "promote")]
    [InlineData("--users", "delete", "alice")]
    [InlineData("--users", "promote", "alice", "extra")]
    public void InvalidArguments_PrintUsage(params string[] args)
    {
        Run(args).Should().Be(1);

        _error.ToString().Should().Contain("--users promote <username>");
        _users.Verify(r => r.Update(It.IsAny<User>()), Times.Never);
    }
}

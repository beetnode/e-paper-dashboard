using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services;

public class SetupCodeProviderTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IDeploymentStrategy> _deployment = new();

    public SetupCodeProviderTests()
    {
        _deployment.SetupGet(d => d.IsUserManagementEnabled).Returns(true);
        _users.Setup(r => r.GetOrCreateSetupCode(It.IsAny<string>())).Returns((string candidate) => candidate);
    }

    private SetupCodeProvider CreateSut() =>
        new(_users.Object, new UserService(_users.Object, new Mock<IDashboardRepository>().Object), _deployment.Object);

    [Fact]
    public void PendingSetup_HasAReadableCode()
    {
        var code = CreateSut().GetPendingCode();

        code.Should().MatchRegex("^[A-Z2-9]{4}-[A-Z2-9]{4}$");
    }

    [Fact]
    public void PendingSetup_SharesTheStoredCode()
    {
        _users.Setup(r => r.GetOrCreateSetupCode(It.IsAny<string>())).Returns("ABCD2345");

        CreateSut().GetPendingCode().Should().Be("ABCD-2345");
    }

    [Fact]
    public void CompletedSetup_HasNoCode()
    {
        _users.Setup(r => r.IsSetupComplete()).Returns(true);
        var sut = CreateSut();

        sut.GetPendingCode().Should().BeNull();
        sut.Matches("ABCD2345").Should().BeFalse();
        _users.Verify(r => r.GetOrCreateSetupCode(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void AddonDeployment_HasNoCode()
    {
        _deployment.SetupGet(d => d.IsUserManagementEnabled).Returns(false);

        CreateSut().GetPendingCode().Should().BeNull();
    }

    [Theory]
    [InlineData("ABCD2345", true)]
    [InlineData("abcd-2345", true)]
    [InlineData(" ABCD 2345 ", true)]
    [InlineData("ABCD2346", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Matches_IgnoresCaseAndSeparators(string? entered, bool expected)
    {
        _users.Setup(r => r.GetOrCreateSetupCode(It.IsAny<string>())).Returns("ABCD2345");

        CreateSut().Matches(entered).Should().Be(expected);
    }
}

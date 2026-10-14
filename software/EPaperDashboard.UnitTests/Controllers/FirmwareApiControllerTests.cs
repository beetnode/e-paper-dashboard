using EPaperDashboard.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace EPaperDashboard.UnitTests.Controllers;

public class FirmwareApiControllerTests
{
    [Fact]
    public void RefreshFirmwareCheck_RequiresSuperUser() =>
        Policies(nameof(FirmwareApiController.RefreshFirmwareCheck)).Should().Contain("SuperUserOnly");

    [Fact]
    public void GetLatestFirmware_IsAvailableToAnySignedInUser() =>
        Policies(nameof(FirmwareApiController.GetLatestFirmware)).Should().ContainSingle().Which.Should().BeNull();

    private static IEnumerable<string?> Policies(string methodName) =>
        typeof(FirmwareApiController).GetMethod(methodName)!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()
            .Select(attribute => attribute.Policy);
}

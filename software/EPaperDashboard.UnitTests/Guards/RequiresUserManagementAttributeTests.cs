using EPaperDashboard.Guards;
using EPaperDashboard.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Guards;

public class RequiresUserManagementAttributeTests
{
    private static AuthorizationFilterContext Run(bool userManagementEnabled)
    {
        var deployment = new Mock<IDeploymentStrategy>();
        deployment.SetupGet(d => d.IsUserManagementEnabled).Returns(userManagementEnabled);
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(deployment.Object).BuildServiceProvider()
        };
        var context = new AuthorizationFilterContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());
        new RequiresUserManagementAttribute().OnAuthorization(context);
        return context;
    }

    [Fact]
    public void UserManagementEnabled_AllowsTheRequest() =>
        Run(userManagementEnabled: true).Result.Should().BeNull();

    [Fact]
    public void UserManagementDisabled_ReturnsForbidden() =>
        Run(userManagementEnabled: false).Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
}

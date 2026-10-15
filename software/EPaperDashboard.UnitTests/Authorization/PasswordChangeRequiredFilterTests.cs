using System.Security.Claims;
using EPaperDashboard.Authorization;
using EPaperDashboard.Utilities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace EPaperDashboard.UnitTests.Authorization;

public class PasswordChangeRequiredFilterTests
{
    private static AuthorizationFilterContext Context(bool mustChange, params object[] endpointMetadata)
    {
        var claims = mustChange ? new[] { new Claim(Constants.MustChangePasswordClaim, "true") } : [];
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) };
        var actionDescriptor = new ActionDescriptor { EndpointMetadata = endpointMetadata.ToList() };
        return new AuthorizationFilterContext(new ActionContext(httpContext, new RouteData(), actionDescriptor), []);
    }

    [Fact]
    public void PendingPasswordChange_BlocksOtherEndpoints()
    {
        var context = Context(mustChange: true);

        new PasswordChangeRequiredFilter().OnAuthorization(context);

        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        result.Value!.GetType().GetProperty("code")!.GetValue(result.Value).Should().Be("password_change_required");
    }

    [Fact]
    public void PendingPasswordChange_AllowsMarkedEndpoints()
    {
        var context = Context(mustChange: true, new AllowedDuringPasswordChangeAttribute());

        new PasswordChangeRequiredFilter().OnAuthorization(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public void NoPendingPasswordChange_AllowsEverything()
    {
        var context = Context(mustChange: false);

        new PasswordChangeRequiredFilter().OnAuthorization(context);

        context.Result.Should().BeNull();
    }
}

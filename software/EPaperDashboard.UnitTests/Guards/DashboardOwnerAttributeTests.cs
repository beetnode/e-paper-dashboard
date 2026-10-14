using EPaperDashboard.Guards;
using EPaperDashboard.Models;
using EPaperDashboard.UnitTests.TestSupport;
using EPaperDashboard.Utilities;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Guards;

public class DashboardOwnerAttributeTests
{
    private static readonly DashboardOwnerAttribute Sut = new();

    [Fact]
    public void OnAuthorization_MissingRouteDashboardId_SetsBadRequest()
    {
        var context = AuthorizationFilterContextBuilder.Build(userIdClaim: "someid", routeDashboardId: null);

        Sut.OnAuthorization(context);

        context.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void OnAuthorization_NoUserClaim_SetsUnauthorized()
    {
        var context = AuthorizationFilterContextBuilder.Build(userIdClaim: null, routeDashboardId: DashboardId.New().Value);

        Sut.OnAuthorization(context);

        context.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public void OnAuthorization_UnparsableUserIdClaim_SetsUnauthorized()
    {
        var context = AuthorizationFilterContextBuilder.Build(userIdClaim: "not-a-valid-id", routeDashboardId: DashboardId.New().Value);

        Sut.OnAuthorization(context);

        context.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public void OnAuthorization_InvalidDashboardIdFormat_SetsBadRequest()
    {
        var userId = UserId.New();
        var context = AuthorizationFilterContextBuilder.Build(
            userIdClaim: userId.Value,
            routeDashboardId: "not-a-valid-dashboard-id",
            configure: (users, _) => users.Setup(r => r.FindById(userId)).Returns(new User { Id = userId }));

        Sut.OnAuthorization(context);

        context.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void OnAuthorization_DashboardNotFound_SetsNotFound()
    {
        var userId = UserId.New();
        var dashboardId = DashboardId.New();
        var context = AuthorizationFilterContextBuilder.Build(
            userIdClaim: userId.Value,
            routeDashboardId: dashboardId.Value,
            configure: (users, dashboards) =>
            {
                users.Setup(r => r.FindById(userId)).Returns(new User { Id = userId });
                dashboards.Setup(r => r.FindById(dashboardId)).Returns(CSharpFunctionalExtensions.Maybe<Dashboard>.None);
            });

        Sut.OnAuthorization(context);

        context.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public void OnAuthorization_DashboardOwnedByDifferentUser_SetsForbid()
    {
        var userId = UserId.New();
        var dashboardId = DashboardId.New();
        var dashboard = new Dashboard { Id = dashboardId, UserId = UserId.New() };
        var context = AuthorizationFilterContextBuilder.Build(
            userIdClaim: userId.Value,
            routeDashboardId: dashboardId.Value,
            configure: (users, dashboards) =>
            {
                users.Setup(r => r.FindById(userId)).Returns(new User { Id = userId });
                dashboards.Setup(r => r.FindById(dashboardId)).Returns(dashboard);
            });

        Sut.OnAuthorization(context);

        context.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public void OnAuthorization_DashboardOwnedByCurrentUser_LeavesResultNullToContinue()
    {
        var userId = UserId.New();
        var dashboardId = DashboardId.New();
        var dashboard = new Dashboard { Id = dashboardId, UserId = userId };
        var context = AuthorizationFilterContextBuilder.Build(
            userIdClaim: userId.Value,
            routeDashboardId: dashboardId.Value,
            configure: (users, dashboards) =>
            {
                users.Setup(r => r.FindById(userId)).Returns(new User { Id = userId });
                dashboards.Setup(r => r.FindById(dashboardId)).Returns(dashboard);
            });

        Sut.OnAuthorization(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public void OnAuthorization_HomeAssistantIngressWithVirtualUser_SkipsUserExistenceCheck()
    {
        var dashboardId = DashboardId.New();
        var dashboard = new Dashboard { Id = dashboardId, UserId = Constants.HomeAssistantVirtualUserId };
        var context = AuthorizationFilterContextBuilder.Build(
            userIdClaim: Constants.HomeAssistantVirtualUserId.ToString(),
            isHomeAssistantIngress: true,
            routeDashboardId: dashboardId.Value,
            configure: (_, dashboards) => dashboards.Setup(r => r.FindById(dashboardId)).Returns(dashboard));

        Sut.OnAuthorization(context);

        context.Result.Should().BeNull();
    }
}

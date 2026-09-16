using System.Text.Json;
using EPaperDashboard.Data;
using EPaperDashboard.Data.PostgreSql;
using EPaperDashboard.Models;
using FluentAssertions;
using Xunit;

namespace EPaperDashboard.UnitTests.Data;

/// <summary>
/// Set TEST_POSTGRES_CONNECTION_STRING to run against a disposable PostgreSQL database.
/// </summary>
public class PostgresRepositoryIntegrationTests
{
    [PostgresFact]
    public void Repositories_PersistAndQueryAllDocumentTypes()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING")!;

        using var context = new PostgresDbContext(new DatabaseSettings(DatabaseProvider.PostgreSql, connectionString));
        context.Initialize();
        context.Initialize();

        var users = new PostgresUserRepository(context);
        var dashboards = new PostgresDashboardRepository(context);
        var devices = new PostgresDeviceRepository(context);
        var sessions = new PostgresPairingSessionRepository(context);
        var suffix = Guid.NewGuid().ToString("N");
        var user = new User { Username = $"user-{suffix}", PasswordHash = "hash", IsSuperUser = true };
        var dashboard = new Dashboard
        {
            UserId = UserId.Empty,
            Name = "Test",
            UpdateTimes = [new TimeOnly(8, 30)],
            LayoutConfig = new LayoutConfig
            {
                Widgets = [new WidgetConfig
                {
                    Id = "widget-1",
                    Type = "markdown",
                    Config = JsonDocument.Parse("{\"text\":\"hello\"}").RootElement.Clone()
                }]
            }
        };

        try
        {
            users.Insert(user);
            dashboard.UserId = user.Id;
            dashboards.Insert(dashboard);
            var device = new Device
            {
                UserId = user.Id,
                DashboardId = dashboard.Id,
                DeviceIdentifier = $"device-{suffix}",
                ApiKey = $"key-{suffix}"
            };
            devices.Insert(device);
            var session = new PairingSession
            {
                Code = suffix[..6].ToUpperInvariant(),
                UserId = user.Id,
                PendingDeviceId = device.Id,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
            };
            sessions.Insert(session);

            users.FindByUsername(user.Username).Value.Id.Should().Be(user.Id);
            users.ExistsSuperUser().Should().BeTrue();
            dashboards.FindByUserId(user.Id).Should().ContainSingle();
            dashboards.FindById(dashboard.Id).Value.LayoutConfig!.Widgets[0].Config
                .GetProperty("text").GetString().Should().Be("hello");
            dashboards.FindById(dashboard.Id).Value.UpdateTimes.Should().ContainSingle()
                .Which.Should().Be(new TimeOnly(8, 30));
            devices.FindByIdentifier(device.DeviceIdentifier).Value.Id.Should().Be(device.Id);
            devices.FindByApiKey(device.ApiKey).Value.Id.Should().Be(device.Id);
            devices.FindByDashboardId(dashboard.Id).Should().ContainSingle();
            sessions.FindByCode(session.Code).Value.PendingDeviceId.Should().Be(device.Id);
            sessions.HasActiveSessions(DateTimeOffset.UtcNow).Should().BeTrue();

            device.Name = "updated";
            devices.Update(device);
            devices.FindById(device.Id).Value.Name.Should().Be("updated");
            session.IsCompleted = true;
            sessions.Update(session);
            sessions.FindById(session.Id).Value.IsCompleted.Should().BeTrue();
            sessions.DeleteExpired(DateTimeOffset.UtcNow.AddHours(1));
            sessions.FindById(session.Id).HasValue.Should().BeFalse();
        }
        finally
        {
            dashboards.DeleteByUserId(user.Id);
            foreach (var device in devices.FindByUserId(user.Id)) devices.Delete(device.Id);
            users.Delete(user.Id);
        }
    }

    [PostgresFact]
    public void UnitOfWork_RollsBackWritesAcrossRepositories()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING")!;

        using var context = new PostgresDbContext(new DatabaseSettings(DatabaseProvider.PostgreSql, connectionString));
        context.Initialize();
        var users = new PostgresUserRepository(context);
        var dashboards = new PostgresDashboardRepository(context);
        var unitOfWork = new PostgresUnitOfWork(context);
        var user = new User { Username = $"rollback-{Guid.NewGuid():N}" };
        var dashboard = new Dashboard { Name = "rollback" };

        Action operation = () => unitOfWork.Execute<bool>(() =>
        {
            users.Insert(user);
            dashboard.UserId = user.Id;
            dashboards.Insert(dashboard);
            throw new InvalidOperationException("rollback");
        });

        operation.Should().Throw<InvalidOperationException>();
        users.FindById(user.Id).HasValue.Should().BeFalse();
        dashboards.FindById(dashboard.Id).HasValue.Should().BeFalse();
    }

    [PostgresFact]
    public void UnitOfWork_CommitsWritesAcrossRepositories()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING")!;
        using var context = new PostgresDbContext(new DatabaseSettings(DatabaseProvider.PostgreSql, connectionString));
        context.Initialize();
        var users = new PostgresUserRepository(context);
        var dashboards = new PostgresDashboardRepository(context);
        var unitOfWork = new PostgresUnitOfWork(context);
        var user = new User { Username = $"commit-{Guid.NewGuid():N}" };
        var dashboard = new Dashboard { Name = "commit" };

        try
        {
            unitOfWork.Execute(() =>
            {
                users.Insert(user);
                dashboard.UserId = user.Id;
                dashboards.Insert(dashboard);
                return true;
            });

            users.FindById(user.Id).HasValue.Should().BeTrue();
            dashboards.FindById(dashboard.Id).HasValue.Should().BeTrue();
        }
        finally
        {
            dashboards.Delete(dashboard.Id);
            users.Delete(user.Id);
        }
    }
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING")))
            Skip = "Set TEST_POSTGRES_CONNECTION_STRING to run PostgreSQL integration tests";
    }
}

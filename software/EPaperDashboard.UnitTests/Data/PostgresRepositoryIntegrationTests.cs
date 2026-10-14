using System.Text.Json;
using EPaperDashboard.Data;
using EPaperDashboard.Data.PostgreSql;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
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

        using var services = CreateMigratedServices(connectionString);
        services.GetRequiredService<IPersistenceInitializer>().Initialize();
        var context = services.GetRequiredService<PostgresDbContext>();

        var users = new PostgresUserRepository(context);
        var dashboards = new PostgresDashboardRepository(context);
        var devices = new PostgresDeviceRepository(context);
        var sessions = new PostgresPairingSessionRepository(context);
        var connections = new PostgresProviderConnectionRepository(context);
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
            var provider = new ProviderConnection { UserId = user.Id, PluginId = "google", Name = "Personal", ProtectedCredentials = "ciphertext" };
            connections.Insert(provider);
            connections.FindByUserId(user.Id).Should().ContainSingle();
            connections.Find(provider.Id, UserId.New()).Should().BeNull();
            provider.ProtectedCredentials = "updated-ciphertext";
            connections.Update(provider, "ciphertext").Should().BeTrue();
            connections.Update(provider, "ciphertext").Should().BeFalse();
            connections.Find(provider.Id, user.Id)!.ProtectedCredentials.Should().Be("updated-ciphertext");
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
            var suspendedAdmin = new User { Username = $"suspended-{suffix}", IsSuperUser = true, IsSuspended = true };
            users.Insert(suspendedAdmin);
            users.ExistsOtherActiveSuperUser(suspendedAdmin.Id).Should().BeTrue();
            var otherAdmins = users.GetAll().Where(u => u.IsSuperUser && !u.IsSuspended && u.Id != user.Id).ToList();
            users.ExistsOtherActiveSuperUser(user.Id).Should().Be(otherAdmins.Count > 0);
            users.Delete(suspendedAdmin.Id);
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

            var otherUser = UserId.New();
            var otherDevice = new Device { UserId = otherUser, DashboardId = dashboard.Id, DeviceIdentifier = $"other-{suffix}", ApiKey = $"other-key-{suffix}" };
            devices.Insert(otherDevice);
            var claimed = new PairingSession { Code = suffix[6..12].ToUpperInvariant(), UserId = user.Id, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) };
            var otherSession = new PairingSession { Code = suffix[12..18].ToUpperInvariant(), UserId = otherUser, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) };
            sessions.Insert(claimed);
            sessions.Insert(otherSession);
            devices.DeleteByUserId(user.Id);
            sessions.DeleteByUserId(user.Id);
            devices.FindByUserId(user.Id).Should().BeEmpty();
            devices.FindById(otherDevice.Id).HasValue.Should().BeTrue();
            sessions.FindById(claimed.Id).HasValue.Should().BeFalse();
            sessions.FindById(otherSession.Id).HasValue.Should().BeTrue();
            devices.Delete(otherDevice.Id);
            sessions.DeleteExpired(DateTimeOffset.UtcNow.AddHours(1));
        }
        finally
        {
            dashboards.DeleteByUserId(user.Id);
            connections.DeleteByUserId(user.Id);
            foreach (var device in devices.FindByUserId(user.Id)) devices.Delete(device.Id);
            users.Delete(user.Id);
        }
    }

    [PostgresFact]
    public void UnitOfWork_RollsBackWritesAcrossRepositories()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING")!;

        using var services = CreateMigratedServices(connectionString);
        var context = services.GetRequiredService<PostgresDbContext>();
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
        using var services = CreateMigratedServices(connectionString);
        var context = services.GetRequiredService<PostgresDbContext>();
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

    [PostgresFact]
    public void Migrations_BaselineExistingSchemaWithoutLosingData()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING")!;
        var schema = $"migration_{Guid.NewGuid():N}";
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using var createSchema = new NpgsqlCommand($"CREATE SCHEMA {schema}", connection);
        createSchema.ExecuteNonQuery();

        try
        {
            var schemaConnectionString = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema }.ConnectionString;
            var user = new User { Username = "preserved-user", PasswordHash = "preserved-hash" };
            using (var previousServices = CreateMigratedServices(schemaConnectionString))
                previousServices.GetRequiredService<IUserRepository>().Insert(user);

            // The previous initializer created the same tables without migration history.
            using var removeHistory = new NpgsqlCommand($"DROP TABLE {schema}.izboard_schema_versions", connection);
            removeHistory.ExecuteNonQuery();

            Migrate(schemaConnectionString);
            Migrate(schemaConnectionString);

            using var services = CreateServices(schemaConnectionString);
            services.GetRequiredService<IPersistenceInitializer>().Initialize();
            var users = services.GetRequiredService<IUserRepository>();
            users.FindById(user.Id).Value.PasswordHash.Should().Be("preserved-hash");
            using var versions = new NpgsqlCommand($"SELECT COUNT(*) FROM {schema}.izboard_schema_versions WHERE version = 1", connection);
            versions.ExecuteScalar().Should().Be(1L);
        }
        finally
        {
            using var dropSchema = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", connection);
            dropSchema.ExecuteNonQuery();
        }
    }

    private static ServiceProvider CreateMigratedServices(string connectionString)
    {
        Migrate(connectionString);
        var services = CreateServices(connectionString);
        try
        {
            services.GetRequiredService<IPersistenceInitializer>().Initialize();
            return services;
        }
        catch
        {
            services.Dispose();
            throw;
        }
    }

    private static ServiceProvider CreateServices(string connectionString) => new ServiceCollection()
        .AddLogging()
        .AddPersistence(new DatabaseSettings(DatabaseProvider.PostgreSql, connectionString))
        .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

    private static void Migrate(string connectionString)
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddDatabaseMaintenance(new DatabaseSettings(DatabaseProvider.PostgreSql, connectionString))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        services.GetRequiredService<IDatabaseMaintenance>().Migrate();
    }

    [PostgresFact]
    public void Startup_RequiresMatchingSchemaWithoutApplyingMigrations()
    {
        WithEmptySchema(connectionString =>
        {
            using (var services = CreateServices(connectionString))
            {
                Action start = () => services.GetRequiredService<IPersistenceInitializer>().Initialize();
                start.Should().Throw<InvalidOperationException>().WithMessage("*--maintenance*");
                using var connection = new NpgsqlConnection(connectionString);
                connection.Open();
                using var tables = new NpgsqlCommand("SELECT to_regclass('izboard_users') IS NULL", connection);
                tables.ExecuteScalar().Should().Be(true);
            }

            Migrate(connectionString);
            using (var connection = new NpgsqlConnection(connectionString))
            {
                connection.Open();
                using var futureVersion = new NpgsqlCommand("UPDATE izboard_schema_versions SET version = version + 100", connection);
                futureVersion.ExecuteNonQuery();
            }

            using var oldApp = CreateServices(connectionString);
            Action startOldApp = () => oldApp.GetRequiredService<IPersistenceInitializer>().Initialize();
            startOldApp.Should().Throw<InvalidOperationException>().WithMessage("*matching app release*");
            Action migrateOldApp = () => Migrate(connectionString);
            migrateOldApp.Should().Throw<InvalidOperationException>().WithMessage("*matching app release*");
            using var check = new NpgsqlConnection(connectionString);
            check.Open();
            using var unchanged = new NpgsqlCommand("SELECT version FROM izboard_schema_versions", check);
            unchanged.ExecuteScalar().Should().BeOneOf(101L, 102L, 103L);
        });
    }

    [PostgresFact]
    public void Maintenance_AndRunningServiceExcludeEachOther()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING")!;
        using (var services = CreateMigratedServices(connectionString))
        {
            Action migrate = () => Migrate(connectionString);
            migrate.Should().Throw<InvalidOperationException>().WithMessage("*Stop all app instances*");
        }

        using var maintenance = PostgresDatabaseLease.Acquire(
            new DatabaseSettings(DatabaseProvider.PostgreSql, connectionString), maintenance: true);
        using var app = CreateServices(connectionString);
        Action start = () => app.GetRequiredService<IPersistenceInitializer>().Initialize();
        start.Should().Throw<InvalidOperationException>().WithMessage("*maintenance is in progress*");
    }

    [PostgresFact]
    public void InitialSchema_PreservesPartialUniqueIndexesAndTimestampType()
    {
        WithEmptySchema(connectionString =>
        {
            using var services = CreateMigratedServices(connectionString);
            var devices = services.GetRequiredService<IDeviceRepository>();
            devices.Insert(new Device());
            devices.Insert(new Device());
            devices.Insert(new Device { ApiKey = "unique-key", DeviceIdentifier = "unique-device" });
            Action duplicateKey = () => devices.Insert(new Device { ApiKey = "unique-key" });
            duplicateKey.Should().Throw<PostgresException>().Which.SqlState.Should().Be("23505");
            Action duplicateIdentifier = () => devices.Insert(new Device { DeviceIdentifier = "unique-device" });
            duplicateIdentifier.Should().Throw<PostgresException>().Which.SqlState.Should().Be("23505");

            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();
            using var type = new NpgsqlCommand("""
                SELECT data_type FROM information_schema.columns
                WHERE table_schema = current_schema() AND table_name = 'izboard_pairing_sessions' AND column_name = 'expires_at'
                """, connection);
            type.ExecuteScalar().Should().Be("timestamp with time zone");
        });
    }

    [PostgresFact]
    public void Setup_IsAtomicAcrossConnectionsAndStaysClosedAfterDeletion()
    {
        WithEmptySchema(connectionString =>
        {
            using var services = CreateMigratedServices(connectionString);
            var settings = new DatabaseSettings(DatabaseProvider.PostgreSql, connectionString);
            var results = new bool[12];
            Parallel.For(0, results.Length, i =>
            {
                using var context = new PostgresDbContext(settings);
                results[i] = new PostgresUserRepository(context).TryCreateAdministrator(new User { Username = "admin-" + i });
            });
            results.Count(result => result).Should().Be(1);
            var users = services.GetRequiredService<IUserRepository>();
            var admin = users.GetAll().Should().ContainSingle().Subject;
            admin.IsSuperUser.Should().BeTrue();
            users.SetRenderingEnabled(admin.Id, false);
            users.GetRenderingEnabled(admin.Id).Should().BeFalse();
            users.GetAiConfig(admin.Id).Should().BeNull();
            users.SetAiConfig(admin.Id, new AiConfig { ConnectionMode = AiConnectionMode.HomeAssistant });
            users.SetRenderingEnabled(admin.Id, true);
            users.GetAiConfig(admin.Id)!.ConnectionMode.Should().Be(AiConnectionMode.HomeAssistant);
            users.GetRenderingEnabled(admin.Id).Should().BeTrue();
            users.Delete(admin.Id);
            users.IsSetupComplete().Should().BeTrue();
            users.TryCreateAdministrator(new User { Username = "attacker" }).Should().BeFalse();
            Parallel.For(0, results.Length, i =>
            {
                using var context = new PostgresDbContext(settings);
                results[i] = new PostgresUserRepository(context).TryInsert(new User { Username = "same-user" });
            });
            results.Count(result => result).Should().Be(1);
        });
    }

    [PostgresFact]
    public void ExecuteExclusive_LetsOneInstanceRunAtATime()
    {
        WithEmptySchema(connectionString =>
        {
            var settings = new DatabaseSettings(DatabaseProvider.PostgreSql, connectionString);
            var running = 0;
            var overlapped = false;
            Parallel.For(0, 4, _ =>
            {
                using var context = new PostgresDbContext(settings);
                new PostgresUnitOfWork(context).ExecuteExclusive("test", () =>
                {
                    if (Interlocked.Increment(ref running) > 1) overlapped = true;
                    Thread.Sleep(100);
                    Interlocked.Decrement(ref running);
                    return true;
                });
            });
            overlapped.Should().BeFalse();
        });
    }

    [PostgresFact]
    public void LegacySettingsMigration_InstancesStartingTogetherShareOneConnection()
    {
        WithEmptySchema(connectionString =>
        {
            using var services = CreateMigratedServices(connectionString);
            var settings = new DatabaseSettings(DatabaseProvider.PostgreSql, connectionString);
            var owner = UserId.New();
            var dashboards = Enumerable.Range(0, 6).Select(i => new Dashboard
            {
                UserId = owner, Name = "D" + i, Host = "http://ha.test:8123", AccessToken = "token",
                RenderingMode = RenderingMode.HomeAssistant
            }).ToList();
            var setup = services.GetRequiredService<IDashboardRepository>();
            dashboards.ForEach(setup.Insert);
            // Instances share data protection keys in production; one ephemeral provider stands in for them.
            var protection = new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider();
            var deployment = new Moq.Mock<EPaperDashboard.Services.IDeploymentStrategy>();

            Parallel.For(0, 3, _ =>
            {
                using var context = new PostgresDbContext(settings);
                var connections = new EPaperDashboard.Services.Plugins.ProviderConnectionStore(new PostgresProviderConnectionRepository(context), protection);
                new EPaperDashboard.Services.Plugins.HomeAssistant.LegacyHomeAssistantSettingsMigration(
                    new PostgresDashboardRepository(context), connections, new PostgresUnitOfWork(context), deployment.Object,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<EPaperDashboard.Services.Plugins.HomeAssistant.LegacyHomeAssistantSettingsMigration>.Instance).Run();
            });

            services.GetRequiredService<IProviderConnectionRepository>().FindByUserId(owner).Should().ContainSingle();
            var migrated = setup.FindByUserId(owner);
            migrated.Should().OnlyContain(d => d.AccessToken == null && d.Host == null);
            migrated.Select(d => d.HomeAssistantConnectionId).Distinct().Should().ContainSingle().Which.Should().NotBeNull();
        });
    }

    private static void WithEmptySchema(Action<string> test)
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING")!;
        var schema = $"migration_{Guid.NewGuid():N}";
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using var createSchema = new NpgsqlCommand($"CREATE SCHEMA {schema}", connection);
        createSchema.ExecuteNonQuery();
        try
        {
            test(new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema }.ConnectionString);
        }
        finally
        {
            using var dropSchema = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", connection);
            dropSchema.ExecuteNonQuery();
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

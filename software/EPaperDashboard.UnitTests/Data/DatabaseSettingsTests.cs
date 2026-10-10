using EPaperDashboard.Data;
using EPaperDashboard.Data.LiteDb;
using EPaperDashboard.Data.PostgreSql;
using EPaperDashboard.Data.Repositories;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FluentMigrator.Runner;

namespace EPaperDashboard.UnitTests.Data;

public class DatabaseSettingsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("litedb")]
    [InlineData(" LiteDB ")]
    public void Parse_DefaultsToLiteDb(string? provider)
    {
        var settings = DatabaseSettings.Parse(provider, null);

        settings.Provider.Should().Be(DatabaseProvider.LiteDb);
        settings.ConnectionString.Should().BeNull();
    }

    [Theory]
    [InlineData("postgresql")]
    [InlineData("POSTGRES")]
    public void Parse_AcceptsPostgreSql(string provider)
    {
        var settings = DatabaseSettings.Parse(provider, "Host=localhost;Database=izboard");

        settings.Provider.Should().Be(DatabaseProvider.PostgreSql);
        settings.ConnectionString.Should().Be("Host=localhost;Database=izboard");
    }

    [Fact]
    public void Parse_RejectsPostgreSqlWithoutConnectionString() =>
        FluentActions.Invoking(() => DatabaseSettings.Parse("postgresql", null))
            .Should().Throw<ArgumentException>().WithMessage("*DATABASE_CONNECTION_STRING*");

    [Fact]
    public void Parse_RejectsUnknownProvider() =>
        FluentActions.Invoking(() => DatabaseSettings.Parse("other", null))
            .Should().Throw<ArgumentException>().WithMessage("*DATABASE_PROVIDER*");

    [Theory]
    [InlineData(DatabaseProvider.LiteDb, typeof(LiteDbUserRepository), typeof(LiteDbContext))]
    [InlineData(DatabaseProvider.PostgreSql, typeof(PostgresUserRepository), typeof(PostgresDbContext))]
    public void AddPersistence_RegistersSelectedBackend(
        DatabaseProvider provider, Type repositoryType, Type contextType)
    {
        var services = new ServiceCollection();
        services.AddPersistence(new DatabaseSettings(provider, "Host=localhost;Database=izboard"));

        services.Single(service => service.ServiceType == typeof(IUserRepository))
            .ImplementationType.Should().Be(repositoryType);
        services.Single(service => service.ServiceType == contextType).Lifetime
            .Should().Be(ServiceLifetime.Singleton);
        services.Single(service => service.ServiceType == typeof(IUnitOfWork))
            .Should().NotBeNull();
        services.Should().NotContain(service => service.ServiceType == typeof(IMigrationRunner));
        services.Should().NotContain(service => service.ServiceType == typeof(IDatabaseMaintenance));
    }
}

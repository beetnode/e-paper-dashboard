using CSharpFunctionalExtensions;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;

namespace EPaperDashboard.Data.PostgreSql;

internal sealed class PostgresDeviceRepository(PostgresDbContext context) : IDeviceRepository
{
    public Maybe<Device> FindById(DeviceId id) => Maybe.From(
        context.FindOne<Device>("SELECT document FROM izboard_devices WHERE id = @id",
            PostgresDbContext.Parameter("id", id.Value)));

    public Maybe<Device> FindByIdentifier(string deviceIdentifier) => Maybe.From(
        context.FindOne<Device>("SELECT document FROM izboard_devices WHERE device_identifier = @identifier",
            PostgresDbContext.Parameter("identifier", deviceIdentifier)));

    public Maybe<Device> FindByApiKey(string apiKey) => Maybe.From(
        context.FindOne<Device>("SELECT document FROM izboard_devices WHERE api_key = @api_key",
            PostgresDbContext.Parameter("api_key", apiKey)));

    public List<Device> FindByDashboardId(DashboardId dashboardId) => context.FindMany<Device>(
        "SELECT document FROM izboard_devices WHERE dashboard_id = @dashboard_id",
        PostgresDbContext.Parameter("dashboard_id", dashboardId.Value));

    public List<Device> FindByUserId(UserId userId) => context.FindMany<Device>(
        "SELECT document FROM izboard_devices WHERE user_id = @user_id",
        PostgresDbContext.Parameter("user_id", userId.Value));

    public void Insert(Device device)
    {
        if (device.Id == DeviceId.Empty)
            device.Id = DeviceId.New();

        context.Execute("""
            INSERT INTO izboard_devices (id, user_id, dashboard_id, device_identifier, api_key, document)
            VALUES (@id, @user_id, @dashboard_id, @identifier, @api_key, @document)
            """, Parameters(device));
    }

    public void Update(Device device) => context.Execute("""
        UPDATE izboard_devices SET user_id = @user_id, dashboard_id = @dashboard_id,
            device_identifier = @identifier, api_key = @api_key, document = @document
        WHERE id = @id
        """, Parameters(device));

    public void Delete(DeviceId id) => context.Execute(
        "DELETE FROM izboard_devices WHERE id = @id", PostgresDbContext.Parameter("id", id.Value));

    public void DeleteByUserId(UserId userId) => context.Execute(
        "DELETE FROM izboard_devices WHERE user_id = @user_id", PostgresDbContext.Parameter("user_id", userId.Value));

    private static Npgsql.NpgsqlParameter[] Parameters(Device device) =>
    [
        PostgresDbContext.Parameter("id", device.Id.Value),
        PostgresDbContext.Parameter("user_id", device.UserId.Value),
        PostgresDbContext.Parameter("dashboard_id", device.DashboardId.Value),
        PostgresDbContext.Parameter("identifier", device.DeviceIdentifier),
        PostgresDbContext.Parameter("api_key", device.ApiKey),
        PostgresDbContext.DocumentParameter(device)
    ];
}

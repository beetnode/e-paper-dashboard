namespace EPaperDashboard.Data;

public enum DatabaseProvider
{
    LiteDb,
    PostgreSql
}

public sealed record DatabaseSettings(DatabaseProvider Provider, string? ConnectionString)
{
    public static DatabaseSettings Parse(string? provider, string? connectionString)
    {
        provider = provider?.Trim();
        if (string.IsNullOrWhiteSpace(provider) || provider.Equals("litedb", StringComparison.OrdinalIgnoreCase))
            return new(DatabaseProvider.LiteDb, null);

        if (provider.Equals("postgresql", StringComparison.OrdinalIgnoreCase) ||
            provider.Equals("postgres", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("DATABASE_CONNECTION_STRING is required when DATABASE_PROVIDER is postgresql");

            return new(DatabaseProvider.PostgreSql, connectionString);
        }

        throw new ArgumentException("DATABASE_PROVIDER must be litedb or postgresql");
    }
}

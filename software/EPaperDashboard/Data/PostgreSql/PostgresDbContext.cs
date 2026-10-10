using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace EPaperDashboard.Data.PostgreSql;

/// <summary>
/// PostgreSQL infrastructure shared by the repository implementations. Each operation owns a
/// pooled connection unless it is running inside an IUnitOfWork transaction.
/// </summary>
internal sealed class PostgresDbContext(DatabaseSettings settings) : IDisposable
{
    private readonly NpgsqlDataSource _dataSource = NpgsqlDataSource.Create(settings.ConnectionString!);
    private readonly AsyncLocal<NpgsqlConnection?> _transactionConnection = new();
    private readonly AsyncLocal<NpgsqlTransaction?> _transaction = new();

    internal bool InTransaction => _transaction.Value is not null;

    internal T? FindOne<T>(string sql, params NpgsqlParameter[] parameters) where T : class =>
        WithConnection(connection =>
        {
            using var command = CreateCommand(connection, sql, parameters);
            using var reader = command.ExecuteReader();
            return reader.Read() ? Deserialize<T>(reader.GetString(0)) : null;
        });

    internal List<T> FindMany<T>(string sql, params NpgsqlParameter[] parameters) where T : class =>
        WithConnection(connection =>
        {
            using var command = CreateCommand(connection, sql, parameters);
            using var reader = command.ExecuteReader();
            var results = new List<T>();
            while (reader.Read())
                results.Add(Deserialize<T>(reader.GetString(0)));
            return results;
        });

    internal bool Exists(string sql, params NpgsqlParameter[] parameters) =>
        WithConnection(connection =>
        {
            using var command = CreateCommand(connection, sql, parameters);
            return (bool)command.ExecuteScalar()!;
        });

    internal void Execute(string sql, params NpgsqlParameter[] parameters)
    {
        WithConnection(connection =>
        {
            using var command = CreateCommand(connection, sql, parameters);
            command.ExecuteNonQuery();
            return true;
        });
    }

    internal T ExecuteInTransaction<T>(Func<T> operation)
    {
        if (InTransaction)
            throw new InvalidOperationException("Nested persistence transactions are not supported");

        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();
        _transactionConnection.Value = connection;
        _transaction.Value = transaction;
        try
        {
            var result = operation();
            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
        finally
        {
            _transaction.Value = null;
            _transactionConnection.Value = null;
        }
    }

    internal static NpgsqlParameter Parameter(string name, object value) => new(name, value);

    internal static NpgsqlParameter DocumentParameter<T>(T value) =>
        new("document", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(value) };

    private T WithConnection<T>(Func<NpgsqlConnection, T> operation)
    {
        if (_transactionConnection.Value is { } transactionConnection)
            return operation(transactionConnection);

        using var connection = _dataSource.OpenConnection();
        return operation(connection);
    }

    private NpgsqlCommand CreateCommand(NpgsqlConnection connection, string sql, NpgsqlParameter[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection, _transaction.Value);
        command.Parameters.AddRange(parameters);
        return command;
    }

    private static T Deserialize<T>(string json) where T : class =>
        JsonSerializer.Deserialize<T>(json) ?? throw new JsonException($"Database document is not a {typeof(T).Name}");

    public void Dispose() => _dataSource.Dispose();
}

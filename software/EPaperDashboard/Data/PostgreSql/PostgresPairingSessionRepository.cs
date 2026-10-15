using EPaperDashboard.Utilities;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;

namespace EPaperDashboard.Data.PostgreSql;

internal sealed class PostgresPairingSessionRepository(PostgresDbContext context) : IPairingSessionRepository
{
    public PairingSession? FindById(PairingSessionId id) =>
        context.FindOne<PairingSession>("SELECT document FROM izboard_pairing_sessions WHERE id = @id",
            PostgresDbContext.Parameter("id", id.Value));

    public PairingSession? FindByCode(string code) =>
        context.FindOne<PairingSession>(
            "SELECT document FROM izboard_pairing_sessions WHERE code = @code" +
            (context.InTransaction ? " FOR UPDATE" : ""),
            PostgresDbContext.Parameter("code", code));

    public void Insert(PairingSession session)
    {
        if (session.Id == PairingSessionId.Empty)
            session.Id = PairingSessionId.New();

        context.Execute("""
            INSERT INTO izboard_pairing_sessions (id, code, expires_at, is_completed, document)
            VALUES (@id, @code, @expires_at, @is_completed, @document)
            """, Parameters(session));
    }

    public void Update(PairingSession session) => context.Execute("""
        UPDATE izboard_pairing_sessions SET code = @code, expires_at = @expires_at,
            is_completed = @is_completed, document = @document WHERE id = @id
        """, Parameters(session));

    public void DeleteExpired(DateTimeOffset before) => context.Execute(
        "DELETE FROM izboard_pairing_sessions WHERE expires_at <= @before",
        PostgresDbContext.Parameter("before", before.ToUniversalTime()));

    public void DeleteByUserId(UserId userId) => context.Execute(
        "DELETE FROM izboard_pairing_sessions WHERE document->>'UserId' = @user_id",
        PostgresDbContext.Parameter("user_id", userId.Value));

    public bool HasActiveSessions(DateTimeOffset now) => context.Exists("""
        SELECT EXISTS (SELECT 1 FROM izboard_pairing_sessions
            WHERE NOT is_completed AND expires_at > @now)
        """, PostgresDbContext.Parameter("now", now.ToUniversalTime()));

    private static Npgsql.NpgsqlParameter[] Parameters(PairingSession session) =>
    [
        PostgresDbContext.Parameter("id", session.Id.Value),
        PostgresDbContext.Parameter("code", session.Code),
        PostgresDbContext.Parameter("expires_at", session.ExpiresAt.ToUniversalTime()),
        PostgresDbContext.Parameter("is_completed", session.IsCompleted),
        PostgresDbContext.DocumentParameter(session)
    ];
}

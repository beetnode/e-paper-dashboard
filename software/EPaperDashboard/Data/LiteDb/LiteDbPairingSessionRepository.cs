using EPaperDashboard.Utilities;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using LiteDB;

namespace EPaperDashboard.Data.LiteDb;

internal sealed class LiteDbPairingSessionRepository(LiteDbContext context) : IPairingSessionRepository
{
    public PairingSession? FindById(PairingSessionId id) =>
        context.PairingSessions.FindById(new ObjectId(id.Value));

    public PairingSession? FindByCode(string code) =>
        context.PairingSessions.FindOne(s => s.Code == code);

    public void Insert(PairingSession session)
    {
        if (session.Id == PairingSessionId.Empty)
            session.Id = PairingSessionId.New();
        context.PairingSessions.Insert(session);
    }

    public void Update(PairingSession session) =>
        context.PairingSessions.Update(session);

    public void DeleteExpired(DateTimeOffset before) =>
        context.PairingSessions.DeleteMany(s => s.ExpiresAt <= before);

    public void DeleteByUserId(UserId userId) =>
        context.PairingSessions.DeleteMany(s => s.UserId == userId);
}

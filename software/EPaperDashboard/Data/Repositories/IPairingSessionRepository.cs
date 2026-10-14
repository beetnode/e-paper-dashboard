using CSharpFunctionalExtensions;
using EPaperDashboard.Models;

namespace EPaperDashboard.Data.Repositories;

public interface IPairingSessionRepository
{
    Maybe<PairingSession> FindById(PairingSessionId id);
    Maybe<PairingSession> FindByCode(string code);
    void Insert(PairingSession session);
    void Update(PairingSession session);
    void DeleteExpired(DateTimeOffset before);
    void DeleteByUserId(UserId userId);
    bool HasActiveSessions(DateTimeOffset now);
}

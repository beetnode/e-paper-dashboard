using EPaperDashboard.Utilities;
using EPaperDashboard.Models;

namespace EPaperDashboard.Data.Repositories;

public interface IPairingSessionRepository
{
    PairingSession? FindById(PairingSessionId id);
    PairingSession? FindByCode(string code);
    void Insert(PairingSession session);
    void Update(PairingSession session);
    void DeleteExpired(DateTimeOffset before);
    void DeleteByUserId(UserId userId);
    bool HasActiveSessions(DateTimeOffset now);
}

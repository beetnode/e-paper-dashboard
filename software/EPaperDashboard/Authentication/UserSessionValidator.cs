using System.Collections.Concurrent;
using System.Security.Claims;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using EPaperDashboard.Utilities;

namespace EPaperDashboard.Authentication;

public enum SessionState
{
    Valid,
    Reject,
    Refresh
}

public sealed record SessionCheck(SessionState State, ClaimsPrincipal? Principal = null);

/// <summary>
/// Checks a signed-in cookie against the stored account, so deleting an account, rotating its security stamp or
/// suspending it or changing its role takes effect without waiting for the user to sign in again. Accounts are cached briefly to keep
/// the per-request cost low; other replicas pick up a change within <see cref="CacheDuration"/>.
/// </summary>
public sealed class UserSessionValidator(IUserRepository users, TimeProvider time)
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<UserId, (User? User, DateTimeOffset LoadedAt)> _cache = new();

    public SessionCheck Validate(ClaimsPrincipal principal)
    {
        var id = principal.GetUserId();
        if (id == UserId.Empty)
        {
            return new SessionCheck(SessionState.Reject);
        }

        var user = Load(id);
        if (user is null || user.IsSuspended)
        {
            return new SessionCheck(SessionState.Reject);
        }

        var stamp = principal.FindFirst(Constants.SecurityStampClaim)?.Value;
        if (user.SecurityStamp is not null && !string.Equals(stamp, user.SecurityStamp, StringComparison.Ordinal))
        {
            return new SessionCheck(SessionState.Reject);
        }

        var isSuperUser = principal.FindFirst(Constants.IsSuperUserClaim)?.Value == "true";
        var username = principal.FindFirst(ClaimTypes.Name)?.Value;
        var mustChangePassword = principal.FindFirst(Constants.MustChangePasswordClaim)?.Value == "true";
        return isSuperUser != user.IsSuperUser || username != user.Username || mustChangePassword != user.MustChangePassword
            ? new SessionCheck(SessionState.Refresh, UserClaims.Create(user))
            : new SessionCheck(SessionState.Valid);
    }

    public void Invalidate(UserId id) => _cache.TryRemove(id, out _);

    private User? Load(UserId id)
    {
        var now = time.GetUtcNow();
        if (_cache.TryGetValue(id, out var cached) && now - cached.LoadedAt < CacheDuration)
        {
            return cached.User;
        }

        var user = users.FindById(id);
        _cache[id] = (user, now);
        return user;
    }
}

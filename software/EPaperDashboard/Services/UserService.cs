using CSharpFunctionalExtensions;
using EPaperDashboard.Authentication;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using EPaperDashboard.Utilities;
using System.Security.Cryptography;

namespace EPaperDashboard.Services;

public sealed class UserService(
    IUserRepository userRepository,
    IDashboardRepository dashboardRepository,
    IProviderConnectionRepository? connections = null,
    IDeploymentStrategy? deployment = null,
    IDeviceRepository? devices = null,
    IPairingSessionRepository? pairingSessions = null,
    IUnitOfWork? unitOfWork = null,
    UserSessionValidator? sessions = null)
{
    public Maybe<User> GetUserByUsername(string username) =>
        userRepository.FindByUsername(username);

    public Maybe<User> GetUserById(UserId id) =>
        userRepository.FindById(id);

    /// <summary>
    /// Whether the owner is a known account. In add-on mode the only account is the Home Assistant ingress user
    /// (<see cref="Constants.HomeAssistantVirtualUserId"/>), which has no user record: its settings are kept in
    /// service state instead. Use the owner settings below rather than reading <see cref="User"/> fields directly,
    /// so this owner is handled the same way as a regular account.
    /// </summary>
    public bool HasAccount(UserId id) =>
        IsAddonOwner(id)
        || (id != UserId.Empty && deployment?.IsUserManagementEnabled != false
            && userRepository.FindById(id).Map(user => !user.IsSuspended).GetValueOrDefault());

    /// <summary>Returns the owner's global AI provider, or null when it is not configured or the owner has no account.</summary>
    public AiConfig? GetAiConfig(UserId id) =>
        IsAddonOwner(id) ? userRepository.GetAiConfig(id) : FindAccount(id).Map(user => user.AiConfig).GetValueOrDefault();

    public bool TryUpdateAiConfig(UserId id, AiConfig config)
    {
        if (IsAddonOwner(id))
        {
            userRepository.SetAiConfig(id, config);
            return true;
        }

        return FindAccount(id).Match(
            user =>
            {
                user.AiConfig = config;
                userRepository.Update(user);
                return true;
            },
            () => false);
    }

    /// <summary>Rendering requires an explicit opt-in; owners without an account never have access.</summary>
    public bool IsRenderingEnabled(UserId id) =>
        HasAccount(id) && (userRepository.GetRenderingEnabled(id)
            ?? FindAccount(id).Map(user => user.RenderingEnabled).GetValueOrDefault() ?? false);

    public bool TrySetRenderingEnabled(UserId id, bool enabled)
    {
        if (!HasAccount(id))
        {
            return false;
        }

        userRepository.SetRenderingEnabled(id, enabled);
        return true;
    }

    private static bool IsAddonOwner(UserId id) => id == Constants.HomeAssistantVirtualUserId;

    private Maybe<User> FindAccount(UserId id) =>
        HasAccount(id) ? userRepository.FindById(id) : Maybe<User>.None;

    public bool IsSetupComplete() => userRepository.IsSetupComplete();

    public bool TryCreateAdministrator(string username, string password) =>
        userRepository.TryCreateAdministrator(new User
        {
            Username = username.Trim(), PasswordHash = PasswordHashing.Hash(password),
            IsSuperUser = true, RenderingEnabled = false,
            SecurityStamp = NewSecurityStamp(), CreatedAt = DateTimeOffset.UtcNow
        });

    public static string? ValidateCredentials(string? username, string? password) =>
        string.IsNullOrWhiteSpace(username) || username.Trim().Length is < 3 or > 100
            ? "Username must contain between 3 and 100 characters."
            : string.IsNullOrWhiteSpace(password) || password.Length is < 6 or > 1024
                ? "Password must contain between 6 and 1024 characters." : null;

    public bool HasSuperUser() =>
        userRepository.ExistsSuperUser();

    public List<User> GetAllUsers() =>
        userRepository.GetAll();

    public void UpdateUser(User user) =>
        userRepository.Update(user);

    /// <summary>
    /// Deletes the caller's own account together with everything it owns, including paired devices.
    /// The last active administrator cannot leave.
    /// </summary>
    public bool TryDeleteUser(UserId id) =>
        ExecuteUnderUsersLock(() => userRepository.FindById(id)
            .Where(u => !IsLastActiveAdministrator(u))
            .Match(
                u =>
                {
                    DeleteAccount(u);
                    return true;
                },
                () => false
            ));

    public AdminActionResult SetSuperUser(UserId actor, UserId target, bool isSuperUser) =>
        Administer(actor, target, user =>
        {
            if (!isSuperUser && IsLastActiveAdministrator(user))
            {
                return AdminActionResult.LastAdministrator;
            }

            user.IsSuperUser = isSuperUser;
            if (!isSuperUser)
            {
                user.SecurityStamp = NewSecurityStamp();
            }
            userRepository.Update(user);
            return AdminActionResult.Done;
        });

    public AdminActionResult SetSuspended(UserId actor, UserId target, bool suspended) =>
        Administer(actor, target, user =>
        {
            if (suspended && IsLastActiveAdministrator(user))
            {
                return AdminActionResult.LastAdministrator;
            }

            user.IsSuspended = suspended;
            user.SecurityStamp = NewSecurityStamp();
            userRepository.Update(user);
            return AdminActionResult.Done;
        });

    /// <summary>Replaces the target's password with a random one that must be changed at the next sign-in.</summary>
    public (AdminActionResult Result, string? TemporaryPassword) ResetPassword(UserId actor, UserId target)
    {
        string? temporaryPassword = null;
        var result = Administer(actor, target, user =>
        {
            temporaryPassword = NewTemporaryPassword();
            user.PasswordHash = PasswordHashing.Hash(temporaryPassword);
            user.MustChangePassword = true;
            user.SecurityStamp = NewSecurityStamp();
            userRepository.Update(user);
            return AdminActionResult.Done;
        });
        return (result, result == AdminActionResult.Done ? temporaryPassword : null);
    }

    public AdminActionResult DeleteUserAsAdmin(UserId actor, UserId target) =>
        Administer(actor, target, user =>
        {
            if (IsLastActiveAdministrator(user))
            {
                return AdminActionResult.LastAdministrator;
            }

            DeleteAccount(user);
            return AdminActionResult.Done;
        });

    public const int TemporaryPasswordLength = 20;

    private AdminActionResult Administer(UserId actor, UserId target, Func<User, AdminActionResult> change)
    {
        if (actor == target)
        {
            return AdminActionResult.SelfNotAllowed;
        }

        var result = ExecuteUnderUsersLock(() => userRepository.FindById(target).Match(change, () => AdminActionResult.NotFound));
        sessions?.Invalidate(target);
        return result;
    }

    // Callers hold the users lock, so two administrators cannot remove each other at the same time.
    private bool IsLastActiveAdministrator(User user) =>
        user.IsSuperUser && !user.IsSuspended && !userRepository.ExistsOtherActiveSuperUser(user.Id);

    private void DeleteAccount(User user)
    {
        dashboardRepository.DeleteByUserId(user.Id);
        connections?.DeleteByUserId(user.Id);
        devices?.DeleteByUserId(user.Id);
        pairingSessions?.DeleteByUserId(user.Id);
        userRepository.Delete(user.Id);
        sessions?.Invalidate(user.Id);
    }

    private static string NewTemporaryPassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        return RandomNumberGenerator.GetString(alphabet, TemporaryPasswordLength);
    }

    private T ExecuteUnderUsersLock<T>(Func<T> operation) =>
        unitOfWork is null ? operation() : unitOfWork.ExecuteExclusive(UsersLock, operation);

    private const string UsersLock = "users";

    /// <summary>Returns the account when the password matches, upgrading a legacy password hash on the way.</summary>
    public Maybe<User> VerifyCredentials(string username, string password)
    {
        var user = GetUserByUsername(username);
        if (user.HasNoValue)
        {
            PasswordHashing.SimulateVerification(password);
            return Maybe<User>.None;
        }

        switch (PasswordHashing.Verify(user.Value.PasswordHash, password))
        {
            case PasswordCheck.ValidNeedsRehash:
                user.Value.PasswordHash = PasswordHashing.Hash(password);
                userRepository.Update(user.Value);
                return user;
            case PasswordCheck.Valid:
                return user;
            default:
                return Maybe<User>.None;
        }
    }

    /// <summary>Creates a regular account. Administrators are only made by setup or by promoting an account.</summary>
    public bool TryCreateUser(string username, string password, bool mustChangePassword = false)
    {
        if (userRepository.ExistsByUsername(username))
        {
            return false;
        }

        var user = new User
        {
            Username = username,
            PasswordHash = PasswordHashing.Hash(password),
            RenderingEnabled = false,
            SecurityStamp = NewSecurityStamp(),
            CreatedAt = DateTimeOffset.UtcNow,
            MustChangePassword = mustChangePassword
        };

        return userRepository.TryInsert(user);
    }

    public bool TryChangeNickname(UserId userId, string? newNickname) =>
        GetUserById(userId)
        .Match(
            user =>
            {
                user.Nickname = string.IsNullOrWhiteSpace(newNickname) ? null : newNickname;
                userRepository.Update(user);
                return true;
            },
            () => false);

    public bool TryChangePassword(UserId userId, string oldPassword, string newPassword) =>
        userRepository.FindById(userId)
            .Where(user => PasswordHashing.Verify(user.PasswordHash, oldPassword) != PasswordCheck.Failed)
            .Match(
                user =>
                {
                    if (PasswordHashing.Verify(user.PasswordHash, newPassword) != PasswordCheck.Failed)
                    {
                        return false;
                    }

                    user.PasswordHash = PasswordHashing.Hash(newPassword);
                    user.MustChangePassword = false;
                    user.SecurityStamp = NewSecurityStamp();
                    userRepository.Update(user);
                    sessions?.Invalidate(user.Id);
                    return true;
                },
                () => false
            );

    private static string NewSecurityStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
}

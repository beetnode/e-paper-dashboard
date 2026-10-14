using CSharpFunctionalExtensions;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using EPaperDashboard.Utilities;
using System.Security.Cryptography;
using System.Text;

namespace EPaperDashboard.Services;

public sealed class UserService(
    IUserRepository userRepository,
    IDashboardRepository dashboardRepository,
    IProviderConnectionRepository? connections = null,
    IDeploymentStrategy? deployment = null,
    IDeviceRepository? devices = null,
    IPairingSessionRepository? pairingSessions = null,
    IUnitOfWork? unitOfWork = null)
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
        || (id != UserId.Empty && deployment?.IsUserManagementEnabled != false && userRepository.FindById(id).HasValue);

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
            Username = username.Trim(), PasswordHash = ComputeSha256Hash(password),
            IsSuperUser = true, RenderingEnabled = false
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

    /// <summary>Deletes a regular account together with everything it owns, including paired devices.</summary>
    public bool TryDeleteUser(UserId id) =>
        ExecuteUnderUsersLock(() => userRepository.FindById(id)
            .Where(u => !u.IsSuperUser)
            .Match(
                u =>
                {
                    dashboardRepository.DeleteByUserId(u.Id);
                    connections?.DeleteByUserId(u.Id);
                    devices?.DeleteByUserId(u.Id);
                    pairingSessions?.DeleteByUserId(u.Id);
                    userRepository.Delete(u.Id);
                    return true;
                },
                () => false
            ));

    private T ExecuteUnderUsersLock<T>(Func<T> operation) =>
        unitOfWork is null ? operation() : unitOfWork.ExecuteExclusive(UsersLock, operation);

    private const string UsersLock = "users";

    public bool IsUserValid(string username, string password) =>
        GetUserByUsername(username)
        .Select(user => string.Equals(user.PasswordHash, ComputeSha256Hash(password), StringComparison.OrdinalIgnoreCase))
        .GetValueOrDefault();

    public bool TryCreateUser(string username, string password, bool isSuperUser = false)
    {
        if (userRepository.ExistsByUsername(username))
        {
            return false;
        }

        var user = new User
        {
            Username = username,
            PasswordHash = ComputeSha256Hash(password),
            IsSuperUser = isSuperUser,
            RenderingEnabled = false
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
            .Where(user => string.Equals(user.PasswordHash, ComputeSha256Hash(oldPassword), StringComparison.Ordinal))
            .Match(
                user =>
                {
                    var newHash = ComputeSha256Hash(newPassword);
                    if (string.Equals(user.PasswordHash, newHash, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    user.PasswordHash = newHash;
                    userRepository.Update(user);
                    return true;
                },
                () => false
            );

    public static string ComputeSha256Hash(string rawData)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawData));
        var builder = new StringBuilder();
        foreach (var b in bytes)
        {
            builder.Append(b.ToString("x2"));
        }

        return builder.ToString();
    }
}

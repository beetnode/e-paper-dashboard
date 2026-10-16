using EPaperDashboard.Utilities;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using System.Security.Cryptography;
using System.Text;

namespace EPaperDashboard.Services;

public enum PairingFailure
{
    None,
    InvalidRequest,
    NotFound,
    Expired,
    Conflict,
    AlreadyClaimed,
    DeviceOwnedByAnotherUser,
    InvalidRegistrationToken,
    LimitReached
}

public sealed record PairingResult<T>(T? Value, PairingFailure Failure, string? Message) where T : class
{
    public bool IsSuccess => Failure == PairingFailure.None && Value is not null;

    public static PairingResult<T> Success(T value) => new(value, PairingFailure.None, null);
    public static PairingResult<T> Failed(PairingFailure failure, string message) => new(null, failure, message);
}

public sealed class PairingService(
    IPairingSessionRepository pairingSessionRepository,
    DeviceService deviceService,
    TimeProvider timeProvider,
    IUnitOfWork unitOfWork)
{
    private const int CodeLength = 6;
    private const int DeviceClaimExpiryMinutes = 10;
    private const int CredentialDeliveryExpiryMinutes = 2;
    private readonly object _sync = new();
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    public PairingSession? GetPairingSessionByCode(string code) =>
        pairingSessionRepository.FindByCode(NormalizeCode(code));

    public PairingResult<PairingSession> AnnounceDevice(
        string code,
        string registrationToken,
        string deviceIdentifier,
        string? deviceName,
        int? screenWidth,
        int? screenHeight)
    {
        var normalizedCode = NormalizeCode(code);
        if (!IsValidCode(normalizedCode)
            || !IsValidRegistrationToken(registrationToken)
            || string.IsNullOrWhiteSpace(deviceIdentifier)
            || deviceIdentifier.Length > 128
            || deviceName?.Length > 100)
        {
            return PairingResult<PairingSession>.Failed(PairingFailure.InvalidRequest, "Invalid device registration request");
        }

        lock (_sync)
        {
            var now = timeProvider.GetUtcNow();
            pairingSessionRepository.DeleteExpired(now);
            var tokenHash = HashRegistrationToken(registrationToken);
            var existing = pairingSessionRepository.FindByCode(normalizedCode);

            if (existing is not null)
            {
                var session = existing;
                if (RegistrationTokenMatches(session, registrationToken)
                    && string.Equals(session.DeviceIdentifier, deviceIdentifier, StringComparison.OrdinalIgnoreCase))
                {
                    return PairingResult<PairingSession>.Success(session);
                }

                return PairingResult<PairingSession>.Failed(
                    PairingFailure.Conflict,
                    "Claim code is already in use; restart setup to generate a new code");
            }

            var announced = new PairingSession
            {
                UserId = UserId.Empty,
                Code = normalizedCode,
                RegistrationTokenHash = tokenHash,
                DeviceIdentifier = deviceIdentifier.Trim(),
                DeviceName = string.IsNullOrWhiteSpace(deviceName) ? deviceIdentifier.Trim() : deviceName.Trim(),
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(DeviceClaimExpiryMinutes),
                Status = PairingStatus.Pending,
                IsCompleted = false
            };
            SetScreenDimensions(announced, screenWidth, screenHeight);
            pairingSessionRepository.Insert(announced);
            return PairingResult<PairingSession>.Success(announced);
        }
    }

    public PairingResult<Device> ClaimDevice(string code, UserId userId)
    {
        var normalizedCode = NormalizeCode(code);
        if (!IsValidCode(normalizedCode) || userId == UserId.Empty)
        {
            return PairingResult<Device>.Failed(PairingFailure.InvalidRequest, "Invalid claim code");
        }

        lock (_sync)
        {
            return _unitOfWork.Execute(() =>
            {
                var now = timeProvider.GetUtcNow();
                var maybeSession = pairingSessionRepository.FindByCode(normalizedCode);
                if (maybeSession is null || string.IsNullOrWhiteSpace(maybeSession.RegistrationTokenHash))
                {
                    return PairingResult<Device>.Failed(
                        PairingFailure.NotFound, "Pending device not found");
                }

                var session = maybeSession;
                if (session.ExpiresAt <= now)
                {
                    pairingSessionRepository.DeleteExpired(now);
                    return PairingResult<Device>.Failed(PairingFailure.Expired, "Claim code expired");
                }

                if (session.Status != PairingStatus.Pending)
                {
                    return PairingResult<Device>.Failed(
                        PairingFailure.AlreadyClaimed, "Device has already been claimed");
                }

                var existingDevice = deviceService.GetDeviceByIdentifier(session.DeviceIdentifier!);
                if (existingDevice is not null)
                {
                    if (existingDevice.UserId != userId)
                    {
                        return PairingResult<Device>.Failed(
                            PairingFailure.DeviceOwnedByAnotherUser,
                            "Device is owned by another user and must be released before it can be claimed");
                    }

                }
                else if (UserLimits.AppliesTo(userId) && deviceService.GetDevicesForUser(userId).Count >= UserLimits.MaxDevices)
                {
                    return PairingResult<Device>.Failed(
                        PairingFailure.LimitReached, $"You can pair up to {UserLimits.MaxDevices} devices.");
                }

                // The plaintext key waits in the session only until the device collects it.
                var apiKey = DeviceApiKey.Generate();
                var device = CreatePendingDevice(existingDevice, session, userId, now, apiKey);
                session.UserId = userId;
                session.ApiKey = apiKey;
                session.PendingDeviceId = device.Id;
                session.ClaimedAt = now;
                session.ExpiresAt = now.AddMinutes(CredentialDeliveryExpiryMinutes);
                session.Status = PairingStatus.Claimed;
                pairingSessionRepository.Update(session);
                return PairingResult<Device>.Success(device);
            });
        }
    }

    public PairingResult<PairingSession> GetDeviceClaimStatus(string code, string registrationToken)
    {
        var normalizedCode = NormalizeCode(code);
        if (!IsValidCode(normalizedCode) || !IsValidRegistrationToken(registrationToken))
        {
            return PairingResult<PairingSession>.Failed(PairingFailure.InvalidRequest, "Invalid status request");
        }

        lock (_sync)
        {
            var now = timeProvider.GetUtcNow();
            var maybeSession = pairingSessionRepository.FindByCode(normalizedCode);
            if (maybeSession is null)
            {
                return PairingResult<PairingSession>.Failed(PairingFailure.NotFound, "Pending device not found");
            }

            var session = maybeSession;
            if (session.ExpiresAt <= now)
            {
                pairingSessionRepository.DeleteExpired(now);
                return PairingResult<PairingSession>.Failed(PairingFailure.Expired, "Claim code expired");
            }

            if (!RegistrationTokenMatches(session, registrationToken))
            {
                return PairingResult<PairingSession>.Failed(
                    PairingFailure.InvalidRegistrationToken,
                    "Invalid registration token");
            }

            if (session.Status == PairingStatus.Claimed)
            {
                _unitOfWork.Execute(() =>
                {
                    var existingDevice = deviceService.GetDeviceByIdentifier(session.DeviceIdentifier!);
                    var device = CreatePendingDevice(existingDevice, session, session.UserId, now, session.ApiKey);
                    if (existingDevice is not null)
                    {
                        deviceService.UpdateDevice(device);
                    }
                    else
                    {
                        deviceService.AddDevice(device);
                    }

                    session.Status = PairingStatus.Completed;
                    session.IsCompleted = true;
                    pairingSessionRepository.Update(session);
                    return true;
                });
            }

            return PairingResult<PairingSession>.Success(session);
        }
    }

    public void CleanupExpiredSessions() =>
        pairingSessionRepository.DeleteExpired(timeProvider.GetUtcNow());

    public int GetSecondsUntilExpiry(PairingSession session) =>
        Math.Max(0, (int)Math.Ceiling((session.ExpiresAt - timeProvider.GetUtcNow()).TotalSeconds));

    private static Device CreatePendingDevice(
        Device? existing,
        PairingSession session,
        UserId userId,
        DateTimeOffset now,
        string apiKey)
    {
        return new Device
        {
            Id = existing?.Id ?? (session.PendingDeviceId != DeviceId.Empty
                ? session.PendingDeviceId
                : DeviceId.New()),
            UserId = userId,
            DashboardId = existing?.DashboardId ?? DashboardId.Empty,
            DeviceIdentifier = session.DeviceIdentifier!,
            Name = existing?.Name ?? session.DeviceName ?? session.DeviceIdentifier!,
            ApiKeyHash = DeviceApiKey.Hash(apiKey),
            PairedAt = now,
            LastSeenAt = existing?.LastSeenAt,
            FirmwareVersion = existing?.FirmwareVersion,
            ScreenWidth = session.ScreenWidth,
            ScreenHeight = session.ScreenHeight
        };
    }

    private static string NormalizeCode(string? code) => code?.Trim().ToUpperInvariant() ?? string.Empty;

    private static bool IsValidCode(string code) =>
        code.Length == CodeLength && code.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c));

    private static bool IsValidRegistrationToken(string? token) =>
        token?.Length == 32 && token.All(Uri.IsHexDigit);

    private static string HashRegistrationToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static bool RegistrationTokenMatches(PairingSession session, string token)
    {
        if (string.IsNullOrWhiteSpace(session.RegistrationTokenHash))
        {
            return false;
        }

        try
        {
            var expected = Convert.FromHexString(session.RegistrationTokenHash);
            var actual = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void SetScreenDimensions(PairingSession session, int? screenWidth, int? screenHeight)
    {
        if (screenWidth is > 0 and <= 4000 && screenHeight is > 0 and <= 4000)
        {
            session.ScreenWidth = Math.Max(screenWidth.Value, screenHeight.Value);
            session.ScreenHeight = Math.Min(screenWidth.Value, screenHeight.Value);
        }
    }
}

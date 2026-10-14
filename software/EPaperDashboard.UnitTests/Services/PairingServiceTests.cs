using CSharpFunctionalExtensions;
using EPaperDashboard.Data.Repositories;
using EPaperDashboard.Models;
using EPaperDashboard.Services;
using EPaperDashboard.Utilities;
using EPaperDashboard.UnitTests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace EPaperDashboard.UnitTests.Services;

public class PairingServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static Mock<IPairingSessionRepository> CreateRepo() => new();

    private static PairingService CreateSut(
        Mock<IPairingSessionRepository> repo,
        TimeProvider? timeProvider = null,
        Mock<IDeviceRepository>? deviceRepo = null) =>
        new(repo.Object, new DeviceService((deviceRepo ?? new Mock<IDeviceRepository>()).Object),
            timeProvider ?? new FakeTimeProvider(FixedNow), ImmediateUnitOfWork.Instance);

    [Fact]
    public void CreatePairingSession_SetsCreatedAtToCurrentTime()
    {
        var repo = CreateRepo();
        var sut = CreateSut(repo);

        var session = sut.CreatePairingSession(UserId.New());

        session.CreatedAt.Should().Be(FixedNow);
    }

    [Fact]
    public void CreatePairingSession_SetsExpiryFiveMinutesAfterCreation()
    {
        var repo = CreateRepo();
        var sut = CreateSut(repo);

        var session = sut.CreatePairingSession(UserId.New());

        session.ExpiresAt.Should().Be(FixedNow.AddMinutes(5));
    }

    [Fact]
    public void CreatePairingSession_GeneratesSixCharacterUppercaseAlphanumericCode()
    {
        var repo = CreateRepo();
        var sut = CreateSut(repo);

        var session = sut.CreatePairingSession(UserId.New());

        session.Code.Should().MatchRegex("^[0-9A-Z]{6}$");
    }

    [Fact]
    public void CreatePairingSession_PersistsSessionViaRepository()
    {
        var repo = CreateRepo();
        var sut = CreateSut(repo);

        var session = sut.CreatePairingSession(UserId.New());

        repo.Verify(r => r.Insert(session), Times.Once);
        session.Status.Should().Be(PairingStatus.Pending);
        session.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public void RegisterDevice_UnknownCode_ReturnsMaybeNone()
    {
        var repo = CreateRepo();
        repo.Setup(r => r.FindByCode("BADCOD")).Returns(Maybe<PairingSession>.None);
        var sut = CreateSut(repo);

        var result = sut.RegisterDevice("BADCOD", "device-1");

        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public void RegisterDevice_ValidCode_CompletesSessionAndAssignsApiKey()
    {
        var repo = CreateRepo();
        var session = new PairingSession { Code = "ABC123", Status = PairingStatus.Pending, ExpiresAt = FixedNow.AddMinutes(5) };
        repo.Setup(r => r.FindByCode("ABC123")).Returns(session);
        var sut = CreateSut(repo);

        var result = sut.RegisterDevice("ABC123", "device-1");

        result.HasValue.Should().BeTrue();
        result.Value.Status.Should().Be(PairingStatus.Completed);
        result.Value.IsCompleted.Should().BeTrue();
        result.Value.DeviceIdentifier.Should().Be("device-1");
        result.Value.ApiKey.Should().NotBeNullOrEmpty();
        repo.Verify(r => r.Update(session), Times.Once);
    }

    [Fact]
    public void RegisterDevice_WithScreenDimensions_NormalizesWidthAsMaxAndHeightAsMin()
    {
        var repo = CreateRepo();
        var session = new PairingSession { Code = "ABC123", ExpiresAt = FixedNow.AddMinutes(5) };
        repo.Setup(r => r.FindByCode("ABC123")).Returns(session);
        var sut = CreateSut(repo);

        // portrait device reports width < height
        var result = sut.RegisterDevice("ABC123", "device-1", screenWidth: 480, screenHeight: 800);

        result.Value.ScreenWidth.Should().Be(800);
        result.Value.ScreenHeight.Should().Be(480);
    }

    [Fact]
    public void RegisterDevice_WithoutScreenDimensions_LeavesScreenSizeUnset()
    {
        var repo = CreateRepo();
        var session = new PairingSession { Code = "ABC123", ExpiresAt = FixedNow.AddMinutes(5) };
        repo.Setup(r => r.FindByCode("ABC123")).Returns(session);
        var sut = CreateSut(repo);

        var result = sut.RegisterDevice("ABC123", "device-1");

        result.Value.ScreenWidth.Should().BeNull();
        result.Value.ScreenHeight.Should().BeNull();
    }

    [Fact]
    public void HasActiveSessions_DelegatesToRepositoryWithCurrentTime()
    {
        var repo = CreateRepo();
        repo.Setup(r => r.HasActiveSessions(FixedNow)).Returns(true);
        var sut = CreateSut(repo);

        sut.HasActiveSessions().Should().BeTrue();
        repo.Verify(r => r.HasActiveSessions(FixedNow), Times.Once);
    }

    [Fact]
    public void CleanupExpiredSessions_DelegatesToRepositoryWithCurrentTime()
    {
        var repo = CreateRepo();
        var sut = CreateSut(repo);

        sut.CleanupExpiredSessions();

        repo.Verify(r => r.DeleteExpired(FixedNow), Times.Once);
    }

    [Fact]
    public void AnnounceDevice_CreatesPendingSessionWithHashedToken()
    {
        var repo = CreateRepo();
        repo.Setup(r => r.FindByCode("ABC123")).Returns(Maybe<PairingSession>.None);
        PairingSession? inserted = null;
        repo.Setup(r => r.Insert(It.IsAny<PairingSession>()))
            .Callback<PairingSession>(session => inserted = session);
        var sut = CreateSut(repo);

        var result = sut.AnnounceDevice(
            "abc123", "0123456789abcdef0123456789abcdef", "device-1", "Kitchen", 480, 800);

        result.IsSuccess.Should().BeTrue();
        inserted.Should().NotBeNull();
        inserted!.Code.Should().Be("ABC123");
        inserted.RegistrationTokenHash.Should().NotBe("0123456789abcdef0123456789abcdef");
        inserted.ExpiresAt.Should().Be(FixedNow.AddMinutes(10));
        inserted.ScreenWidth.Should().Be(800);
        inserted.ScreenHeight.Should().Be(480);
    }

    [Fact]
    public void DeviceIssuedFlow_ClaimCreatesDeviceAndReturnsCredentialOnlyToMatchingToken()
    {
        const string token = "0123456789abcdef0123456789abcdef";
        PairingSession? stored = null;
        var repo = CreateRepo();
        repo.Setup(r => r.FindByCode("ABC123"))
            .Returns(() => stored is null ? Maybe<PairingSession>.None : Maybe.From(stored));
        repo.Setup(r => r.Insert(It.IsAny<PairingSession>()))
            .Callback<PairingSession>(session => stored = session);

        var deviceRepo = new Mock<IDeviceRepository>();
        deviceRepo.Setup(r => r.FindByIdentifier("device-1")).Returns(Maybe<Device>.None);
        deviceRepo.Setup(r => r.FindByUserId(It.IsAny<UserId>())).Returns([]);
        Device? insertedDevice = null;
        deviceRepo.Setup(r => r.Insert(It.IsAny<Device>()))
            .Callback<Device>(device => insertedDevice = device);
        var sut = CreateSut(repo, deviceRepo: deviceRepo);
        sut.AnnounceDevice("ABC123", token, "device-1", "Kitchen", 800, 480);
        var userId = UserId.New();

        var claim = sut.ClaimDevice("abc123", userId);
        stored!.Status.Should().Be(PairingStatus.Claimed);
        stored.IsCompleted.Should().BeFalse();
        insertedDevice.Should().BeNull();
        var wrongTokenStatus = sut.GetDeviceClaimStatus(
            "ABC123", "ffffffffffffffffffffffffffffffff");
        insertedDevice.Should().BeNull();
        var status = sut.GetDeviceClaimStatus("ABC123", token);

        claim.IsSuccess.Should().BeTrue();
        insertedDevice.Should().NotBeNull();
        insertedDevice!.UserId.Should().Be(userId);
        insertedDevice.ApiKey.Should().NotBeNullOrWhiteSpace();
        stored!.ExpiresAt.Should().Be(FixedNow.AddMinutes(2));
        wrongTokenStatus.Failure.Should().Be(PairingFailure.InvalidRegistrationToken);
        status.IsSuccess.Should().BeTrue();
        status.Value!.Status.Should().Be(PairingStatus.Completed);
        status.Value.ApiKey.Should().Be(insertedDevice.ApiKey);
    }

    [Fact]
    public void ClaimDevice_WhenDeviceBelongsToAnotherUser_DoesNotTransferOwnership()
    {
        const string token = "0123456789abcdef0123456789abcdef";
        PairingSession? stored = null;
        var repo = CreateRepo();
        repo.Setup(r => r.FindByCode("ABC123"))
            .Returns(() => stored is null ? Maybe<PairingSession>.None : Maybe.From(stored));
        repo.Setup(r => r.Insert(It.IsAny<PairingSession>()))
            .Callback<PairingSession>(session => stored = session);
        var existing = new Device { UserId = UserId.New(), DeviceIdentifier = "device-1" };
        var deviceRepo = new Mock<IDeviceRepository>();
        deviceRepo.Setup(r => r.FindByIdentifier("device-1")).Returns(existing);
        var sut = CreateSut(repo, deviceRepo: deviceRepo);
        sut.AnnounceDevice("ABC123", token, "device-1", "Kitchen", 800, 480);

        var result = sut.ClaimDevice("ABC123", UserId.New());

        result.Failure.Should().Be(PairingFailure.DeviceOwnedByAnotherUser);
        deviceRepo.Verify(r => r.Update(It.IsAny<Device>()), Times.Never);
        existing.UserId.Should().NotBe(UserId.Empty);
    }

    [Fact]
    public void GetSecondsUntilExpiry_UsesInjectedClockAndRoundsUp()
    {
        var repo = CreateRepo();
        var sut = CreateSut(repo);
        var session = new PairingSession { ExpiresAt = FixedNow.AddSeconds(30.5) };

        sut.GetSecondsUntilExpiry(session).Should().Be(31);
    }

    [Fact]
    public void ClaimDevice_AtTheDeviceLimit_DoesNotAddAnotherDevice()
    {
        const string token = "0123456789abcdef0123456789abcdef";
        PairingSession? stored = null;
        var repo = CreateRepo();
        repo.Setup(r => r.FindByCode("ABC123"))
            .Returns(() => stored is null ? Maybe<PairingSession>.None : Maybe.From(stored));
        repo.Setup(r => r.Insert(It.IsAny<PairingSession>())).Callback<PairingSession>(session => stored = session);
        var owner = UserId.New();
        var deviceRepo = new Mock<IDeviceRepository>();
        deviceRepo.Setup(r => r.FindByIdentifier("device-new")).Returns(Maybe<Device>.None);
        deviceRepo.Setup(r => r.FindByUserId(owner))
            .Returns(Enumerable.Range(0, UserLimits.MaxDevices).Select(i => new Device { UserId = owner }).ToList());
        var sut = CreateSut(repo, deviceRepo: deviceRepo);
        sut.AnnounceDevice("ABC123", token, "device-new", "Hall", 800, 480);

        var result = sut.ClaimDevice("ABC123", owner);

        result.Failure.Should().Be(PairingFailure.LimitReached);
        deviceRepo.Verify(r => r.Insert(It.IsAny<Device>()), Times.Never);
    }
}

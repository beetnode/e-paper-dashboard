using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EPaperDashboard.Services;
using EPaperDashboard.Guards;
using EPaperDashboard.Models;
using EPaperDashboard.Utilities;
using Microsoft.AspNetCore.RateLimiting;

namespace EPaperDashboard.Controllers;

[ApiController]
[Route("api/pairing")]
public class PairingController(
    PairingService pairingService,
    DeviceService deviceService,
    IEnvironmentConfiguration environmentConfiguration) : BaseApiController
{
    private readonly PairingService _pairingService = pairingService;
    private readonly DeviceService _deviceService = deviceService;
    private readonly IEnvironmentConfiguration _environmentConfiguration = environmentConfiguration;

    [HttpGet("configuration")]
    [Authorize]
    public IActionResult GetPairingConfiguration()
    {
        var clientUri = _environmentConfiguration.ClientUri;
        var validationError = ClientUrlValidator.GetValidationError(clientUri);
        if (validationError is not null)
        {
            return Problem(
                $"{validationError}. Set CLIENT_URL to an HTTP or HTTPS URL reachable from the display's Wi-Fi network.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        // Devices reach the TLS device port directly on the dashboard's host, not CLIENT_URL itself.
        var serverAddress = $"{clientUri!.Host}:{_environmentConfiguration.DevicePublicPort}";
        return Ok(new PairingConfigurationResponse(clientUri.AbsoluteUri.TrimEnd('/'), serverAddress));
    }

    [HttpPost("announce")]
    [AllowAnonymous]
    [DeviceAccessible]
    [EnableRateLimiting("PairingAnnounce")]
    public IActionResult AnnounceDevice([FromBody] AnnounceDeviceRequest request)
    {
        var result = _pairingService.AnnounceDevice(
            request.Code,
            request.RegistrationToken,
            request.DeviceIdentifier,
            request.DeviceName,
            request.ScreenWidth,
            request.ScreenHeight);

        if (!result.IsSuccess)
        {
            return PairingError(result.Failure, result.Message!);
        }

        return Accepted(new AnnounceDeviceResponse(
            result.Value!.ExpiresAt,
            _pairingService.GetSecondsUntilExpiry(result.Value)));
    }

    [HttpPost("claim")]
    [Authorize]
    [EnableRateLimiting("PairingClaim")]
    public IActionResult ClaimDevice([FromBody] ClaimDeviceRequest request)
    {
        var result = _pairingService.ClaimDevice(request.Code, CurrentUserId);
        if (!result.IsSuccess)
        {
            return PairingError(result.Failure, result.Message!);
        }

        var session = _pairingService.GetPairingSessionByCode(request.Code);
        if (session is null)
        {
            return NotFound("Invalid pairing code");
        }

        return Ok(new ClaimDeviceResponse(
            result.Value!.Id.Value,
            result.Value.DeviceIdentifier,
            result.Value.Name,
            session.ExpiresAt));
    }

    [HttpPost("device-status")]
    [AllowAnonymous]
    [DeviceAccessible]
    [EnableRateLimiting("PairingStatus")]
    public IActionResult GetDeviceClaimStatus([FromBody] DeviceClaimStatusRequest request)
    {
        var result = _pairingService.GetDeviceClaimStatus(request.Code, request.RegistrationToken);
        if (!result.IsSuccess)
        {
            return PairingError(result.Failure, result.Message!);
        }

        var session = result.Value!;
        return Ok(new DeviceClaimStatusResponse(
            session.Status == PairingStatus.Completed ? "completed" : "pending",
            session.Status == PairingStatus.Completed ? session.ApiKey : null,
            session.ExpiresAt,
            _pairingService.GetSecondsUntilExpiry(session)));
    }

    [HttpGet("status")]
    [Authorize]
    public IActionResult GetPairingStatus([FromQuery] string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest("Code is required");
        }

        var session = _pairingService.GetPairingSessionByCode(code);
        if (session is null)
        {
            return NotFound("Invalid pairing code");
        }

        if (session.UserId != CurrentUserId)
        {
            return Forbid();
        }

        if (session.Status != PairingStatus.Completed
            && _pairingService.GetSecondsUntilExpiry(session) == 0)
        {
            return PairingError(PairingFailure.Expired, "Pairing confirmation expired");
        }

        var statusString = session.Status switch
        {
            PairingStatus.Pending => "pending",
            PairingStatus.Completed => "completed",
            PairingStatus.Claimed => "claimed",
            _ => "unknown"
        };

        return Ok(new PairingStatusResponse
        {
            Status = statusString,
            DeviceIdentifier = session.DeviceIdentifier,
            ExpiresAt = session.ExpiresAt
        });
    }

    private ObjectResult PairingError(PairingFailure failure, string message)
    {
        var statusCode = failure switch
        {
            PairingFailure.InvalidRequest => StatusCodes.Status400BadRequest,
            PairingFailure.InvalidRegistrationToken => StatusCodes.Status401Unauthorized,
            PairingFailure.NotFound => StatusCodes.Status404NotFound,
            PairingFailure.Expired => StatusCodes.Status410Gone,
            PairingFailure.Conflict or PairingFailure.AlreadyClaimed or PairingFailure.DeviceOwnedByAnotherUser
                or PairingFailure.LimitReached => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };

        return StatusCode(statusCode, new ProblemDetails { Status = statusCode, Detail = message });
    }
}

public record PairingConfigurationResponse(string ClientUrl, string ServerAddress);

public record AnnounceDeviceRequest(
    string Code,
    string RegistrationToken,
    string DeviceIdentifier,
    string? DeviceName,
    int? ScreenWidth,
    int? ScreenHeight);

public record AnnounceDeviceResponse(DateTimeOffset ExpiresAt, int ExpiresInSeconds);

public record ClaimDeviceRequest(string Code);

public record ClaimDeviceResponse(string Id, string DeviceIdentifier, string Name, DateTimeOffset AcknowledgementExpiresAt);

public record DeviceClaimStatusRequest(string Code, string RegistrationToken);

public record DeviceClaimStatusResponse(string Status, string? ApiKey, DateTimeOffset ExpiresAt, int ExpiresInSeconds);

public record PairingStatusResponse
{
    public string Status { get; init; } = string.Empty;
    public string? DeviceIdentifier { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

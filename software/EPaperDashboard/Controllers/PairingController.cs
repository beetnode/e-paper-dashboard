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

        return Ok(new PairingConfigurationResponse(clientUri!.AbsoluteUri.TrimEnd('/')));
    }

    [HttpPost("start")]
    [Authorize]
    public IActionResult StartPairing()
    {
        var session = _pairingService.CreatePairingSession(CurrentUserId);

        return Ok(new StartPairingResponse
        {
            Code = session.Code,
            ExpiresAt = session.ExpiresAt
        });
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [DeviceAccessible(RequireActivePairing = true)]
    [EnableRateLimiting("PairingAnnounce")]
    public IActionResult RegisterDevice([FromBody] RegisterDeviceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.DeviceIdentifier))
        {
            return BadRequest("Code and DeviceIdentifier are required");
        }

        var session = _pairingService.GetPairingSessionByCode(request.Code);
        if (session.HasNoValue)
        {
            return NotFound("Invalid pairing code");
        }

        if (session.Value.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return BadRequest("Pairing code expired");
        }

        if (session.Value.Status != PairingStatus.Pending)
        {
            return BadRequest("Pairing session is not in a valid state");
        }

        var existingDevice = _deviceService.GetDeviceByIdentifier(request.DeviceIdentifier);

        if (existingDevice.HasValue && existingDevice.Value.UserId != session.Value.UserId)
        {
            return Conflict("Device is owned by another user and must be released before it can be paired");
        }

        var registered = _pairingService.RegisterDevice(
            request.Code, request.DeviceIdentifier, request.ScreenWidth, request.ScreenHeight);

        if (registered.HasNoValue)
        {
            return StatusCode(500, "Failed to register device");
        }

        if (existingDevice.HasValue)
        {
            existingDevice.Value.ApiKey = registered.Value.ApiKey;
            existingDevice.Value.PairedAt = DateTimeOffset.UtcNow;
            existingDevice.Value.ScreenWidth = registered.Value.ScreenWidth;
            existingDevice.Value.ScreenHeight = registered.Value.ScreenHeight;

            _deviceService.UpdateDevice(existingDevice.Value);
        }
        else
        {
            var device = new Models.Device
            {
                UserId = registered.Value.UserId,
                DeviceIdentifier = registered.Value.DeviceIdentifier!,
                Name = request.DeviceName ?? registered.Value.DeviceIdentifier!,
                ApiKey = registered.Value.ApiKey,
                PairedAt = DateTimeOffset.UtcNow,
                ScreenWidth = registered.Value.ScreenWidth,
                ScreenHeight = registered.Value.ScreenHeight
            };
            _deviceService.AddDevice(device);
        }

        return Ok(new RegisterDeviceResponse
        {
            ApiKey = registered.Value.ApiKey
        });
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

        var session = _pairingService.GetPairingSessionByCode(request.Code).Value;
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
        if (session.HasNoValue)
        {
            return NotFound("Invalid pairing code");
        }

        if (session.Value.UserId != CurrentUserId)
        {
            return Forbid();
        }

        if (session.Value.Status != PairingStatus.Completed
            && _pairingService.GetSecondsUntilExpiry(session.Value) == 0)
        {
            return PairingError(PairingFailure.Expired, "Pairing confirmation expired");
        }

        var statusString = session.Value.Status switch
        {
            PairingStatus.Pending => "pending",
            PairingStatus.Completed => "completed",
            PairingStatus.Claimed => "claimed",
            _ => "unknown"
        };

        return Ok(new PairingStatusResponse
        {
            Status = statusString,
            DeviceIdentifier = session.Value.DeviceIdentifier,
            ExpiresAt = session.Value.ExpiresAt
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

public record PairingConfigurationResponse(string ClientUrl);

public record StartPairingResponse
{
    public string Code { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; init; }
}

public record RegisterDeviceRequest(string Code, string DeviceIdentifier, string? DeviceName, int? ScreenWidth, int? ScreenHeight);

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

public record RegisterDeviceResponse
{
    public string ApiKey { get; init; } = string.Empty;
}

public record PairingStatusResponse
{
    public string Status { get; init; } = string.Empty;
    public string? DeviceIdentifier { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using EPaperDashboard.Services;
using EPaperDashboard.Models;
using EPaperDashboard.Utilities;

namespace EPaperDashboard.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthApiController(UserService userService, IDeploymentStrategy deploymentStrategy) : BaseApiController
{
    private readonly UserService _userService = userService;
    private readonly IDeploymentStrategy _deploymentStrategy = deploymentStrategy;

    [HttpGet("setup")]
    [AllowAnonymous]
    public IActionResult GetSetupStatus()
    {
        var localAccounts = _deploymentStrategy.IsUserManagementEnabled;
        var setupComplete = localAccounts && _userService.IsSetupComplete();
        return Ok(new
        {
            setupRequired = localAccounts && !setupComplete,
            registrationEnabled = setupComplete
        });
    }

    [HttpPost("setup")]
    [AllowAnonymous]
    [EnableRateLimiting("AccountCreation")]
    public async Task<IActionResult> Setup([FromBody] RegisterRequest request)
    {
        if (!_deploymentStrategy.IsUserManagementEnabled) return Forbid();
        var error = UserService.ValidateCredentials(request.Username, request.Password);
        if (error is not null) return BadRequest(new { message = error });
        if (!_userService.TryCreateAdministrator(request.Username, request.Password))
            return Conflict(new { message = "Service setup is already complete. Sign in or create a regular account." });
        return await Login(new LoginRequest(request.Username.Trim(), request.Password));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("Login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!_deploymentStrategy.IsUserManagementEnabled) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
            return Unauthorized(new { message = "Invalid username or password." });
        var user = _userService.VerifyCredentials(request.Username, request.Password);
        if (user.HasNoValue)
        {
            return Unauthorized(new { message = "Invalid username or password." });
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Value.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Value.Username),
            new Claim(Constants.IsSuperUserClaim, user.Value.IsSuperUser.ToString().ToLower())
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        return Ok(UserResponse(user.Value.Id.ToString(), user.Value.Username, user.Value.Nickname, user.Value.IsSuperUser));
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("AccountCreation")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (!_deploymentStrategy.IsUserManagementEnabled) return Forbid();
        if (!_userService.IsSetupComplete())
            return Conflict(new { message = "Create the service administrator through initial setup first." });
        var error = UserService.ValidateCredentials(request.Username, request.Password);
        if (error is not null) return BadRequest(new { message = error });
        request = request with { Username = request.Username.Trim() };
        var existingUser = _userService.GetUserByUsername(request.Username);
        if (existingUser.HasValue)
        {
            return BadRequest(new { message = "Username already exists." });
        }

        if (!_userService.TryCreateUser(request.Username, request.Password, isSuperUser: false))
        {
            return BadRequest(new { message = "Failed to create user." });
        }

        var user = _userService.GetUserByUsername(request.Username);
        if (user.HasNoValue)
        {
            return BadRequest(new { message = "Failed to retrieve created user." });
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Value.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Value.Username),
            new Claim(Constants.IsSuperUserClaim, user.Value.IsSuperUser.ToString().ToLower())
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        return Ok(UserResponse(user.Value.Id.ToString(), user.Value.Username, user.Value.Nickname, user.Value.IsSuperUser));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { message = "Logged out successfully." });
    }

    [HttpGet("current")]
    public IActionResult GetCurrentUser()
    {
        if (!User.Identity?.IsAuthenticated ?? true)
        {
            return Unauthorized(new { message = "Not authenticated." });
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var username = CurrentUsername;
        var isSuperUser = IsSuperUser;
        var isHAIngress = IsHomeAssistantIngress;

        // In Home Assistant ingress mode, return simplified user info
        if (isHAIngress)
        {
            return Ok(UserResponse(userId, username, username, isSuperUser));
        }

        // In standalone mode, get full user details from database
        var user = _userService.GetUserById(CurrentUserId);
        if (user.HasNoValue)
        {
            return Unauthorized();
        }

        return Ok(UserResponse(user.Value.Id.ToString(), user.Value.Username, user.Value.Nickname, user.Value.IsSuperUser));
    }

    // The deployment's capabilities travel with the user, so the UI never has to infer them from a mode name.
    private object UserResponse(string? id, string? username, string? nickname, bool isSuperUser) => new
    {
        id,
        username,
        nickname,
        isSuperUser,
        userManagementEnabled = _deploymentStrategy.IsUserManagementEnabled,
        hasDefaultHomeAssistantConnection = _deploymentStrategy.DefaultHomeAssistant is not null
    };
}

public record LoginRequest(string Username, string Password);
public record RegisterRequest(string Username, string Password);

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using EPaperDashboard.Authentication;
using EPaperDashboard.Authorization;
using EPaperDashboard.Guards;
using EPaperDashboard.Services;
using EPaperDashboard.Models;
using System.Security.Claims;

namespace EPaperDashboard.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
[RequiresUserManagement]
public class UsersApiController(UserService userService, ILogger<UsersApiController>? logger = null) : BaseApiController
{
    [HttpPost("change-nickname")]
    public IActionResult ChangeNickname([FromBody] ChangeNicknameRequest request)
    {
        if (userService.TryChangeNickname(CurrentUserId, request.NewNickname))
        {
            return Ok(new { message = string.IsNullOrWhiteSpace(request.NewNickname) ? "Nickname cleared." : "Nickname changed successfully." });
        }

        return BadRequest(new { message = "Nickname change failed." });
    }

    [HttpPost("change-password")]
    [AllowedDuringPasswordChange]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword) || 
            string.IsNullOrWhiteSpace(request.NewPassword) || 
            string.IsNullOrWhiteSpace(request.ConfirmNewPassword))
        {
            return BadRequest(new { message = "All password fields are required." });
        }

        if (!string.Equals(request.NewPassword, request.ConfirmNewPassword, StringComparison.Ordinal))
        {
            return BadRequest(new { message = "New password and confirmation do not match." });
        }

        if (userService.TryChangePassword(CurrentUserId, request.CurrentPassword, request.NewPassword))
        {
            // The new security stamp ends every other session; reissue this one so the user stays signed in here.
            var user = userService.GetUserById(CurrentUserId);
            if (user.HasValue)
            {
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, UserClaims.Create(user.Value));
            }

            return Ok(new { message = "Password changed successfully." });
        }

        return BadRequest(new { message = "Password change failed." });
    }

    [HttpDelete("delete-profile")]
    public IActionResult DeleteProfile()
    {
        if (userService.TryDeleteUser(CurrentUserId))
        {
            return Ok(new { message = "Profile deleted successfully." });
        }

        return BadRequest(new { message = "Failed to delete profile." });
    }

    [HttpGet("all")]
    [Authorize(Policy = "SuperUserOnly")]
    public IActionResult GetAllUsers()
    {
        var users = userService.GetAllUsers();
        return Ok(users.Select(u => new { u.Id, u.Username, u.Nickname, u.IsSuperUser, u.IsSuspended, u.MustChangePassword, RenderingAllowed = u.RenderingAllowed != false, u.CreatedAt }));
    }

    [HttpPost("add")]
    [Authorize(Policy = "SuperUserOnly")]
    public IActionResult AddUser([FromBody] AddUserRequest request)
    {
        var error = UserService.ValidateCredentials(request.Username, request.Password);
        if (error is not null) return BadRequest(new { message = error });

        if (userService.TryCreateUser(request.Username.Trim(), request.Password, request.MustChangePassword))
        {
            Audit("added", request.Username.Trim());
            return Ok(new { message = "User added successfully." });
        }

        return BadRequest(new { message = "User already exists." });
    }

    [HttpPut("{id}/role")]
    [Authorize(Policy = "SuperUserOnly")]
    public IActionResult SetRole(string id, [FromBody] SetRoleRequest request) =>
        Administer(id, request.IsSuperUser ? "promoted" : "demoted",
            target => userService.SetSuperUser(CurrentUserId, target, request.IsSuperUser),
            request.IsSuperUser ? "User promoted to administrator." : "Administrator rights removed.");

    [HttpPut("{id}/suspension")]
    [Authorize(Policy = "SuperUserOnly")]
    public IActionResult SetSuspension(string id, [FromBody] SetSuspensionRequest request) =>
        Administer(id, request.Suspended ? "suspended" : "reinstated",
            target => userService.SetSuspended(CurrentUserId, target, request.Suspended),
            request.Suspended ? "User suspended." : "User reinstated.");

    [HttpPut("{id}/rendering")]
    [Authorize(Policy = "SuperUserOnly")]
    public IActionResult SetRenderingAccess(string id, [FromBody] SetRenderingAccessRequest request) =>
        Administer(id, request.Allowed ? "allowed rendering for" : "revoked rendering for",
            target => userService.SetRenderingAllowed(CurrentUserId, target, request.Allowed),
            request.Allowed ? "Rendering allowed." : "Rendering turned off for this user.");

    [HttpPost("{id}/reset-password")]
    [Authorize(Policy = "SuperUserOnly")]
    public IActionResult ResetPassword(string id)
    {
        string? temporaryPassword = null;
        var response = Administer(id, "reset the password of", target =>
        {
            var (result, password) = userService.ResetPassword(CurrentUserId, target);
            temporaryPassword = password;
            return result;
        }, "Password reset.");

        return temporaryPassword is null
            ? response
            : Ok(new { message = "Password reset. Share the temporary password with the user; it is shown only once.", temporaryPassword });
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "SuperUserOnly")]
    public IActionResult DeleteUser(string id) =>
        Administer(id, "deleted", target => userService.DeleteUserAsAdmin(CurrentUserId, target), "User deleted successfully.");

    private IActionResult Administer(string id, string action, Func<UserId, AdminActionResult> change, string successMessage)
    {
        if (!UserId.TryParse(id, out var target))
        {
            return BadRequest(new { message = "Invalid user id." });
        }

        var result = change(target);
        if (result == AdminActionResult.Done)
        {
            Audit(action, target.ToString());
        }

        return result switch
        {
            AdminActionResult.Done => Ok(new { message = successMessage }),
            AdminActionResult.NotFound => NotFound(new { message = "User not found." }),
            AdminActionResult.SelfNotAllowed => BadRequest(new { message = "Use your profile to change your own account." }),
            _ => Conflict(new { message = "At least one active administrator must remain." })
        };
    }

    private void Audit(string action, string target) =>
        logger?.LogInformation("Administrator {Actor} {Action} user {Target}", CurrentUserId, action, target);
}

public record ChangeNicknameRequest(string NewNickname);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword, string ConfirmNewPassword);
public record AddUserRequest(string Username, string Password, bool MustChangePassword = true);
public record SetRoleRequest(bool IsSuperUser);
public record SetSuspensionRequest(bool Suspended);
public record SetRenderingAccessRequest(bool Allowed);

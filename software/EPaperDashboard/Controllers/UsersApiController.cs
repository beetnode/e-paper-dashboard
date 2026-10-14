using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using EPaperDashboard.Authentication;
using EPaperDashboard.Guards;
using EPaperDashboard.Services;
using EPaperDashboard.Models;
using System.Security.Claims;

namespace EPaperDashboard.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
[RequiresUserManagement]
public class UsersApiController(UserService userService) : BaseApiController
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
        return Ok(users.Select(u => new { u.Id, u.Username, u.Nickname, u.IsSuperUser }));
    }

    [HttpPost("add")]
    [Authorize(Policy = "SuperUserOnly")]
    public IActionResult AddUser([FromBody] AddUserRequest request)
    {
        var error = UserService.ValidateCredentials(request.Username, request.Password);
        if (error is not null) return BadRequest(new { message = error });

        if (userService.TryCreateUser(request.Username.Trim(), request.Password))
        {
            return Ok(new { message = "User added successfully." });
        }

        return BadRequest(new { message = "User already exists." });
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "SuperUserOnly")]
    public IActionResult DeleteUser(string id)
    {
        if (!UserId.TryParse(id, out var userId))
        {
            return BadRequest(new { message = "Invalid user id." });
        }

        if (userService.TryDeleteUser(userId))
        {
            return Ok(new { message = "User deleted successfully." });
        }

        return BadRequest(new { message = "Cannot delete user." });
    }
}

public record ChangeNicknameRequest(string NewNickname);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword, string ConfirmNewPassword);
public record AddUserRequest(string Username, string Password);

using Finance.Contracts.Inputs;
using Finance.Contracts.Outputs;
using FinanceApi.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinanceApi.Controllers;

[Route("api/[controller]")]
[ApiController]
[Produces("application/json")]
public class AuthController(
    SignInManager<IdentityUser> signInManager, UserManager<IdentityUser> userManager, DbTicketStore sessions,
    UnknownUsernames unknownUsernames) : ControllerBase
{
    /// <summary>Signs in and sets the session cookie.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(SignInGuard.PolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<CurrentUserDto>> Login(LoginInputDto login)
    {
        var user = await userManager.FindByNameAsync(login.Username);
        if (user is null)
        {
            // Answered as a wrong password is, as slowly, and locked out after as many: no answer tells which
            // usernames exist.
            return unknownUsernames.Fail(userManager, login.Username, login.Password) ? LockedOut() : SignInFailed();
        }
        var result = await signInManager.PasswordSignInAsync(user, login.Password, login.RememberMe, lockoutOnFailure: true);
        // Usernames match case-insensitively; answer with the stored spelling.
        if (result.Succeeded) return new CurrentUserDto { Username = user.UserName! };
        return result.IsLockedOut ? LockedOut() : SignInFailed();
    }

    private ObjectResult LockedOut() => Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many attempts",
        detail: "Too many failed sign-in attempts. Try again in 15 minutes.");

    private ObjectResult SignInFailed() => Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Sign-in failed",
        detail: "Incorrect username or password.");

    /// <summary>Ends the session (on the server too: a copy of the cookie stops working). Harmless when already signed out.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }

    /// <summary>Who is signed in; 401 when nobody is.</summary>
    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<CurrentUserDto> Me()
    {
        return new CurrentUserDto { Username = User.Identity!.Name! };
    }

    [HttpPost("change-password")]
    [EnableRateLimiting(SignInGuard.PolicyName)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword(ChangePasswordInputDto change)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        // A wrong current password counts towards the lockout as at sign-in, or a session left open somewhere would be a
        // way to guess the password without limit.
        var check = await signInManager.CheckPasswordSignInAsync(user, change.CurrentPassword, lockoutOnFailure: true);
        if (!check.Succeeded)
        {
            ModelState.AddModelError(nameof(change.CurrentPassword), check.IsLockedOut
                ? "Too many wrong passwords. Try again in 15 minutes."
                : "Incorrect password.");
            return ValidationProblem(ModelState);
        }

        var result = await userManager.ChangePasswordAsync(user, change.CurrentPassword, change.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                var field = error.Code == "PasswordMismatch" ? nameof(change.CurrentPassword) : nameof(change.NewPassword);
                ModelState.AddModelError(field, error.Description);
            }
            return ValidationProblem(ModelState);
        }

        // Whoever else was signed in (possibly with the old password) is signed out now; this session goes on.
        await sessions.RemoveOtherSessionsAsync(User);
        await signInManager.RefreshSignInAsync(user);
        return NoContent();
    }

    /// <summary>Ends every other session of this account (other browsers and devices); this one stays signed in.</summary>
    [HttpPost("sign-out-others")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<SignedOutSessionsDto>> SignOutOthers()
    {
        return new SignedOutSessionsDto { Count = await sessions.RemoveOtherSessionsAsync(User) };
    }
}

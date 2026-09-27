using Finance.Contracts.Inputs;
using Finance.Contracts.Outputs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FinanceApi.Controllers;

[Route("api/[controller]")]
[ApiController]
[Produces("application/json")]
public class AuthController(SignInManager<IdentityUser> signInManager, UserManager<IdentityUser> userManager) : ControllerBase
{
    /// <summary>Signs in and sets the session cookie.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<CurrentUserDto>> Login(LoginInputDto login)
    {
        var result = await signInManager.PasswordSignInAsync(login.Username, login.Password, login.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            // Usernames match case-insensitively; answer with the stored spelling.
            var user = await userManager.FindByNameAsync(login.Username);
            return new CurrentUserDto { Username = user!.UserName! };
        }
        if (result.IsLockedOut)
        {
            return Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many attempts",
                detail: "Too many failed sign-in attempts. Try again in 15 minutes.");
        }
        // Same answer for an unknown user and a wrong password.
        return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Sign-in failed",
            detail: "Incorrect username or password.");
    }

    /// <summary>Ends the session. Harmless when already signed out.</summary>
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
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword(ChangePasswordInputDto change)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
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

        // New security stamp: keep this session, other sessions end when they are next validated.
        await signInManager.RefreshSignInAsync(user);
        return NoContent();
    }
}

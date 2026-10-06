using Finance.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinanceApi.Auth;

public static class AuthSetup
{
    public const string CookieName = "finance_session";
    private const string SessionOrTokenScheme = "SessionOrToken";

    /// <summary>
    /// Username/password sign-in with ASP.NET Core Identity and an HttpOnly session cookie. The frontend is served
    /// from the same origin as the API (nginx in production, the dev-server proxy locally), so SameSite=Strict
    /// works and also stops other sites from making requests with the cookie. Every endpoint requires a signed-in
    /// user unless it opts out with [AllowAnonymous].
    /// </summary>
    public static IServiceCollection AddLogin(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .Validate(options => options.IsValid(),
                $"{AuthOptions.SectionName}:ReadOnlyToken must be at least {AuthOptions.MinimumTokenLength} characters.")
            .ValidateOnStart();

        // A request with a bearer token is the read-only token's; any other is the session cookie's.
        var authentication = services.AddAuthentication(SessionOrTokenScheme);
        authentication.AddIdentityCookies();
        authentication.AddScheme<AuthenticationSchemeOptions, ReadOnlyTokenHandler>(ReadOnlyTokenHandler.SchemeName, _ => { });
        authentication.AddPolicyScheme(SessionOrTokenScheme, null, options => options.ForwardDefaultSelector = context =>
            ReadOnlyTokenHandler.HasBearerToken(context.Request) ? ReadOnlyTokenHandler.SchemeName : IdentityConstants.ApplicationScheme);
        services.AddIdentityCore<IdentityUser>(options =>
            {
                // Length over composition rules (NIST SP 800-63B).
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<FinanceDbContext>()
            .AddSignInManager()
            // Needed by the password reset of Auth:InitialUser:ResetPassword.
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;
            // An API answers with status codes; the frontend shows its own login page.
            options.Events.OnRedirectToLogin = context => StatusCode(context.Response, StatusCodes.Status401Unauthorized);
            options.Events.OnRedirectToAccessDenied = context => StatusCode(context.Response, StatusCodes.Status403Forbidden);
        });

        // Sessions live in the database, so signing out, a password change or "sign out other devices" really
        // ends them; the cookie only carries the session key.
        services.AddSingleton<DbTicketStore>();
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<DbTicketStore>((options, store) => options.SessionStore = store);

        services.AddDataProtection()
            .SetApplicationName("PersonalFinanceInsights")
            .PersistKeysToDbContext<FinanceDbContext>();

        // The session cookie, or the read-only token for requests that don't change anything.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireAssertion(ReadOnlyTokenHandler.MayProceed)
                .Build());

        return services;
    }

    private static Task StatusCode(HttpResponse response, int statusCode)
    {
        response.StatusCode = statusCode;
        return Task.CompletedTask;
    }

    /// <summary>Creates (or, when asked, resets) the account configured under Auth:InitialUser.</summary>
    public static async Task EnsureInitialUserAsync(this IServiceProvider services, ILogger logger)
    {
        await using var scope = services.CreateAsyncScope();
        var initial = scope.ServiceProvider.GetRequiredService<IOptions<AuthOptions>>().Value.InitialUser;
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

        if (string.IsNullOrWhiteSpace(initial.Username))
        {
            await WarnIfNoAccountsAsync(users, logger);
            return;
        }
        if (string.IsNullOrEmpty(initial.Password))
        {
            throw new InvalidOperationException("Auth:InitialUser:Password must be set together with Auth:InitialUser:Username.");
        }

        var user = await users.FindByNameAsync(initial.Username);
        if (user is null)
        {
            Check(await users.CreateAsync(new IdentityUser(initial.Username), initial.Password), "create the initial user");
            logger.LogInformation("Created the account {Username}", initial.Username);
        }
        else if (initial.ResetPassword)
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            Check(await users.ResetPasswordAsync(user, token, initial.Password), "reset the password");
            await users.SetLockoutEndDateAsync(user, null);
            await users.ResetAccessFailedCountAsync(user);
            logger.LogWarning("Reset the password of {Username}; remove Auth:InitialUser:ResetPassword now", initial.Username);
        }
    }

    private static async Task WarnIfNoAccountsAsync(UserManager<IdentityUser> users, ILogger logger)
    {
        try
        {
            if (!await users.Users.AnyAsync())
            {
                logger.LogWarning("No accounts exist, so nobody can sign in. Set Auth__InitialUser__Username and " +
                                  "Auth__InitialUser__Password to create one.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Only a hint: the database may not be migrated yet (Database:MigrateOnStartup off).
            logger.LogDebug(ex, "Could not check for accounts");
        }
    }

    private static void Check(IdentityResult result, string action)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not {action} (Auth:InitialUser): {string.Join(" ", result.Errors.Select(e => e.Description))}");
        }
    }
}

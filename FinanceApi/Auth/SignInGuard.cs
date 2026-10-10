using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace FinanceApi.Auth;

/// <summary>
/// Limits on guessing passwords, on top of the account lockout: how often one address may try, and the same lockout
/// for a username nobody has, so the answers never tell which usernames exist.
/// </summary>
public static class SignInGuard
{
    /// <summary>The rate-limiting policy of the endpoints that check a password.</summary>
    public const string PolicyName = "password-attempts";

    /// <summary>Attempts one address may make a minute, right or wrong.</summary>
    public const int AttemptsPerMinute = 10;

    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutTime = TimeSpan.FromMinutes(15);

    public static IServiceCollection AddSignInGuard(this IServiceCollection services)
    {
        services.AddSingleton<UnknownUsernames>();
        services.AddRateLimiter(options =>
        {
            // By the client's address as the trusted proxies report it (ReverseProxy). Behind a single proxy that
            // doesn't forward it, every client shares one allowance: still far more than a person signing in needs.
            options.AddPolicy(PolicyName, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = AttemptsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                }
                var problems = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problems.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many attempts",
                        Detail = "Too many sign-in attempts from this device. Try again in a minute.",
                    },
                });
            };
        });
        return services;
    }
}

/// <summary>
/// Failed sign-ins with a username no account has, counted and locked out as Identity does an account's, and checked
/// against a password hash as long to compute: the answer and the time it takes are an existing account's.
/// </summary>
public sealed class UnknownUsernames(TimeProvider timeProvider) : IDisposable
{
    private sealed class Attempts
    {
        public int Failures;
        public DateTimeOffset LockedUntil;
    }

    // Bounded, so a flood of made-up names can't fill the memory; the oldest go first.
    private readonly MemoryCache _attempts = new(new MemoryCacheOptions { SizeLimit = 10_000 });
    private string? _decoyHash;

    /// <summary>Records a failed sign-in for <paramref name="username"/>, which no account has.</summary>
    /// <returns>True when that name is now locked out, as an account would be.</returns>
    public bool Fail(UserManager<IdentityUser> users, string username, string password)
    {
        var decoy = _decoyHash ??= users.PasswordHasher.HashPassword(new IdentityUser(), Guid.NewGuid().ToString());
        users.PasswordHasher.VerifyHashedPassword(new IdentityUser(), decoy, password);

        var key = users.NormalizeName(username) ?? username;
        var attempts = _attempts.GetOrCreate(key, entry =>
        {
            entry.Size = 1;
            entry.SlidingExpiration = TimeSpan.FromDays(1);
            return new Attempts();
        })!;
        lock (attempts)
        {
            var now = timeProvider.GetUtcNow();
            if (attempts.LockedUntil > now) return true;
            // As Identity does: the count starts over once the lockout is set.
            if (++attempts.Failures < SignInGuard.MaxFailedAttempts) return false;
            attempts.Failures = 0;
            attempts.LockedUntil = now + SignInGuard.LockoutTime;
            return true;
        }
    }

    public void Dispose() => _attempts.Dispose();
}

using System.Security.Claims;
using System.Security.Cryptography;
using Finance.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace FinanceApi.Auth;

/// <summary>
/// Keeps sign-ins in the database (<see cref="UserSession"/>) instead of in the cookie, so they can be ended:
/// signing out deletes the session, and a copy of the cookie stops working with it. A singleton, as the cookie
/// handler expects; each call uses its own database context.
/// </summary>
public class DbTicketStore(IServiceScopeFactory scopes, TimeProvider timeProvider) : ITicketStore
{
    /// <summary>
    /// The session key on the signed-in user. The cookie handler reads its own copy from the cookie and then
    /// works with the ticket stored here, so the store adds the key when it hands a ticket back.
    /// </summary>
    private const string SessionKeyClaim = "finance:session";

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var now = timeProvider.GetUtcNow();
        var session = new UserSession
        {
            // The cookie is encrypted anyway; a random key keeps sessions unguessable even from a leaked database.
            Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            UserId = UserId(ticket.Principal),
            Ticket = TicketSerializer.Default.Serialize(ticket),
            CreatedUtc = now,
            ExpiresUtc = ticket.Properties.ExpiresUtc,
        };

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        // Sign-ins are rare, so this is a good moment to clear the sessions that expired unused.
        db.UserSessions.RemoveRange(await db.UserSessions.Where(s => s.ExpiresUtc < now).ToListAsync());
        db.UserSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var session = await db.UserSessions.FindAsync(key);
        // Ended meanwhile (signed out elsewhere): stays ended.
        if (session is null) return;

        session.Ticket = TicketSerializer.Default.Serialize(ticket);
        session.ExpiresUtc = ticket.Properties.ExpiresUtc;
        await db.SaveChangesAsync();
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var session = await db.UserSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == key);
        if (session is null || session.ExpiresUtc < timeProvider.GetUtcNow()) return null;

        var ticket = TicketSerializer.Default.Deserialize(session.Ticket);
        var identity = ticket?.Principal.Identities.FirstOrDefault();
        if (identity is not null && !identity.HasClaim(c => c.Type == SessionKeyClaim))
        {
            identity.AddClaim(new Claim(SessionKeyClaim, key));
        }
        return ticket;
    }

    public async Task RemoveAsync(string key)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var session = await db.UserSessions.FindAsync(key);
        if (session is null) return;
        db.UserSessions.Remove(session);
        await db.SaveChangesAsync();
    }

    /// <summary>Ends every session of the user except the one making this request; returns how many ended.</summary>
    public async Task<int> RemoveOtherSessionsAsync(ClaimsPrincipal current)
    {
        var userId = UserId(current);
        var currentKey = current.FindFirstValue(SessionKeyClaim);

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var others = await db.UserSessions.Where(s => s.UserId == userId && s.Id != currentKey).ToListAsync();
        db.UserSessions.RemoveRange(others);
        await db.SaveChangesAsync();
        return others.Count;
    }

    private static string UserId(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("A sign-in without a user id can't be stored.");
}

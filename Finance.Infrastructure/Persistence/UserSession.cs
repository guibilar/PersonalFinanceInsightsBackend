namespace Finance.Infrastructure.Persistence;

/// <summary>
/// A signed-in session. The session cookie only carries <see cref="Id"/> (encrypted); the sign-in itself lives
/// here, so deleting the row ends the session everywhere the cookie was copied to.
/// </summary>
public class UserSession
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;

    /// <summary>The serialized authentication ticket (the user's claims and the cookie's properties).</summary>
    public byte[] Ticket { get; set; } = [];

    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? ExpiresUtc { get; set; }
}

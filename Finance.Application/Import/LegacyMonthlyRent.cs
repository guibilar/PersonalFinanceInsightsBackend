namespace Finance.Application.Import;

/// <summary>
/// Rent used to be generated: the real payment was dropped and a fixed "Monthly Rent" row was added on the 1st of
/// every month a chequing statement covered. Real payments are imported now; these are the rows left from before.
/// A migration locked their category, and an import that brings a month's real rent payment hides that month's row.
/// </summary>
public static class LegacyMonthlyRent
{
    public const string Description = "Monthly Rent";
}

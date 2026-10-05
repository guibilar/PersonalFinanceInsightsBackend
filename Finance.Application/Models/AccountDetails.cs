using Finance.Domain.Enums;

namespace Finance.Application.Models;

/// <summary>What can be set on an account besides its kind. Null leaves a field as it is (on a new account: its default).</summary>
/// <param name="ContributionRoom">The plan's room for <paramref name="ContributionRoomYear"/> (this year when not given).</param>
public record AccountDetails(
    string Name,
    StatementFormat? Format = null,
    RegisteredPlan? Plan = null,
    decimal? ContributionRoom = null,
    int? ContributionRoomYear = null);

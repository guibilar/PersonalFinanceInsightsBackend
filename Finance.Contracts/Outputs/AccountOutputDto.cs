using Finance.Domain.Enums;

namespace Finance.Contracts.Outputs;

public class AccountOutputDto
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public AccountType Type { get; set; }
    public StatementFormat Format { get; set; }

    /// <summary>Including deleted (hidden) ones; an account can only be deleted when this is 0.</summary>
    public int TransactionCount { get; set; }

    public RegisteredPlan Plan { get; set; }

    /// <summary>The plan's contribution room for <see cref="ContributionRoomYear"/>, as entered.</summary>
    public decimal? ContributionRoom { get; set; }

    public int? ContributionRoomYear { get; set; }

    /// <summary>This year's transfers in: for a registered plan, its contributions.</summary>
    public decimal DepositedThisYear { get; set; }

    /// <summary>This year's transfers out: for a TFSA, room again next January.</summary>
    public decimal WithdrawnThisYear { get; set; }

    /// <summary>Room left this year: the room entered for this year less this year's deposits. Null when no room was entered for this year.</summary>
    public decimal? RoomLeft { get; set; }

    /// <summary>The latest balance entered.</summary>
    public AccountBalanceOutputDto? Balance { get; set; }
}

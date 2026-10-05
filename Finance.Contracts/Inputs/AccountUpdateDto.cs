using System.ComponentModel.DataAnnotations;
using Finance.Domain.Enums;

namespace Finance.Contracts.Inputs;

public class AccountUpdateDto
{
    /// <summary>Stored trimmed; unique ignoring case.</summary>
    [Required(AllowEmptyStrings = false), MaxLength(TextLimits.AccountName)]
    public string Name { get; set; } = string.Empty;

    /// <summary>The layout of the bank's statement files. Unchanged when omitted.</summary>
    [EnumDataType(typeof(StatementFormat))]
    public StatementFormat? Format { get; set; }

    /// <summary>The registered plan (TFSA, RRSP, FHSA) of a savings or investment account; None clears it and its room. Unchanged when omitted.</summary>
    [EnumDataType(typeof(RegisteredPlan))]
    public RegisteredPlan? Plan { get; set; }

    /// <summary>The plan's contribution room for <see cref="ContributionRoomYear"/>. Unchanged when omitted.</summary>
    [Range(0, 10_000_000)]
    public decimal? ContributionRoom { get; set; }

    /// <summary>The year <see cref="ContributionRoom"/> is for; this year when omitted.</summary>
    public int? ContributionRoomYear { get; set; }
}

using System.Text.RegularExpressions;

namespace Finance.Domain.Helpers;

/// <summary>
/// Text as rules compare it: upper case, with runs of spaces as one. Banks pad descriptions unevenly
/// ("TIM  HORTONS #55"), while keywords are typed, or suggested from a merchant name, with single spaces.
/// </summary>
public static partial class RuleText
{
    public static string Of(string text) => WhitespaceRun().Replace(text.Trim(), " ").ToUpperInvariant();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}

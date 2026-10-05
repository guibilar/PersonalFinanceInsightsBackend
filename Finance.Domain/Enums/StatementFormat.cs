namespace Finance.Domain.Enums;

/// <summary>The layout of the statement files an account's bank exports; decides how they are read.</summary>
public enum StatementFormat
{
    /// <summary>CIBC: no header; date, description, money out, money in (and the card number on card exports).</summary>
    Cibc,

    /// <summary>Neo Financial: a header line, and one signed amount (negative is money out).</summary>
    Neo,

    /// <summary>The bank offers no export: transactions are added by hand, and statement files are refused.</summary>
    Manual,

    /// <summary>
    /// Wealthsimple's monthly PDF statements (TFSA, RRSP, FHSA, non-registered): the cash activity, each row with a
    /// statement code (CONT, BUY, DIV, …), and the portfolio's market value at the end of the month.
    /// </summary>
    Wealthsimple
}

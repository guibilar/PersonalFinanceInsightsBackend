namespace Finance.Domain.Enums;

/// <summary>The layout of the statement files an account's bank exports; decides how they are read.</summary>
public enum StatementFormat
{
    /// <summary>CIBC: no header; date, description, money out, money in (and the card number on card exports).</summary>
    Cibc,

    /// <summary>Neo Financial: a header line, and one signed amount (negative is money out).</summary>
    Neo
}

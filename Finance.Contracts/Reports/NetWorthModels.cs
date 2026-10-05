using Finance.Domain.Enums;

namespace Finance.Contracts.Reports;

/// <summary>
/// What everything is worth: each account's balance today and at the end of each of the last twelve months, from the
/// balances entered and the transactions since (or, for a bank account or card, before) them.
/// </summary>
public class NetWorthReport
{
    public decimal Assets { get; set; }

    /// <summary>What the cards owe, positive.</summary>
    public decimal Liabilities { get; set; }

    public decimal NetWorth { get; set; }

    /// <summary>Every account, by kind then name; one without a balance entered has no <see cref="NetWorthAccount.Balance"/>.</summary>
    public List<NetWorthAccount> Accounts { get; set; } = [];

    /// <summary>The last twelve months, oldest first; the current one is as of today. Months nothing is known for are left out.</summary>
    public List<NetWorthMonth> Months { get; set; } = [];
}

public class NetWorthAccount
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public AccountType Type { get; set; }
    public RegisteredPlan Plan { get; set; }

    /// <summary>Today's balance (for a card, what it owes); null until a balance is entered.</summary>
    public decimal? Balance { get; set; }

    /// <summary>The day of the latest balance entered.</summary>
    public DateTime? BalanceDate { get; set; }

    /// <summary>Transactions since <see cref="BalanceDate"/> moved it: an investment's market value since isn't in it.</summary>
    public bool MovedSince { get; set; }

    /// <summary>A credit card: its balance is owed.</summary>
    public bool IsLiability { get; set; }
}

public class NetWorthMonth
{
    /// <summary>"Sep 2026".</summary>
    public required string Month { get; set; }

    /// <summary>The month's last day, or today for the current month.</summary>
    public DateTime Date { get; set; }

    public decimal Assets { get; set; }
    public decimal Liabilities { get; set; }
    public decimal NetWorth { get; set; }

    /// <summary>How many accounts were known then: fewer than today's means an account's history starts later.</summary>
    public int AccountsCounted { get; set; }
}

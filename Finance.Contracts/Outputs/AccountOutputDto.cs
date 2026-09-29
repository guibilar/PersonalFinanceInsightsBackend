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
}

using Finance.Domain.Entities;

namespace Finance.Application.Models;

public record AccountSummary(Account Account, int TransactionCount);

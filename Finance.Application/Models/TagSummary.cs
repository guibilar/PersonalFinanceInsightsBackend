using Finance.Domain.Entities;

namespace Finance.Application.Models;

/// <summary>A tag, and how many transactions have it.</summary>
public record TagSummary(Tag Tag, int TransactionCount);

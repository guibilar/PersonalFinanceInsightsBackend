using System.ComponentModel.DataAnnotations;
using Finance.Domain.Enums;

namespace Finance.Contracts.Inputs;

public class StatementImportDto
{
    /// <summary>Which account the statement belongs to; decides how its rows are read.</summary>
    [Required]
    public AccountType? Account { get; set; }
}

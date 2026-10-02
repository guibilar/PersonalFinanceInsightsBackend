using System.ComponentModel.DataAnnotations;

namespace Finance.Contracts.Inputs;

/// <summary>A money amount in whole cents: 12.5 and 12.50 pass, 12.505 doesn't (no bank statement has it).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CentsAttribute() : ValidationAttribute("The {0} field can have at most 2 decimal places.")
{
    public override bool IsValid(object? value) => value is not decimal amount || decimal.Round(amount, 2) == amount;
}

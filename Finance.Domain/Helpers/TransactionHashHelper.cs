using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Finance.Domain.Helpers;

public static class TransactionHashHelper
{
    /// <summary>
    /// Identifies a transaction for de-duplication across imports.
    /// <paramref name="occurrence"/> numbers identical rows (same day, amount and description) within one
    /// statement, so two identical purchases on the same day are both kept. The first occurrence keeps the
    /// original un-suffixed format, which is what every hash stored before this change used.
    /// </summary>
    public static string GenerateTransactionHash(decimal amount, DateTime date, string description, int occurrence = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(occurrence, 1);

        var input = string.Create(CultureInfo.InvariantCulture,
            $"{amount:F2}{date:yyyy-MM-dd}{description.Trim().ToLowerInvariant()}");
        if (occurrence > 1)
        {
            input += $"#{occurrence}";
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}

using System.Security.Cryptography;
using System.Text;

namespace FinanceDto.Helper;

public static class TransactionHashHelper
{
    public static string GenerateTransactionHash(decimal amount, DateTime date, string description)
    {
        var input = $"{amount:F2}{date:yyyy-MM-dd}{description?.Trim().ToLowerInvariant()}";
        var inputBytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = SHA256.HashData(inputBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
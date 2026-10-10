using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Finance.Domain.Exceptions;

namespace Finance.Infrastructure.Import;

/// <summary>
/// Reads a statement file as text: UTF-8 (with or without a byte order mark), or as Windows-1252 when the file is not
/// valid UTF-8 (as saved by Excel on Windows).
/// </summary>
internal static partial class StatementText
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding Windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

    /// <summary>
    /// A row's day, as the banks write it: "2026-01-05", with or without a time. Another layout (a file re-saved by a
    /// spreadsheet, "05/01/2026") is refused rather than guessed: read as month/day it would swap days and months, and
    /// drop the rows past the 12th.
    /// </summary>
    /// <returns>False for a field that is no date at all (a blank or a heading), to skip that row.</returns>
    /// <exception cref="InvalidStatementException">A date in another layout.</exception>
    public static bool TryParseDay(string? text, out DateTime day)
    {
        day = default;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;
        if (DateTime.TryParseExact(trimmed, IsoLayouts, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            day = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
            return true;
        }
        if (OtherDateLayout().IsMatch(trimmed))
        {
            throw new InvalidStatementException(
                $"The date “{trimmed}” isn't written as the bank writes it (2026-01-05). Was the file opened and saved again " +
                "in a spreadsheet? Download it again and import that.");
        }
        return false;
    }

    // "05/01/2026", "5.1.26", "2026/01/05": a date, but not in the bank's layout.
    [GeneratedRegex(@"^\d{1,4}[/.-]\d{1,2}[/.-]\d{1,4}( \d{1,2}:\d{2}(:\d{2})?)?$")]
    private static partial Regex OtherDateLayout();

    private static readonly string[] IsoLayouts = ["yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm"];

    public static string Read(Stream statement)
    {
        using var buffer = new MemoryStream();
        statement.CopyTo(buffer);
        var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
        if (bytes.StartsWith(Encoding.UTF8.Preamble)) bytes = bytes[Encoding.UTF8.Preamble.Length..];

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Plain UTF-8 decoding would turn accented characters into U+FFFD instead.
            return Windows1252.GetString(bytes);
        }
    }
}

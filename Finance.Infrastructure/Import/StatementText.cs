using System.Text;

namespace Finance.Infrastructure.Import;

/// <summary>
/// Reads a statement file as text: UTF-8 (with or without a byte order mark), or as Windows-1252 when the file is not
/// valid UTF-8 (as saved by Excel on Windows).
/// </summary>
internal static class StatementText
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding Windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

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

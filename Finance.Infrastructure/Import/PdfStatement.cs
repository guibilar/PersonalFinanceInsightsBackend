using Finance.Domain.Exceptions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Finance.Infrastructure.Import;

/// <summary>
/// What every bank's PDF statement reader needs: the words of each page as lines, and the check that the rows read take
/// the balance from the statement's opening figure to its closing one. Each bank's reader keeps only its own layout.
/// </summary>
internal static class PdfStatement
{
    /// <summary>Words this close vertically are on the same line (their baselines differ by fractions of a point).</summary>
    public const double SameLine = 2;

    /// <summary>A wrapped description continues within this distance below its row.</summary>
    public const double WrappedLine = 16;

    /// <summary>A line of words, left to right, at height <see cref="Y"/> (from the bottom of the page).</summary>
    public sealed record Line(double Y, List<Word> Words)
    {
        public string Text => string.Join(' ', Words.Select(w => w.Text));
        public double Left => Words[0].BoundingBox.Left;
    }

    public static bool IsPdf(ReadOnlySpan<byte> bytes) => bytes.StartsWith("%PDF-"u8);

    public static byte[] ReadAll(Stream statement)
    {
        using var buffer = new MemoryStream();
        statement.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Each page's lines, top to bottom.</summary>
    /// <exception cref="InvalidStatementException"><paramref name="unreadable"/>, when the file can't be opened as a PDF.</exception>
    public static List<List<Line>> Pages(byte[] pdf, string unreadable)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(pdf);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidStatementException(unreadable);
        }
        using (document)
        {
            return document.GetPages().Select(LinesOf).ToList();
        }
    }

    /// <summary>
    /// Each row must move the <paramref name="balanceName"/> by its <paramref name="change"/> to its own
    /// <paramref name="balance"/>, from <paramref name="opening"/>, and the last one must reach <paramref name="closing"/>:
    /// otherwise a row was misread, and the statement is refused rather than imported wrong.
    /// </summary>
    public static void Reconcile<TRow>(
        IEnumerable<TRow> rows,
        decimal opening,
        decimal closing,
        Func<TRow, decimal> change,
        Func<TRow, decimal> balance,
        Func<TRow, string> text,
        string balanceName)
    {
        var running = opening;
        foreach (var row in rows)
        {
            if (running + change(row) != balance(row))
            {
                throw new InvalidStatementException(
                    $"Couldn't read this statement reliably: \"{text(row)}\" doesn't take the {balanceName} from {running:N2} to {balance(row):N2}. Nothing was imported.");
            }
            running = balance(row);
        }
        if (running != closing)
        {
            throw new InvalidStatementException(
                $"Couldn't read this statement reliably: its rows end at {running:N2}, but the closing {balanceName} is {closing:N2}. Nothing was imported.");
        }
    }

    /// <summary>A page's words as lines, top to bottom, each left to right.</summary>
    private static List<Line> LinesOf(Page page)
    {
        var lines = new List<Line>();
        foreach (var word in page.GetWords().OrderByDescending(w => w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left))
        {
            var line = lines.LastOrDefault();
            if (line is not null && line.Y - word.BoundingBox.Bottom <= SameLine) line.Words.Add(word);
            else lines.Add(new Line(word.BoundingBox.Bottom, [word]));
        }
        foreach (var line in lines) line.Words.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
        return lines;
    }
}

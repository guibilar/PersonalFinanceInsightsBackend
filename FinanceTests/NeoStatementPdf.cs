using System.Globalization;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace FinanceTests;

/// <summary>
/// Builds a statement laid out as Neo's monthly PDFs are (made-up holder and figures): a summary page, then the
/// transactions table on the pages after it. Positions follow a real statement.
/// </summary>
internal static class NeoStatementPdf
{
    /// <summary>A table row; a description with a line break wraps onto a second line, as long ones do.</summary>
    public record Row(string Day, string Description, decimal Amount, decimal Balance);

    private static readonly CultureInfo Money = CultureInfo.InvariantCulture;

    public static byte[] Build(string period, decimal opening, decimal closing, Row[] table, Row[]? nextPage = null)
    {
        Row[][] tablePages = nextPage is null ? [table] : [table, nextPage];
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        void Header(PdfPageBuilder page)
        {
            page.AddText("Alex Sample", 9, new PdfPoint(20, 653), font);
            page.AddText("Neo Savings", 9, new PdfPoint(534, 653), font);
            page.AddText("100 Example St", 9, new PdfPoint(20, 635), font);
            page.AddText("Calgary, Alberta", 9, new PdfPoint(20, 618), font);
            page.AddText(period, 9, new PdfPoint(469, 618), font);
        }

        var summary = builder.AddPage(PageSize.Letter);
        Header(summary);
        summary.AddText("Neo Savings Account Summary", 12, new PdfPoint(20, 561), font);
        summary.AddText("Amount ($CAD)", 9, new PdfPoint(519, 528), font);
        summary.AddText("Opening balance", 9, new PdfPoint(20, 511), font);
        summary.AddText(opening.ToString("N2", Money), 9, new PdfPoint(552, 511), font);
        summary.AddText("Closing balance", 9, new PdfPoint(20, 444), font);
        summary.AddText(closing.ToString("N2", Money), 9, new PdfPoint(543, 444), font);
        summary.AddText("Neo Savings is provided by Neo Financial in trust.", 8, new PdfPoint(20, 136), font);
        summary.AddText("Page 1", 8, new PdfPoint(20, 46), font);

        foreach (var rows in tablePages)
        {
            var page = builder.AddPage(PageSize.Letter);
            Header(page);
            page.AddText("Transactions", 12, new PdfPoint(20, 561), font);
            page.AddText("Transaction", 8, new PdfPoint(20, 539), font);
            page.AddText("Posted", 8, new PdfPoint(85, 539), font);
            page.AddText("Date", 8, new PdfPoint(20, 524), font);
            page.AddText("Date", 8, new PdfPoint(85, 524), font);
            page.AddText("Description", 8, new PdfPoint(140, 524), font);
            page.AddText("Credits", 8, new PdfPoint(438, 524), font);
            page.AddText("Debits", 8, new PdfPoint(502, 524), font);
            page.AddText("Balance", 8, new PdfPoint(554, 524), font);
            var y = 488.0;
            foreach (var row in rows)
            {
                var lines = row.Description.Split('\n');
                page.AddText(row.Day, 9, new PdfPoint(20, y), font);
                page.AddText(row.Day, 9, new PdfPoint(85, y), font);
                page.AddText(lines[0], 9, new PdfPoint(140, y), font);
                page.AddText(row.Amount.ToString("N2", Money), 9, new PdfPoint(row.Amount < 0 ? 493 : 429, y), font);
                page.AddText(row.Balance.ToString("N2", Money), 9, new PdfPoint(547, y), font);
                foreach (var more in lines.Skip(1))
                {
                    y -= 11;
                    page.AddText(more, 9, new PdfPoint(140, y), font);
                }
                y -= 23;
            }
            page.AddText("Neo Savings is provided by Neo Financial in trust.", 8, new PdfPoint(20, 136), font);
            page.AddText("Contact Neo Financial", 8, new PdfPoint(20, 105), font);
        }
        return builder.Build();
    }
}

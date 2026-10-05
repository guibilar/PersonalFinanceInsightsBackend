using System.Globalization;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace FinanceTests;

/// <summary>
/// Builds a statement laid out as Wealthsimple's monthly PDFs are (made-up holder, account and figures): the header and
/// the portfolio and cash summaries on the first page, then the activity table. Positions follow a real statement; the table's small type keeps its columns apart.
/// </summary>
internal static class WealthsimpleStatementPdf
{
    /// <summary>An activity row; a description with a line break wraps onto a second line.</summary>
    public record Row(string Date, string Code, string Description, decimal Debit, decimal Credit, decimal Balance);

    public static string Dollars(decimal amount) =>
        (amount < 0 ? "-$" : "$") + Math.Abs(amount).ToString("N2", CultureInfo.InvariantCulture);

    public static byte[] Build(string period, decimal portfolio, decimal openingCash, decimal closingCash, Row[] activity, Row[]? nextPage = null)
    {
        Row[][] pages = nextPage is null ? [activity] : [activity, nextPage];
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        void Text(PdfPageBuilder page, string text, double x, double y, double size = 8) => page.AddText(text, size, new PdfPoint(x, y), font);

        var first = builder.AddPage(PageSize.A4);
        Text(first, "1/3", 553, 824);
        Text(first, "ORDER EXECUTION ONLY ACCOUNT", 224, 799);
        Text(first, "Account No.", 251, 762);
        Text(first, "Owner", 365, 762);
        Text(first, "Statement Period", 462, 762);
        Text(first, "ZZ00TEST0CAD", 243, 750);
        Text(first, "Alex Sample", 352, 750);
        Text(first, period, 451, 750);
        Text(first, "Self-directed TFSA Account", 325, 700);
        Text(first, "Alex Sample", 55, 671);
        Text(first, "100 Example Street", 55, 661);
        Text(first, "Cash", 222, 668);
        Text(first, Dollars(closingCash), 362, 668);
        Text(first, "Total Portfolio", 222, 624);
        Text(first, Dollars(portfolio), 362, 624);
        Text(first, "100.00", 414, 624);
        Text(first, Dollars(portfolio - 100), 475, 624);
        Text(first, "100.00", 532, 624);
        Text(first, "Portfolio Cash", 49, 581);
        // As on the real statement, the second column's labels share these lines.
        Text(first, "Last Statement Cash Balance", 49, 553);
        Text(first, Dollars(openingCash), 184, 553);
        Text(first, "Cash Paid In", 242, 553);
        Text(first, "Contributions:", 416, 553);
        Text(first, "Contributions (year to date):", 419, 543);
        Text(first, "$5,300.00", 524, 543);
        Text(first, "Closing Cash Balance", 49, 519);
        Text(first, Dollars(closingCash), 184, 519);
        Text(first, "Interest Earned", 303, 518);
        Text(first, "$0.00", 378, 518);

        foreach (var rows in pages)
        {
            var page = builder.AddPage(PageSize.A4);
            Text(page, "Activity - Current period", 49, 780);
            Text(page, "Date", 48, 763, 6);
            Text(page, "Transaction", 84, 763, 6);
            Text(page, "Description", 130, 763, 6);
            Text(page, "Debit ($)", 445, 763, 6);
            Text(page, "Credit ($)", 481, 763, 6);
            Text(page, "Balance ($)", 519, 763, 6);
            var y = 745.0;
            foreach (var row in rows)
            {
                var lines = row.Description.Split('\n');
                Text(page, row.Date, 48, y, 6);
                Text(page, row.Code, 84, y, 6);
                Text(page, lines[0], 130, y, 6);
                Text(page, Dollars(row.Debit), 445, y, 6);
                Text(page, Dollars(row.Credit), 481, y, 6);
                Text(page, Dollars(row.Balance), 519, y, 6);
                foreach (var more in lines.Skip(1))
                {
                    y -= 10;
                    Text(page, more, 130, y, 6);
                }
                y -= 18;
            }
        }

        var codes = builder.AddPage(PageSize.A4);
        Text(codes, "Information about Statement Codes", 51, 804);
        Text(codes, "BUY - Purchase of assets", 51, 781);
        Text(codes, "CONT - Contribution", 51, 734);
        return builder.Build();
    }
}

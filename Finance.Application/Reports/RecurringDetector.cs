using Finance.Contracts.Reports;
using Finance.Domain.Entities;

namespace Finance.Application.Reports;

/// <summary>A purchase, as the recurring-payment search sees it.</summary>
public record Charge(Guid Id, DateTime Date, decimal Amount, string Merchant, Category? Category);

/// <summary>A recurring payment found, with the charges that make it up.</summary>
public record DetectedPayment(RecurringPayment Payment, IReadOnlySet<Guid> Charges);

/// <summary>
/// Finds payments that come again and again: a merchant's charges at a steady interval (a week, two, a month, a
/// quarter, a year) for a steady amount. A merchant billing several of them (an app store) is looked at amount by amount.
/// </summary>
public static class RecurringDetector
{
    private sealed record Shape(RecurringCadence Cadence, double Days, double Tolerance, int MinCharges, decimal AmountTolerance, int GraceDays, decimal PerYear);

    // Intervals are matched within their tolerance (or twice the interval: one charge missing from a statement).
    // A short cadence needs steadier amounts and more charges, or weekly groceries would pass for a subscription.
    private static readonly Shape[] Shapes =
    [
        new(RecurringCadence.Weekly, 7, 2, 5, 0.15m, 3, 52),
        new(RecurringCadence.Biweekly, 14, 3, 4, 0.15m, 4, 26),
        new(RecurringCadence.Monthly, 30.44, 5, 3, 0.35m, 6, 12),
        new(RecurringCadence.Quarterly, 91.3, 12, 3, 0.35m, 12, 4),
        new(RecurringCadence.Yearly, 365.25, 20, 2, 0.35m, 20, 1),
    ];

    /// <summary>The share of intervals and of amounts that must fit; the rest are allowed to be off.</summary>
    private const double Steady = 0.75;

    /// <summary>A stopped payment is listed for this long after its last charge, then forgotten.</summary>
    private const int StoppedForDays = 365;

    public static List<DetectedPayment> Detect(IEnumerable<Charge> charges, DateTime today)
    {
        var found = new List<DetectedPayment>();
        foreach (var merchant in charges.Where(c => c.Amount > 0).GroupBy(c => c.Merchant))
        {
            var all = merchant.OrderBy(c => c.Date).ToList();
            if (TryDetect(all, today) is { } whole)
            {
                found.Add(whole);
                continue;
            }
            // Several subscriptions billed by one merchant interleave: each price on its own may be steady.
            found.AddRange(all.GroupBy(c => c.Amount)
                .Select(price => TryDetect(price.ToList(), today))
                .OfType<DetectedPayment>());
        }
        return found
            .Where(d => d.Payment.Status != RecurringStatus.Stopped || (today - d.Payment.LastDate).TotalDays <= StoppedForDays)
            .ToList();
    }

    private static DetectedPayment? TryDetect(List<Charge> charges, DateTime today)
    {
        if (charges.Count < 2) return null;
        var intervals = charges.Zip(charges.Skip(1), (a, b) => (b.Date - a.Date).TotalDays).ToList();
        var median = Median(intervals);
        var shape = Shapes.FirstOrDefault(s => Math.Abs(median - s.Days) <= s.Tolerance);
        if (shape is null || charges.Count < shape.MinCharges) return null;

        var regular = intervals.Count(i => Math.Abs(i - shape.Days) <= shape.Tolerance
                                           || Math.Abs(i - 2 * shape.Days) <= shape.Tolerance * 1.5);
        if (regular < Steady * intervals.Count) return null;

        var amounts = charges.Select(c => c.Amount).ToList();
        var typical = Median(amounts);
        var steadyAmounts = amounts.Count(a => Math.Abs(a - typical) <= shape.AmountTolerance * typical);
        if (steadyAmounts < Steady * amounts.Count) return null;

        var last = charges[^1];
        var (amount, variable, change) = Price(charges);
        var next = Next(last.Date, shape.Cadence);
        var status = today <= next.AddDays(shape.GraceDays) ? RecurringStatus.Active
            : today <= next.AddDays(shape.Days) ? RecurringStatus.Late
            : RecurringStatus.Stopped;
        var yearly = Math.Round(amount * shape.PerYear, 2);
        return new DetectedPayment(new RecurringPayment
        {
            Name = last.Merchant,
            Cadence = shape.Cadence,
            Status = status,
            Amount = amount,
            VariableAmount = variable,
            FirstDate = charges[0].Date,
            LastDate = last.Date,
            LastAmount = last.Amount,
            NextDate = next,
            Count = charges.Count,
            MonthlyCost = Math.Round(amount * shape.PerYear / 12, 2),
            YearlyCost = yearly,
            Category = last.Category is { } category
                ? new() { Id = category.Id, Name = category.Name, Color = category.Color }
                : null,
            PriceChange = change,
        }, charges.Select(c => c.Id).ToHashSet());
    }

    /// <summary>
    /// A fixed price (the latest), a fixed price that changed (the new one, and from what), or a varying amount
    /// (the average of the last three).
    /// </summary>
    private static (decimal Amount, bool Variable, PriceChange? Change) Price(List<Charge> charges)
    {
        var last = charges[^1];
        // The latest price and the charges that have come at it since it changed.
        var run = 1;
        while (run < charges.Count && Same(charges[^(run + 1)].Amount, last.Amount)) run++;
        var before = charges.SkipLast(run).TakeLast(3).Select(c => c.Amount).ToList();
        if (before.Count == 0) return (last.Amount, false, null);
        if (before.All(a => Same(a, before[^1])))
        {
            if (Math.Abs(last.Amount - before[^1]) >= Math.Max(0.5m, before[^1] * 0.02m))
            {
                return (last.Amount, false, new PriceChange { From = before[^1], To = last.Amount, Since = charges[^run].Date });
            }
            return (last.Amount, false, null);
        }
        // Settled on one price after varying.
        if (run >= 3) return (last.Amount, false, null);
        var recent = charges.TakeLast(3).Select(c => c.Amount).ToList();
        return (Math.Round(recent.Average(), 2), true, null);
    }

    /// <summary>Within a cent per dollar: the same price.</summary>
    private static bool Same(decimal a, decimal b) => Math.Abs(a - b) <= Math.Max(0.01m, b * 0.01m);

    /// <summary>The charge after one on <paramref name="date"/>; months by the calendar, so a 31st falls on the month's last day.</summary>
    public static DateTime Next(DateTime date, RecurringCadence cadence) => cadence switch
    {
        RecurringCadence.Weekly => date.AddDays(7),
        RecurringCadence.Biweekly => date.AddDays(14),
        RecurringCadence.Monthly => date.AddMonths(1),
        RecurringCadence.Quarterly => date.AddMonths(3),
        _ => date.AddYears(1),
    };

    private static double Median(List<double> values)
    {
        var sorted = values.Order().ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static decimal Median(List<decimal> values)
    {
        var sorted = values.Order().ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}

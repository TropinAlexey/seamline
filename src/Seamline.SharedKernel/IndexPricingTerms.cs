namespace Seamline.SharedKernel;

// "Average of an index over a pricing window, plus a premium" (ADR-0035,
// spec 03). A value shared by Trading (which owns the terms), Risk (which
// prices them) and their contracts, the same way DeliveryPeriodFormat is
// shared: one definition of what the terms are, not one per module.
public sealed record IndexPricingTerms(string IndexCode, DateOnly WindowFrom, DateOnly WindowTo, decimal Premium)
{
    // Pricing days are the weekdays of the window. Public holidays are
    // ignored — a stated simplification (spec 03, rule 1).
    public IReadOnlyList<DateOnly> PricingDays() => PricingCalendar.Weekdays(WindowFrom, WindowTo);
}

public static class PricingCalendar
{
    public static bool IsPricingDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    public static IReadOnlyList<DateOnly> Weekdays(DateOnly from, DateOnly to)
    {
        var days = new List<DateOnly>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (IsPricingDay(day))
                days.Add(day);
        }
        return days;
    }
}

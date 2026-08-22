namespace Seamline.SharedKernel;

public static class DeliveryPeriodFormat
{
    public static void Validate(string? deliveryPeriod)
    {
        if (deliveryPeriod is not { Length: 7 }
            || deliveryPeriod[4] != '-'
            || !int.TryParse(deliveryPeriod.AsSpan(0, 4), out var year) || year < 2000 || year > 2100
            || !int.TryParse(deliveryPeriod.AsSpan(5, 2), out var month) || month < 1 || month > 12)
            throw new ArgumentException(
                "Delivery period must be in YYYY-MM format (e.g. 2026-08).", nameof(deliveryPeriod));
    }
}

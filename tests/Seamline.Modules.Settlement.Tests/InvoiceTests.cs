using Seamline.Modules.Settlement.Internal;
using Seamline.SharedKernel;

namespace Seamline.Modules.Settlement.Tests;

// The cargo invoice lifecycle (ADR-0034) and its formula-priced variant
// (ADR-0035): two rounding points, the unit price to 4 dp and the amount to
// cents, and a balance against the provisional (ADR-0007).
public class InvoiceTests
{
    private static Invoice Provisional(decimal unitPrice) =>
        Invoice.CreateProvisional(TenantId.New(), Guid.NewGuid(), Guid.NewGuid(), 78_500m, unitPrice);

    [Fact]
    public void Fixed_price_cargo_spec_01_worked_example()
    {
        var provisional = Provisional(110m);

        var final = Invoice.CreateFinal(provisional, 110m, 5_920.0m, 6_000.0m);

        Assert.Equal(8_635_000.00m, provisional.Amount);
        Assert.Equal((108.5333m, 8_519_864.05m, -115_135.95m), (final.UnitPrice, final.Amount, final.Balance));
    }

    [Fact]
    public void Formula_priced_cargo_adjusts_the_final_price_not_the_provisional_one()
    {
        // Spec 03: loaded at the day's expected price, settled at the fixed
        // formula price 106.5000 adjusted for NAR 5,920.
        var provisional = Provisional(106.7174m);

        var final = Invoice.CreateFinal(provisional, 106.5m, 5_920.0m, 6_000.0m);

        Assert.Equal(8_377_315.90m, provisional.Amount);
        Assert.Equal((105.08m, 8_248_780.00m, -128_535.90m), (final.UnitPrice, final.Amount, final.Balance));
        Assert.Equal(InvoiceKind.Final, final.Kind);
    }

    [Fact]
    public void Only_a_provisional_invoice_can_be_settled()
    {
        var standard = Invoice.CreateForDeliveredTrade(TenantId.New(), Guid.NewGuid(), Guid.NewGuid(), 100m, 50m);

        Assert.Throws<InvalidOperationException>(() => Invoice.CreateFinal(standard, 50m, 6_000m, 6_000m));
    }
}

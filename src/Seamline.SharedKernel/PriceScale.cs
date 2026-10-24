namespace Seamline.SharedKernel;

// The scale of every price column and every price the API returns: 4 dp
// (ADR-0007). One definition, so a price rounded in Risk and one rounded in
// Inventory cannot disagree on the scale.
public static class PriceScale
{
    public const int Decimals = 4;

    public static decimal Round(decimal price) => Math.Round(price, Decimals, MidpointRounding.ToEven);
}

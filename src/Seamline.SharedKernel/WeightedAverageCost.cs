namespace Seamline.SharedKernel;

// The one weighted-average cost rule (ADR-0014, ADR-0036), shared by Risk's
// Position and Inventory's storage balance so the two cannot drift:
// - adding in the same direction blends the new price in;
// - reducing without crossing zero leaves the average unchanged — the cost
//   of what is left does not change because some of it went;
// - reaching zero resets it to 0, and crossing zero starts afresh at the
//   incoming price.
// Unrounded: each caller rounds where it stores the result (ADR-0007).
public static class WeightedAverageCost
{
    public static decimal After(decimal quantity, decimal average, decimal signedChange, decimal price)
    {
        var newQuantity = quantity + signedChange;

        if (newQuantity == 0m)
            return 0m;
        if (quantity == 0m || Math.Sign(newQuantity) != Math.Sign(quantity))
            return price;
        if (Math.Abs(newQuantity) > Math.Abs(quantity))
        {
            var oldAbs = Math.Abs(quantity);
            var addedAbs = Math.Abs(signedChange);
            return (average * oldAbs + price * addedAbs) / (oldAbs + addedAbs);
        }
        return average;
    }
}

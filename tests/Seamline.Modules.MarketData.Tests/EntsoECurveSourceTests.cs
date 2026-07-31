using Seamline.Modules.MarketData.Internal;

namespace Seamline.Modules.MarketData.Tests;

public class EntsoECurveSourceTests
{
    [Fact]
    public void AveragePrice_averages_every_price_point_across_the_whole_document()
    {
        var xml = BuildDocument(
            BuildTimeSeries([40m, 60m]),
            BuildTimeSeries([80m, 20m]));

        var average = EntsoECurveSource.AveragePrice(xml);

        // (40 + 60 + 80 + 20) / 4 = 50 — flat across every returned point,
        // not weighted per TimeSeries/day (ADR-0018's documented
        // simplification).
        Assert.Equal(50m, average);
    }

    [Fact]
    public void AveragePrice_returns_null_when_the_document_has_no_points()
    {
        var xml = BuildDocument();

        var average = EntsoECurveSource.AveragePrice(xml);

        Assert.Null(average);
    }

    [Fact]
    public void AveragePrice_rounds_to_four_decimal_places()
    {
        var xml = BuildDocument(BuildTimeSeries([10m, 11m, 11m]));

        var average = EntsoECurveSource.AveragePrice(xml);

        // (10 + 11 + 11) / 3 = 10.6666... -> 10.6667
        Assert.Equal(10.6667m, average);
    }

    private static string BuildDocument(params string[] timeSeries) =>
        $"""<?xml version="1.0"?><Publication_MarketDocument xmlns="urn:iec62325.351:tc57wg16:451-3:publicationdocument:7:0">{string.Join("", timeSeries)}</Publication_MarketDocument>""";

    private static string BuildTimeSeries(decimal[] prices)
    {
        var points = string.Join("", prices.Select((p, i) => $"<Point><position>{i + 1}</position><price.amount>{p}</price.amount></Point>"));
        return $"<TimeSeries><Period>{points}</Period></TimeSeries>";
    }
}

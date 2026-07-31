using Seamline.Modules.MarketData.Internal;

namespace Seamline.Modules.MarketData.Tests;

public class EiaCurveSourceTests
{
    [Fact]
    public void AveragePrice_averages_every_daily_value_in_the_response()
    {
        var json = BuildResponse(["3.10", "3.20", "3.30"]);

        var average = EiaCurveSource.AveragePrice(json);

        Assert.Equal(3.2m, average);
    }

    [Fact]
    public void AveragePrice_returns_null_when_the_response_has_no_rows()
    {
        var json = BuildResponse([]);

        var average = EiaCurveSource.AveragePrice(json);

        Assert.Null(average);
    }

    [Fact]
    public void AveragePrice_skips_null_values_eia_uses_for_non_trading_days()
    {
        var json = """
            {"response":{"data":[
                {"period":"2026-08-01","value":"3.00"},
                {"period":"2026-08-02","value":null},
                {"period":"2026-08-03","value":"3.20"}
            ]}}
            """;

        var average = EiaCurveSource.AveragePrice(json);

        // (3.00 + 3.20) / 2 — the null (weekend/holiday, no trading) is
        // excluded rather than treated as zero.
        Assert.Equal(3.1m, average);
    }

    private static string BuildResponse(string[] values)
    {
        var rows = string.Join(",", values.Select((v, i) =>
            $"{{\"period\":\"2026-08-0{i + 1}\",\"value\":\"{v}\"}}"));
        return $"{{\"response\":{{\"data\":[{rows}]}}}}";
    }
}

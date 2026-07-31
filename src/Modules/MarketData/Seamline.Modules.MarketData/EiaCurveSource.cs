using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Seamline.Modules.MarketData.Internal;

// EIA Open Data API — free, open Henry Hub natural gas spot price series
// (RNGWHHD), api.eia.gov/v2 (ADR-0018). US data, not European — the only
// genuinely free/open gas source found; documented as a known geographic
// mismatch with this project's REMIT/European framing, not hidden.
internal sealed class EiaCurveSource(HttpClient httpClient, IConfiguration configuration, ILogger<EiaCurveSource> logger) : ICurveSource
{
    public async Task<decimal?> GetMonthlyAveragePriceAsync(string commodityCode, DateOnly month, CancellationToken cancellationToken = default)
    {
        var apiKey = configuration["MarketData:CurveImport:Eia:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("EIA curve source selected for {Commodity} but no API key is configured — skipping.", commodityCode);
            return null;
        }

        var (start, end) = ElapsedRange(month);
        var url = "natural-gas/pri/fut/data/" +
                  $"?api_key={apiKey}&frequency=daily&data[0]=value&facets[series][]=RNGWHHD" +
                  $"&start={start:yyyy-MM-dd}&end={end:yyyy-MM-dd}";

        try
        {
            using var response = await httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return AveragePrice(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "EIA request failed for {Commodity} after retries — keeping the existing curve point.", commodityCode);
            return null;
        }
    }

    internal static decimal? AveragePrice(string json)
    {
        using var document = JsonDocument.Parse(json);
        var rows = document.RootElement.GetProperty("response").GetProperty("data");

        var prices = new List<decimal>();
        foreach (var row in rows.EnumerateArray())
        {
            var value = row.GetProperty("value");
            if (value.ValueKind == JsonValueKind.String)
            {
                prices.Add(decimal.Parse(value.GetString()!, CultureInfo.InvariantCulture));
            }
        }

        return prices.Count == 0 ? null : Math.Round(prices.Average(), 4, MidpointRounding.ToEven);
    }

    // EIA's daily series typically lags a day behind "today" — the most
    // recent day requested is yesterday, not today, to avoid always asking
    // for a day that isn't published yet.
    private static (DateOnly Start, DateOnly End) ElapsedRange(DateOnly month)
    {
        var monthStart = new DateOnly(month.Year, month.Month, 1);
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var end = yesterday < monthStart ? monthStart : yesterday;
        return (monthStart, end);
    }
}

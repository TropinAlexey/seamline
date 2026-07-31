using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Seamline.Modules.MarketData.Internal;

// ENTSO-E Transparency Platform — free, open day-ahead electricity price API
// for the EU (ADR-0018). One request spans the whole elapsed range of the
// current month; the average is taken flat across every hourly point the
// document returns, not a two-step per-day-then-per-month average — a
// deliberate simplification, negligible except across a DST transition,
// not worth the extra bucketing code for a demo project.
internal sealed class EntsoECurveSource(HttpClient httpClient, IConfiguration configuration, ILogger<EntsoECurveSource> logger) : ICurveSource
{
    // Germany-Luxembourg bidding zone EIC code — the only zone this demo
    // wires up. A real deployment would map each trader's actual bidding
    // zone instead of hardcoding one.
    private const string BiddingZone = "10Y1001A1001A82H";

    public async Task<decimal?> GetMonthlyAveragePriceAsync(string commodityCode, DateOnly month, CancellationToken cancellationToken = default)
    {
        var token = configuration["MarketData:CurveImport:EntsoE:ApiToken"];
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("ENTSO-E curve source selected for {Commodity} but no API token is configured — skipping.", commodityCode);
            return null;
        }

        var (periodStart, periodEnd) = ElapsedRange(month);
        var url = $"?documentType=A44&in_Domain={BiddingZone}&out_Domain={BiddingZone}" +
                  $"&periodStart={periodStart:yyyyMMdd}0000&periodEnd={periodEnd:yyyyMMdd}0000&securityToken={token}";

        try
        {
            using var response = await httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            return AveragePrice(xml);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "ENTSO-E request failed for {Commodity} after retries — keeping the existing curve point.", commodityCode);
            return null;
        }
    }

    internal static decimal? AveragePrice(string xml)
    {
        var document = XDocument.Parse(xml);
        XNamespace ns = document.Root!.Name.Namespace;

        var prices = document.Descendants(ns + "Point")
            .Select(p => decimal.Parse(p.Element(ns + "price.amount")!.Value, CultureInfo.InvariantCulture))
            .ToList();

        return prices.Count == 0 ? null : Math.Round(prices.Average(), 4, MidpointRounding.ToEven);
    }

    private static (DateOnly Start, DateOnly End) ElapsedRange(DateOnly month)
    {
        var monthStart = new DateOnly(month.Year, month.Month, 1);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var end = today > monthStart ? today : monthStart.AddDays(1);
        return (monthStart, end);
    }
}

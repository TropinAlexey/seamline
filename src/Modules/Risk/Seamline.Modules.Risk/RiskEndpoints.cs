using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Seamline.Modules.MarketData.Contracts;
using Seamline.Modules.Risk.Contracts;

namespace Seamline.Modules.Risk.Internal;

public static class RiskEndpoints
{
    public static IEndpointRouteBuilder MapRiskEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/positions", async (RiskDbContext db, ICurvePointDirectory curvePointDirectory, CancellationToken ct) =>
        {
            var positions = await db.Positions.ToListAsync(ct);

            var result = new List<PositionRef>(positions.Count);
            foreach (var position in positions)
            {
                var curvePoint = await curvePointDirectory.FindAsync(position.CommodityCode, position.DeliveryPeriod, ct);
                result.Add(new PositionRef(position.CommodityCode, position.DeliveryPeriod, position.NetVolume, curvePoint?.Price));
            }

            return Results.Ok(result);
        }).WithTags("Risk");

        return app;
    }
}

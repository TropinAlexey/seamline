using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Seamline.Modules.Risk.Contracts;

namespace Seamline.Modules.Risk.Internal;

public static class RiskEndpoints
{
    public static IEndpointRouteBuilder MapRiskEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/positions", async (RiskDbContext db, CancellationToken ct) =>
        {
            var positions = await db.Positions
                .Select(p => new PositionRef(p.CommodityCode, p.DeliveryPeriod, p.NetVolume))
                .ToListAsync(ct);

            return Results.Ok(positions);
        }).WithTags("Risk");

        return app;
    }
}

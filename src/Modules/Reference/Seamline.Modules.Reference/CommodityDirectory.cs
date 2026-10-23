using Microsoft.EntityFrameworkCore;
using Seamline.Modules.Reference.Contracts;

namespace Seamline.Modules.Reference.Internal;

internal sealed class CommodityDirectory(ReferenceDbContext db) : ICommodityDirectory
{
    public async Task<CommodityRef?> FindAsync(string code, CancellationToken cancellationToken = default)
    {
        var commodity = await db.Commodities.AsNoTracking().FirstOrDefaultAsync(c => c.Code == code, cancellationToken);
        return commodity?.ToRef();
    }
}

using Microsoft.EntityFrameworkCore;
using Seamline.Modules.Reference.Contracts;

namespace Seamline.Modules.Reference.Internal;

internal sealed class CounterpartyDirectory(ReferenceDbContext dbContext) : ICounterpartyDirectory
{
    public async Task<CounterpartyRef?> FindAsync(Guid counterpartyId, CancellationToken cancellationToken = default)
    {
        var counterparty = await dbContext.Counterparties
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == counterpartyId, cancellationToken);

        return counterparty is null
            ? null
            : new CounterpartyRef(counterparty.Id, counterparty.Name, counterparty.CreditLimit);
    }
}

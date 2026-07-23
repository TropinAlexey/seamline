namespace Seamline.Modules.Reference.Contracts;

// Synchronous, in-process, read-only query contract — for slow-changing
// master data another module needs at the moment it needs it, as opposed to
// an integration event for state changes. See CLAUDE.md, "Module boundary
// rules".
public interface ICounterpartyDirectory
{
    Task<CounterpartyRef?> FindAsync(Guid counterpartyId, CancellationToken cancellationToken = default);
}

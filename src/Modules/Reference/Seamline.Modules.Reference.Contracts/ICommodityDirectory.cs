namespace Seamline.Modules.Reference.Contracts;

// Same shape as ICounterpartyDirectory: synchronous, read-only master data
// another module needs at the moment it needs it (ADR-0033).
public interface ICommodityDirectory
{
    Task<CommodityRef?> FindAsync(string code, CancellationToken cancellationToken = default);
}

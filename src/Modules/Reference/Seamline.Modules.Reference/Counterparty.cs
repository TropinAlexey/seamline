using Seamline.SharedKernel;

namespace Seamline.Modules.Reference.Internal;

internal sealed class Counterparty : TenantOwnedEntity<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public decimal CreditLimit { get; private set; }

    private Counterparty() { }

    public static Counterparty Create(TenantId tenantId, string name, decimal creditLimit)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Counterparty name is required.", nameof(name));
        if (creditLimit < 0)
            throw new ArgumentOutOfRangeException(nameof(creditLimit), "Credit limit cannot be negative.");

        return new Counterparty
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            CreditLimit = creditLimit
        };
    }
}

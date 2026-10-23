using Seamline.Modules.Reference.Contracts;
using Seamline.SharedKernel;

namespace Seamline.Modules.Reference.Internal;

// Tenant-owned master data (ADR-0033, ADR-0005): each firm defines its own
// commodity set and contract quality specs. Unit and DeliveryModel are fixed
// at creation — changing either would reinterpret every existing volume on
// the commodity — so only the quality spec of a Cargo commodity can change.
internal sealed class Commodity : TenantOwnedEntity<Guid>
{
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public UnitOfMeasure Unit { get; private set; }
    public DeliveryModel DeliveryModel { get; private set; }
    public QualitySpec? QualitySpec { get; private set; }

    private Commodity() { }

    public static Commodity Create(
        TenantId tenantId, string code, string name, UnitOfMeasure unit, DeliveryModel deliveryModel, QualitySpec? qualitySpec)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 20 || !code.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '-'))
            throw new ArgumentException("Commodity code must be 1–20 characters: A–Z, 0–9 or '-'.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Commodity name is required.", nameof(name));
        if (!Enum.IsDefined(unit))
            throw new ArgumentException($"Unknown unit of measure {unit}.", nameof(unit));
        if (!Enum.IsDefined(deliveryModel))
            throw new ArgumentException($"Unknown delivery model {deliveryModel}.", nameof(deliveryModel));
        RequireSpecMatchesModel(deliveryModel, qualitySpec);

        return new Commodity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = code,
            Name = name,
            Unit = unit,
            DeliveryModel = deliveryModel,
            QualitySpec = qualitySpec
        };
    }

    public void ChangeQualitySpec(QualitySpec qualitySpec)
    {
        if (DeliveryModel != DeliveryModel.Cargo)
            throw new DomainRuleViolationException($"{Code} is a {DeliveryModel} commodity; only Cargo commodities have a quality specification.");
        QualitySpec = qualitySpec;
    }

    public CommodityRef ToRef() => new(Code, Name, Unit, DeliveryModel, QualitySpec?.ToRef());

    private static void RequireSpecMatchesModel(DeliveryModel deliveryModel, QualitySpec? qualitySpec)
    {
        if (deliveryModel == DeliveryModel.Cargo && qualitySpec is null)
            throw new ArgumentException("A Cargo commodity needs a quality specification.");
        if (deliveryModel == DeliveryModel.Flow && qualitySpec is not null)
            throw new ArgumentException("A Flow commodity has no quality specification.");
    }
}

// Contract quality for a cargo commodity, shaped on public standard coal
// contract terms (spec 00): a base net-as-received calorific value the price
// refers to, a rejection floor, and maximum sulphur and ash.
internal sealed record QualitySpec
{
    public decimal BaseNarKcalKg { get; }
    public decimal RejectBelowNarKcalKg { get; }
    public decimal MaxSulphurPct { get; }
    public decimal MaxAshPct { get; }

    public QualitySpec(decimal baseNarKcalKg, decimal rejectBelowNarKcalKg, decimal maxSulphurPct, decimal maxAshPct)
    {
        if (baseNarKcalKg <= 0)
            throw new ArgumentException("Base NAR must be positive.", nameof(baseNarKcalKg));
        if (rejectBelowNarKcalKg <= 0 || rejectBelowNarKcalKg > baseNarKcalKg)
            throw new ArgumentException("The rejection NAR must be positive and not above the base NAR.", nameof(rejectBelowNarKcalKg));
        if (maxSulphurPct is <= 0 or > 100)
            throw new ArgumentException("Max sulphur must be a percentage above 0.", nameof(maxSulphurPct));
        if (maxAshPct is <= 0 or > 100)
            throw new ArgumentException("Max ash must be a percentage above 0.", nameof(maxAshPct));

        BaseNarKcalKg = baseNarKcalKg;
        RejectBelowNarKcalKg = rejectBelowNarKcalKg;
        MaxSulphurPct = maxSulphurPct;
        MaxAshPct = maxAshPct;
    }

    public QualitySpecRef ToRef() => new(BaseNarKcalKg, RejectBelowNarKcalKg, MaxSulphurPct, MaxAshPct);
}

# ADR-0032: Scope — from an ETRM core to a CTRM

## Status

Accepted. Steps 1–3 implemented (commodity master data, coal cargo,
formula pricing); step 4 (gas storage) specified.

## Context

README and CLAUDE.md describe Seamline as "a mini-CTRM for power and gas".
What the code implements is an ETRM core:
- fixed-price monthly forwards on flow commodities;
- a credit saga;
- valuation, stress, REMIT reporting and audit.

What sets a CTRM apart is the physical handling of commodities that move
as lots: logistics, quality, actualized quantities, inventory, and prices
fixed by formula after the trade. None of these exist. The full comparison
is in [the gap analysis](../ctrm-gap-analysis.md).

Claiming CTRM without them is the kind of overstatement this project's
clean-room and "simplifications are stated" rules exist to prevent. There
are two ways out:
- drop the claim;
- build the missing processes.

## Decision

- **Build them, in a fixed order**, each one end-to-end (domain,
  migrations, endpoints, UI, integration and E2E tests, README) before the
  next starts:
  1. commodity master data and units
     ([ADR-0033](0033-commodity-master-data.md),
     [spec 00](../specs/00-commodity-master-data.md));
  2. coal cargo: logistics, actualization, quality, provisional and final
     invoices ([ADR-0034](0034-coal-cargo-logistics.md),
     [spec 01](../specs/01-coal-cargo.md));
  3. formula pricing ([ADR-0035](0035-formula-pricing.md),
     [spec 03](../specs/03-formula-pricing.md));
  4. gas storage and inventory ([ADR-0036](0036-gas-storage-inventory.md),
     [spec 02](../specs/02-gas-storage.md)).

  Coal comes before formula pricing because formula pricing reuses the
  provisional/final invoice that the cargo introduces. Storage is last
  because it touches the fewest existing decisions.
- **The scope boundaries change as follows** (CLAUDE.md "Scope
  boundaries"):
  - "Physical forwards only, power and gas" becomes "physical forwards
    only; power and gas as flow commodities, coal as a cargo commodity".
  - "Monthly delivery periods only" stays for positions, curves and
    valuation. Two finer-grained dates are allowed where the process needs
    them: a cargo's laycan, and daily index fixings.
  - These lines change in CLAUDE.md and README **when the step that needs
    them is implemented**, not before. Until then README says "ETRM core;
    CTRM processes specified".
- **Still out of scope:**
  - options and VaR;
  - freight, CIF and demurrage;
  - blending;
  - multi-currency (one currency, EUR);
  - storage capacity trading;
  - a compliant REMIT implementation.

## Relationship to existing ADRs

| ADR | Effect |
|---|---|
| [0001](0001-modular-monolith.md) | Upheld. New capabilities are new modules inside the monolith (Inventory), or new aggregates in the module that owns the lifecycle they drive: the coal shipment ended up in Trading ([ADR-0037](0037-shipment-lives-in-trading.md)). |
| [0002](0002-service-extraction-criteria.md) | Upheld. No new extracted process. Inventory valuation rides on the existing Valuation.Worker EOD pass. |
| [0015](0015-reporting-worker.md) | Upheld. REMIT stays simplified and covers power and gas only. Coal is outside REMIT's scope, which is consistent with the regulation, not a gap. |
| [0016](0016-stress-scenarios.md) | Upheld. No VaR. |
| 0011, 0012, 0014, 0018, 0031 | Amended in part, each by the step-specific ADR that needs it (0033–0036). The reasons are given there. Two of those were amended in turn during implementation: 0034 by [ADR-0037](0037-shipment-lives-in-trading.md), 0035 by [ADR-0038](0038-price-fixing-is-confirmed-in-trading.md). |

## Consequences

- The project's claim becomes checkable: the gap analysis lists what is
  implemented and what is only specified, and the wording rules there bind
  README.
- Four steps of real work. Each adds a module or a lifecycle, so arch tests,
  RLS, append-only grants and the OpenAPI contract all grow with it.
- Every amended ADR gets a one-line back-link, so a reader of the old
  decision finds the new one.

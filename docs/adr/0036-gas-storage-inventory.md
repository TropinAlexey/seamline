# ADR-0036: Gas storage — Inventory module and WACOG

## Status

Accepted. Specified, not yet implemented ([spec 02](../specs/02-gas-storage.md)).

## Context

Seamline models contractual exposure (`Position`) but not physical stock.
Gas held in storage has:
- capacity, plus daily injection and withdrawal limits;
- a cost basis that changes with every injection;
- unrealized P&L against the curve.

This is the inventory capability a CTRM has and Seamline lacks.

## Decision

- **A new Inventory module** (`Seamline.Modules.Inventory` + `.Contracts`)
  owns:
  - storage contracts;
  - an append-only movement ledger (`Injection` | `Withdrawal`);
  - a current-state balance (quantity, WACOG) updated in the same
    transaction as each movement.

  Not part of Risk: Risk values things, Inventory records physical
  movements entered by operations.
- **WACOG = the rule `Position.Apply` already uses** for a long position:
  - an injection blends;
  - a withdrawal leaves the average unchanged;
  - empty resets to zero.

  The averaging function moves out of `Position` into SharedKernel, and
  both modules call it. One implementation, so the two cannot drift (the
  same reasoning as `MtmCalculator`, ADR-0016).
- **Valuation stays in Risk.** The existing EOD pass reads balances through
  `IInventoryDirectory` (`Inventory.Contracts`). It writes an append-only
  `risk.inventory_valuation` with the run's `run_id`, using `MtmCalculator`
  against the valuation month's curve point. Valuation.Worker registers the
  Inventory module's read side, the same way it already reads MarketData.
- **Inventory and positions are shown side by side, never summed.** A
  position closes on delivery. Gas injected afterwards is stock.

## Relationship to existing ADRs

| ADR | Effect |
|---|---|
| [0001](0001-modular-monolith.md), [0002](0002-service-extraction-criteria.md) | Upheld. A new module in the monolith. No new process: valuation rides on the existing Valuation.Worker. |
| [0005](0005-multi-tenancy.md) | Upheld. Every inventory table is tenant-owned with RLS. |
| [0006](0006-audit-trail-instead-of-event-sourcing.md) | Upheld. The movement ledger is append-only and the balance is current state, the same split as `trade` and `trade_history`. |
| [0007](0007-decimal-rounding.md) | Upheld. WACOG is rounded to 4 dp once per movement when stored. Withdrawal cost and unrealized P&L are rounded to 2 dp. |
| [0014](0014-valuation-worker.md) | **Amended in part (refactor only).** The weighted-average rule described there moves from `Position` into SharedKernel. Why: Inventory needs the identical rule, and Inventory may not reference Risk's implementation (module boundary rule). Behaviour is unchanged, and a regression test on `Position` proves it. |
| [0015](0015-reporting-worker.md) | Upheld. Storage contracts are reportable under REMIT in reality; here that is out of scope and stated, like the rest of the simplified report. |
| [0031](0031-risk-read-models-for-the-ui.md) | Upheld. Inventory valuation carries the EOD `run_id`, so stock and positions on screen come from one run. |

## Consequences

- Seamline shows physical stock with its cost basis and unrealized P&L,
  next to contractual positions.
- A new schema (`inventory`), arch-test coverage and an extra read in the
  EOD pass.
- **Simplifications, stated:**
  - no capacity trading or storage nominations;
  - no ratchets (rates that depend on fill level);
  - no intrinsic or extrinsic storage valuation;
  - no realized P&L that links a withdrawal to a sale;
  - one currency.

## Revisit criteria

- Withdrawals must be matched to sales for realized P&L. That needs lot
  tracking, which ADR-0014 also deferred.
- Fill-level-dependent rates (ratchets) are needed for realistic
  scheduling.

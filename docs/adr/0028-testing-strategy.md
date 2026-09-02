# ADR-0028: Testing strategy — what each layer proves and what it deliberately skips

## Status

Accepted

## Context

The project has 220+ tests across six projects but no single document
explaining why these layers exist, what guarantee each provides, and —
equally important — what is deliberately left untested and why.

Without a stated strategy the test suite drifts: people add tests that
duplicate coverage at the wrong layer, or skip them where they actually
matter. For a portfolio project this is also an interview talking point: the
interviewer should be able to read one page and understand the reasoning.

## Decision

### Test layers and their jobs

| Layer | Project(s) | Count | What it proves | Real infra? |
|-------|-----------|-------|----------------|-------------|
| **Architecture** | `Seamline.ArchTests` | ~87 | Module boundaries, Contracts isolation, decimal-only money, EF conventions, saga placement, cloud portability, schema isolation, MassTransit version pin | No — reflection only (NetArchTest) |
| **Unit** | `Seamline.Modules.*.Tests` | ~140 | Domain logic in isolation: position math (WAP, MtM), stress scenarios, credit reservation rules, curve parsing/normalisation, password hashing, JWT factory | No |
| **Integration** | `Seamline.IntegrationTests` | ~60 | Full vertical slices through the API: trade lifecycle, settlement, valuation, REMIT XML, RLS tenant isolation, audit append-only grants, credit concurrency (advisory locks), Hangfire schema isolation | Yes — Testcontainers (PostgreSQL), `WebApplicationFactory` |

### What each layer does NOT prove

- **Architecture tests** say nothing about runtime correctness. A module can
  satisfy every boundary constraint and still miscalculate P&L.
- **Unit tests** trust the database. They do not catch EF mapping bugs,
  migration drift, or RLS policy gaps.
- **Integration tests** do not prove UI correctness (there is no UI), do not
  measure performance, and do not replace a real load/stress test against a
  sized database.

### What is deliberately not tested

| Gap | Rationale |
|-----|-----------|
| **Line-level code coverage gate** | Coverage percentage rewards test volume, not test value. The arch tests enforce structural invariants that coverage cannot express; the integration tests prove vertical slices that matter. A 90% gate would produce filler tests for mapping boilerplate and DI wiring with no additional safety. |
| **Contract / consumer-driven tests** | All cross-module communication is in-process (in-memory MassTransit transport). There is no service boundary where schema drift can hide. When a second deployable appears, contract tests become mandatory — not before. |
| **Mutation testing** | High cost-to-signal for the current suite size. Revisit if the unit test count exceeds ~300 and false confidence becomes a plausible risk. |
| **Performance / load tests** | The 5xx stress test (README) is a manual smoke check, not an automated regression gate. Automated perf tests require a stable baseline environment the project does not maintain. |
| **UI / E2E browser tests** | No frontend. |
| **Snapshot / approval tests for REMIT XML** | The XML structure is tested via integration tests that assert element content. Full snapshot approval would couple tests to formatting noise. |

### Principles

1. **Test the invariant, not the implementation.** Architecture tests assert
   "module A never references module B's internals" — they do not assert a
   specific class list. Unit tests assert `position.WeightedAvgPrice` after
   a sequence of trades — they do not mock the repository to verify call
   counts.

2. **Real database for anything that touches SQL.** RLS policies, advisory
   locks, append-only grants, and migration correctness are PostgreSQL
   features. Mocking them away defeats the purpose. Testcontainers makes
   this cheap.

3. **Architecture tests as the first line.** A broken module boundary is
   caught in < 1 s by NetArchTest reflection. The same bug caught at
   integration level costs 10–30 s of container startup. Fail fast, fail
   cheap.

4. **No test-only abstractions.** No `ITimeProvider` wrapper, no
   `IFileSystem` shim, no repository interfaces created solely for
   testability. If a dependency is hard to test, the design is wrong — fix
   the design, not the test infrastructure.

5. **Each test has one reason to fail.** A test that asserts trade creation
   AND settlement AND valuation in a single method is an integration
   smoke test, not three unit tests glued together. The integration suite
   has these deliberately; the unit suite does not.

## Consequences

- New modules get unit tests for domain logic and integration tests for any
  behavior that depends on PostgreSQL. Architecture tests for boundary
  enforcement are already generic — new modules are covered automatically.
- A code-coverage gate is not added to CI. If this changes, it gets its own
  ADR with a stated threshold and the evidence that crossed the bar.
- Contract tests become mandatory the moment a second independently deployed
  service exists. That trigger is documented here so it is not forgotten.
- Mutation testing is revisited at ~300 unit tests or when a production bug
  is traced to a test that passed despite a logic inversion.

# ADR-0039: Integration tests wait for the consumer, not for the clock

## Status

Accepted. Implemented.

## Context

Read models are updated asynchronously: a request commits its state change
and its events through the outbox ([ADR-0004](0004-transactional-outbox.md)),
and consumers in other modules apply them later. The integration tests
([ADR-0028](0028-testing-strategy.md)) bridged that gap by polling the read
model: 40 attempts every 250 ms, a 10 s window copied into 20 loops.

A full `dotnet test SeamlineCtrm.sln` runs every test project and every
test class in parallel, each class with its own PostgreSQL container and
host. Under that load `TradeToPositionTests` once failed at exactly 10 s,
and passed 4 out of 4 when run alone. Two causes:

1. **The host did not wait for the bus.** MassTransit's default lets the
   hosted service return from `StartAsync` while the bus is still starting.
   A test's first request could run before the consumers and the outbox
   delivery service were up, and under load that start took seconds.
2. **The tests measured time, not the event.** Any fixed window is a bet
   on machine speed. A larger window only moves the bet.

## Decision

- **The test host starts only after the bus has.** The test factory sets
  `MassTransitHostOptions.WaitUntilStarted = true`. This removes the
  load-dependent cold start without a timing value.
- **A test waits for the consumer it depends on.**
  - `ConsumeSignals`, a consume observer, is connected to the production
    bus configuration (`AddConsumeObserver`). It records every successful
    consume as (endpoint, message).
  - A test awaits, for example,
    `factory.Bus.Consumed<TradeActivatedConsumer, TradeActivated>(m => m.TradeId == id)`
    and then reads the read model once.
  - The endpoint name comes from the same formatter `ConfigureEndpoints`
    uses, so renaming a consumer cannot silently break a wait.
  - A failed attempt does not count; the retry that succeeds does. Some
    consumers throw on purpose until their input has arrived.
- **No MassTransit test harness here.** It would replace the saga's EF
  Core repository with an in-memory one, so the integration tests would no
  longer exercise the persistence production uses. Unit tests keep using
  the harness for the saga's timeout path, as before.
- **The only timeout left is a hang guard** (60 s). Reaching it means the
  message was never consumed, or its consumer keeps failing. It no longer
  means the machine was slow, and the error says which consumer and which
  message.
- **Absence is proven by an event, not a sleep.** For example, "a quality
  rejection issues no final invoice" waits until Audit has consumed the
  rejection, which shows that the event was delivered. Settlement has no
  consumer that could turn it into a final invoice.

## Consequences

- Integration tests have no polling loops and no sleeps. A green run is
  as fast as the consumers.
- A test names its asynchronous dependency explicitly, so a reader sees
  which module's reaction it is checking.
- The integration test project sees the consumer types:
  - Risk, Settlement and Audit gain `InternalsVisibleTo
    Seamline.IntegrationTests`, as Trading, MarketData and Identity
    already had;
  - the module boundary between production assemblies is unchanged, and
    the arch tests still enforce it.
- One subtlety, verified and noted in `ConsumeSignals`: consumers with the
  same name in different modules share one endpoint (Risk's and
  Settlement's `TradeDeliveredConsumer`). The observer fires once per
  message, after all of them have run.

## Revisit criteria

- A test needs to wait for something no consumer does, such as a Hangfire
  job. Give that its own signal; don't fall back to a clock.
- Consumers move to their own endpoint definitions with custom names. Then
  the wait must take the name from the definition, not the default
  formatter.

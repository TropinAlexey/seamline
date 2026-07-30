# ADR-0017: RabbitMQ Transport

**Status:** Accepted
**Date:** 2026-07

## Context

CLAUDE.md and ADR-0009 both already committed to this: "MassTransit is
wired from Phase 1 with the in-memory transport; switching to RabbitMQ in
Phase 2 is a transport configuration change, not a rewrite of publishers or
consumers." This ADR is that switch, plus two real technical details
neither prior document anticipated.

**The credit-approval saga's timeout needs a broker plugin on RabbitMQ.**
`TradeApprovalStateMachine.ApprovalTimeout` (ADR-0008) is scheduled via
`cfg.UseDelayedMessageScheduler()` — a transport-agnostic MassTransit API
(confirmed in `MassTransit.dll` itself: `UseDelayedMessageScheduler` is a
core symbol, not a RabbitMQ-transport one). On the in-memory transport this
needs nothing extra. On RabbitMQ, the same call works by publishing through
an `x-delayed-message` exchange — a type RabbitMQ core doesn't ship with.
It's provided by `rabbitmq_delayed_message_exchange`, a community plugin
not bundled in the official `rabbitmq:*-management` Docker image. Without
it, the very first scheduled message throws at runtime ("exchange type
'x-delayed-message' not found"), not at startup — this would have been a
silent trap.

**Integration tests shouldn't need a real broker.** `SeamlineApiFactory`
already overrides `ConnectionStrings` for Testcontainers Postgres; it says
nothing about MassTransit today because `Program.cs` only ever configured
one transport. Once `Program.cs` can choose RabbitMQ, tests need an
explicit way to keep using the in-memory transport instead of pulling in a
second Testcontainers module and a slower per-class broker startup for
every existing integration test.

## Decision

**A Dockerfile-built RabbitMQ image with the delayed-exchange plugin
pre-enabled**, not the stock `rabbitmq:3.13-management` image. Same shape
as `Seamline.AcerStub`'s Dockerfile earlier this phase — a small,
purpose-built image checked into `docker/rabbitmq/Dockerfile`, added as a
`docker-compose.yml` service. The plugin is downloaded from its GitHub
release matching the RabbitMQ/Erlang ABI of the base image and enabled with
`rabbitmq-plugins enable --offline`, at build time, not a runtime step.

**Transport choice is config-driven, defaulting to RabbitMQ.**
`Program.cs` branches on a `MessageBroker:Transport` config value between
`x.UsingRabbitMq(...)` and `x.UsingInMemory(...)`; only an explicit
`"InMemory"` selects the in-memory transport, so RabbitMQ is the fallback
default rather than a value declared in `appsettings.json` — the
Development-only RabbitMQ host/credentials live in
`appsettings.Development.json`, matching how `ConnectionStrings` is already
split between environments in this project. The shared pipeline config —
`UseDelayedMessageScheduler()`, the global `UseMessageRetry` policy,
`ConfigureEndpoints(context)` — is written once, in a local generic function
taking `IBusFactoryConfigurator<TEndpoint>`, the common interface both
transport configurators implement; only the transport-specific host/
credentials setup differs between the two branches. `SeamlineApiFactory`
sets `MessageBroker:Transport = InMemory` for tests — but not via
`ConfigureAppConfiguration`, unlike `ConnectionStrings`. That override is
only wired into `WebApplicationFactory`'s `IConfiguration` at
`builder.Build()` time (via a diagnostic listener), which is *after*
`Program.cs`'s `AddMassTransit(...)` block has already read
`MessageBroker:Transport` and chosen a transport — `ConnectionStrings`
overrides work regardless because `UseNpgsql` reads them lazily, inside a
factory delegate invoked at first `DbContext` resolution, well after
`Build()`. `SeamlineApiFactory` instead sets
`Environment.SetEnvironmentVariable("MessageBroker__Transport", "InMemory")`
in a static constructor, guaranteeing it lands before the first
`WebApplication.CreateBuilder(args)` call — every existing integration test
keeps running exactly as it did, no new Testcontainers dependency, no
slower per-class broker startup.

**Publishers and consumers are untouched.** Every `IPublishEndpoint`/
`IConsumer<T>` in `Trading`/`Risk`/`Audit`/`Settlement` is transport-
agnostic already — this ADR only changes `Seamline.Api`'s composition root
and `docker-compose.yml`, confirming the "not a rewrite" half of ADR-0009's
prediction held.

## Consequences

### Positive

- The architecture diagram in README no longer has to say "in-memory
  transport" as a permanent caveat — MassTransit now runs against a real
  broker in local dev, matching what a real deployment would use.
- Test suite speed and reliability are unaffected — the branch exists
  specifically so integration tests never have to care which transport
  production uses.
- The delayed-exchange plugin requirement is now written down before it
  could have been discovered the hard way (a saga timeout silently failing
  at 3am with no compile-time or startup-time signal).

### Negative

- One more container to build and run locally
  (`Seamline.Api` + `Seamline.Valuation.Worker` + `Seamline.Reporting.Worker`
  + `Seamline.AcerStub` + `PostgreSQL` + `RabbitMQ`) — a real, if bounded,
  cost for local dev.
- The custom RabbitMQ image pins a plugin release version to a base image
  tag; bumping the RabbitMQ image tag later needs a matching plugin release
  checked, not just a version bump.

## Alternatives considered

**Stock `rabbitmq:3-management` image, accept that delayed messages don't
work.** Rejected — this would silently break the credit-approval saga's
timeout in anything other than local dev with the in-memory transport,
exactly the kind of gap a demo project claiming "production-shaped
architecture" shouldn't have.

**Move the saga's timeout off MassTransit `Schedule<>` onto Hangfire to
sidestep the plugin.** Rejected — directly contradicts ADR-0003's own rule
that instance-owned timeouts use `Schedule<>`, not Hangfire. Rewriting a
correctly-placed decision to dodge an infrastructure detail would be
worse than the infrastructure detail.

**Testcontainers RabbitMQ for integration tests, matching production
exactly.** Rejected for now — more faithful, but slower (a broker container
per test class) for no correctness gain the existing in-memory-transport
tests don't already provide; the saga's actual state-transition logic
doesn't change based on which transport carries the messages.

## Revisit criteria

- **If a test ever needs to prove behavior that's genuinely
  transport-specific** (e.g., RabbitMQ redelivery/dead-lettering semantics
  the in-memory transport doesn't replicate): add Testcontainers RabbitMQ
  for that one test class, not a blanket switch for the whole suite.
- **When the RabbitMQ base image tag is next bumped**: re-check the
  delayed-exchange plugin's release matrix for a compatible version before
  assuming the same release still applies.

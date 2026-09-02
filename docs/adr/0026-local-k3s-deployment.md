# ADR-0026: Local k3s Deployment

**Status:** Accepted
**Date:** 2026-08

## Context

The platform deploys to AWS ECS Fargate via Terraform (ADR-0019, ADR-0024).
ECS abstracts away every Kubernetes-specific design question: pod scheduling,
probe semantics, graceful shutdown coordination, migration ordering, and the
mapping from cloud secrets to container environment. That abstraction is a
strength in production but a liability in interviews for roles that require
hands-on Kubernetes and AKS experience.

Two goals drive this decision:

1. **One-command local deployment.** A single script takes a clean machine to
   a working cluster with all services, observability, and sample data — no
   manual `docker-compose` port juggling, no "run these five commands in
   order."
2. **Force the design questions ECS hides.** Liveness vs. readiness probes,
   init-container migration ordering, `terminationGracePeriodSeconds` vs.
   .NET's `HostOptions.ShutdownTimeout`, PVC lifecycle, and RBAC for
   Prometheus scraping are all decisions a real AKS deployment would require.
   Making them here with real code produces concrete diffs and talking points.

This is a demonstration and learning artifact, not a production cluster.

## Decision

**Single-node k3s cluster deployed via a Helm chart with three values files
(local, AWS, Azure).**

### Helm over Kustomize

One chart, three `values-<env>.yaml` files. The difference between
environments reduces to where secrets come from and what image registry to
pull from. This is a direct answer to "describe your Kubernetes deployment
model" — a values-file diff, not a templating explanation.

### Runnable processes

Four containers, matching the existing docker-compose topology:

| Workload | Type | Probes | Notes |
|---|---|---|---|
| `seamline-api` | Deployment + Service + Ingress | HTTP liveness, readiness, startup | Traefik ingress (k3s built-in) |
| `seamline-valuation-worker` | Deployment | HTTP liveness, readiness | Hangfire recurring jobs |
| `seamline-reporting-worker` | Deployment | HTTP liveness, readiness | Hangfire + ACER stub calls |
| `seamline-acer-stub` | Deployment + Service | HTTP liveness | Flaky stub for reporting |
| `postgres` | StatefulSet + PVC | TCP liveness, exec readiness | Data survives pod restart |
| `rabbitmq` | Deployment (ephemeral) | TCP liveness, HTTP readiness | Messages transient; outbox in Postgres |

### Migrations: Kubernetes Job

A single `Job` runs before any application Deployment starts. It uses the API
Docker image with a `--migrate-only` CLI flag: run all seven module migrations,
create the `seamline_app` role, and exit. The bootstrap script applies the Job
and waits for completion before applying Deployments.

This matches the ECS intent — the Terraform comments note "only service that
runs migrations" for the API task. Workers in ECS do not receive the
`PostgresMigrator` connection string. In k3s the Job receives it; the workers
do not, and their own migration calls in `Program.cs` are never reached
because Hangfire's DDL (`PostgresMigrator`-dependent) is wired at DI time,
not at migration time — Hangfire creates its schema on first use of the
storage, which in k3s uses the regular `Postgres` connection string.

**Application code change required:** workers switch from
`Host.CreateApplicationBuilder` to `WebApplication.CreateSlimBuilder` and
get a `/health` endpoint. Workers now receive only the `Postgres` (app-role)
connection string; Hangfire storage is configured to use that same string.
The `PostgresMigrator` string is reserved for the migration Job.

### Health probes

| Probe | API | Workers | Postgres | RabbitMQ |
|---|---|---|---|---|
| **Liveness** | `GET /health/live` (always 200) | `GET /health/live` (always 200) | TCP :5432 | TCP :5672 |
| **Readiness** | `GET /health/ready` (Postgres) | `GET /health/ready` (Postgres) | `pg_isready` exec | HTTP :15672/api/health/checks/alarms |
| **Startup** | `GET /health/ready` (10s interval, 30 failures) | `GET /health/ready` | — | — |

**Readiness does not check RabbitMQ.** Most API endpoints are reads that
never touch the broker. MassTransit has retry + outbox for transient broker
failures. Removing the API from Service endpoints on a RabbitMQ blip drops
all HTTP traffic, which is worse than occasional 500s on the write path.

**Worker probes are deliberate, not inherited from the API.** Workers were
console hosts. Kubernetes expresses liveness and readiness over HTTP, so
the workers now run Kestrel with a single health endpoint and nothing else.
The cost is a web server in a process that serves no traffic; the benefit
is that health uses the platform's native contract instead of a shell script
we would have to maintain ourselves. The endpoint was the trivial part. The
decision that mattered was what sits behind it: liveness reflects only
in-process liveness — a wedged consumer, a dead scheduler — while readiness
reflects the database alone. Gating liveness on the broker would turn a
broker blip into a cluster-wide restart storm; gating readiness on the broker
would stall rolling updates for no benefit, since no Service routes to these
pods.

### Graceful shutdown

**Verified:** all Dockerfiles use exec-form `ENTRYPOINT ["dotnet", "..."]`,
so `dotnet` is PID 1 and receives SIGTERM directly from kubelet. Shell-form
(`ENTRYPOINT dotnet ...`) would put `/bin/sh` as PID 1, which does not
propagate SIGTERM — the application would never learn it is being stopped
and would be SIGKILLed after the grace period.

**Verified:** `HostOptions.ShutdownTimeout` is 30 seconds for all three
builder types used in this project (`WebApplication.CreateBuilder`,
`WebApplication.CreateSlimBuilder`, `Host.CreateApplicationBuilder`) on
.NET 10.0.302. No configuration source overrides this.

On SIGTERM:

- **MassTransit** (API) stops consuming new messages. In-flight consumers
  receive cancellation via `context.CancellationToken`. Unacknowledged
  messages return to RabbitMQ for redelivery. All consumers propagate the
  token to `SaveChangesAsync`.
- **Hangfire** (workers) stops the server. Running jobs receive cancellation.
  Hangfire marks interrupted jobs for retry on next startup. Job state lives
  in Postgres, so it survives pod restart.

**Unit-of-work analysis for the 35-second grace period:**

- *Valuation Worker:* `EndOfDayValuationRunner` processes one tenant at a
  time; each tenant batch ends with a single `SaveChangesAsync`. Cancellation
  between tenants loses nothing (prior tenants already committed). The
  compute is `(forward - trade) × volume` — trivial multiplication, not a
  long-running CPU pass. For realistic data volumes, a tenant batch completes
  in sub-second time.
- *Reporting Worker:* `RemitReportingRunner` commits after every individual
  trade report (`SaveChangesAsync` per trade, not batched). Cancellation at
  any point preserves all previously committed reports. The ACER stub HTTP
  call has bounded latency (timeouts handled, retries are per-call).
- *API:* MassTransit consumers are short DB writes (audit records, position
  updates). Individual consumer execution is sub-second.

All three processes have sub-second unit-of-work granularity. 30 seconds of
drain time is orders of magnitude more than any single unit needs. The 35s
`terminationGracePeriodSeconds` is the same on all three workloads because
the profiles are genuinely similar — not because the number was not
considered per-workload.

**What ECS never forced:** no bug was found — the existing framework
defaults are correct. What Kubernetes forced was making the relationship
between two timeouts explicit and recorded. `terminationGracePeriodSeconds`
(35s) is now set deliberately against `HostOptions.ShutdownTimeout` (30s),
so the platform's kill deadline sits outside the application's drain window
rather than being whatever the platform happened to default to. If a unit
of work ever exceeds the drain window, the mismatch is visible in one place
instead of implied across two systems. The value is a decision with a
stated basis, not an inherited default.

### Postgres: StatefulSet + PVC

Data must survive pod restart (verification requirement). 1Gi PVC, single
replica. Postgres 17 Alpine, matching docker-compose.

### RabbitMQ: Deployment, ephemeral

Messages are transient. MassTransit's transactional outbox stores pending
publishes in Postgres. Losing RabbitMQ state means at most a brief
redelivery storm from the outbox, not data loss.

### Image delivery: direct import

`docker build` → `docker save` → `k3s ctr images import`. No local registry.
Single-node cluster, zero extra infrastructure.

### Observability: separate namespace

`seamline-observability` namespace with:

- **OTel Collector** — receives OTLP from app pods, exports traces to Jaeger
  and metrics to Prometheus. Keeps the app's OTLP export cloud-agnostic
  (ADR-0025).
- **Jaeger all-in-one** — in-memory trace backend. Fills the gap docker-compose
  left (ADR-0025: "traces not exported locally until a trace backend is added").
- **Prometheus** — `kubernetes_sd_configs` for scraping, kube-state-metrics for
  cluster telemetry.
- **Grafana** — provisioned datasources and dashboards. Existing
  `seamline-overview` dashboard plus a new k8s-specific panel: pod restarts,
  OOMKilled, probe failures.
- **kube-state-metrics** — feeds Prometheus with pod/container/node state.

All observability workloads use `emptyDir` — monitoring data on a local
cluster does not need to survive restarts.

### Config and secrets

`ConfigMap` for non-secret configuration (broker transport, OTEL endpoint,
AcerStub URL). `Secret` (opaque) for connection strings and JWT signing key.
Plain base64 — no sealed-secrets, no external-secrets-operator. This is a
local dev cluster with dev credentials.

## What this does NOT prove

- **No multi-node scheduling.** Single k3s node. Pod affinity, anti-affinity,
  topology spread constraints are not exercised.
- **No real HA.** Single replicas of everything, including Postgres. No
  failover, no leader election.
- **No cluster autoscaling.** Fixed resources, single node.
- **No network policy enforcement.** k3s defaults to Flannel; Calico or
  Cilium would be needed for meaningful network policy.
- **No HPA.** Single-node cluster cannot demonstrate horizontal scaling.
- **No TLS termination.** Traefik serves HTTP only. cert-manager is excluded.
- **No GitOps.** No ArgoCD, no Flux. Manifests are applied imperatively by
  the bootstrap script.
- **No service mesh.** No Istio, no Linkerd. mTLS between services is not
  demonstrated.

## Pushback table

| Objection | Response |
|---|---|
| "k3s isn't real Kubernetes" | k3s is CNCF-certified conformant. API, scheduling, probes, RBAC, PVC, and Ingress behave identically to AKS/EKS/GKE. |
| "Helm is overkill for a local demo" | The chart exists to tell the multi-cloud story: `values-local.yaml` → `values-aks.yaml` is a two-minute diff. Without Helm, that story requires explaining Kustomize overlays. |
| "Why not just use docker-compose" | docker-compose doesn't force probe design, migration ordering, PVC lifecycle, or `terminationGracePeriodSeconds`. Those are the questions this exercise exists to answer. |
| "StatefulSet for a demo Postgres is over-engineering" | The verification requires data to survive `kubectl delete pod`. emptyDir fails that test. |
| "No tests in CI for k3s?" | A full cluster bootstrap exceeds the CI budget. The bootstrap script is idempotent and tested manually. Consider adding a lightweight CI check later if k3s startup fits within the pipeline budget. |

## Interview talking points

1. **"What changes when you move from ECS to Kubernetes?"** Probe semantics
   (ECS has one health check; k8s has three), migration ordering (ECS
   `depends_on` vs. k8s Job), graceful shutdown (`terminationGracePeriodSeconds`
   must match `HostOptions.ShutdownTimeout`), and PVC lifecycle (ECS EBS
   volumes are attached at task level, k8s PVCs outlive pods).

2. **"How do you handle database migrations in Kubernetes?"** Kubernetes Job
   that runs the API image with `--migrate-only`. Runs before Deployments.
   Idempotent — safe to rerun. Single source of truth for schema (the API
   process), not three competing migrators.

3. **"What happens to in-flight messages when you kill a pod?"** MassTransit:
   unacked messages return to RabbitMQ. Hangfire: interrupted jobs marked for
   retry, state in Postgres survives. `terminationGracePeriodSeconds` (35s) >
   .NET `ShutdownTimeout` (30s) — the platform's kill deadline sits outside
   the app's drain window. All Dockerfiles use exec-form ENTRYPOINT so PID 1
   is `dotnet`, not `sh` — SIGTERM reaches the application directly.

4. **"How does your observability translate to Azure?"** OTel Collector is the
   abstraction layer (ADR-0025). Swap the exporter config from Jaeger/Prometheus
   to Azure Monitor OTLP endpoint. Application code unchanged — it emits OTLP,
   always.

5. **"Why Helm over Kustomize?"** One chart, three values files. The cloud
   difference is where secrets come from (`Secret` vs. Azure Key Vault CSI
   driver) and the image registry. Everything else — probes, resource limits,
   shutdown config — is shared.

## Revisit criteria

- If AKS deployment becomes a real deliverable, the Helm chart needs a
  `values-aks.yaml` with Azure-specific secret injection (CSI driver or
  external-secrets-operator).
- If CI budget allows, add a lightweight k3s smoke test (bootstrap + health
  check + teardown).
- If multi-node testing becomes valuable, switch image delivery from
  `k3s ctr import` to a local registry.

## Consequences

### Positive
- Forces every Kubernetes design question with real code and real verification.
- Produces interview-ready talking points backed by concrete diffs.
- One-command local deployment for the full system.
- Helm chart is reusable for AKS with a values-file swap.

### Negative
- Maintains a second deployment mechanism alongside Terraform/ECS.
- Workers gain Kestrel dependency (for health endpoints) that was previously unnecessary.
- k3s-specific scripts must be tested manually — no CI coverage initially.

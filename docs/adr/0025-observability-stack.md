# ADR-0025: Observability Stack

**Status:** Accepted
**Date:** 2026-08

## Context

The platform has four composition roots (API, Valuation Worker, Reporting
Worker, Valuation Function) but no way to observe their behaviour in
production beyond container logs. We need metrics (request rates, latencies,
error rates, connection pools, GC pressure) and eventually traces.

Two constraints shape the decision:

1. **Cloud portability (ADR-0019):** cloud differences live in infrastructure
   and config, never in domain or module code. The app must not reference
   `AWSSDK.*` or `Azure.*` packages (enforced by arch tests, ADR-0021).
2. **Vendor neutrality:** locking into a proprietary telemetry SDK (X-Ray SDK,
   Application Insights SDK) would violate constraint 1 and make the local
   dev experience depend on cloud credentials.

## Decision

**Vanilla OpenTelemetry SDK with OTLP export everywhere.** The app emits
standard OTLP; the *infrastructure* routes it to the cloud-native backend.

### Application layer (cloud-agnostic)

Each composition root configures:

- `OpenTelemetry.Instrumentation.AspNetCore` (API only)
- `OpenTelemetry.Instrumentation.Http`
- `Npgsql.OpenTelemetry` (connection pool metrics)
- `OpenTelemetry.Instrumentation.Runtime` (GC, thread pool, allocations)
- MassTransit's built-in `MassTransit` meter

All export via `OtlpExporter` to `OTEL_EXPORTER_OTLP_ENDPOINT` — a single
env var that infrastructure sets per environment.

### Local development

`docker-compose.yml` runs:

- **OTel Collector** (`otel/opentelemetry-collector-contrib`) — receives OTLP
  on gRPC 4317 / HTTP 4318, exports metrics to Prometheus.
- **Prometheus** — scrapes the collector's Prometheus exporter on port 8889.
- **Grafana** — pre-provisioned with a Prometheus datasource and a
  `seamline-overview` dashboard (9 panels: HTTP rates/latency/errors, Npgsql
  pool, MassTransit consumer metrics, .NET runtime).

No cloud credentials needed locally.

### AWS (ECS Fargate)

The vanilla OTel SDK cannot sign requests with SigV4, which AWS endpoints
require. Rather than adding an AWS SDK dependency to the app:

- Each ECS task definition includes an **ADOT sidecar**
  (`public.ecr.aws/aws-observability/aws-otel-collector`) as a non-essential
  container.
- The app sends OTLP to `http://localhost:4317` (the sidecar).
- The sidecar signs and forwards to CloudWatch Metrics and X-Ray using the
  task role's IAM permissions (`xray:PutTraceSegments`,
  `xray:PutTelemetryRecords`, `cloudwatch:PutMetricData`).
- Worker task definitions bumped from 256 CPU / 512 MB to 512 CPU / 1024 MB
  to accommodate the sidecar (~128 MB).

### Azure (Container Apps)

Application Insights accepts OTLP natively at
`https://{region}.applicationinsights.azure.com`. The app sends OTLP directly
— no sidecar needed:

- `OTEL_EXPORTER_OTLP_ENDPOINT` → the regional OTLP endpoint (no path
  suffix — the SDK appends `/v1/metrics` and `/v1/traces` itself).
- `OTEL_EXPORTER_OTLP_HEADERS` → `x-ms-ikey={instrumentationKey}` for
  authentication.

Both values are wired from the `monitoring.bicep` module through
`container-apps.bicep`.

## Consequences

- **No cloud SDK in application code.** The app knows only OTLP; the
  infrastructure decides where telemetry lands. Consistent with ADR-0019 and
  ADR-0021.
- **Local observability works out of the box** — `docker compose up` brings
  up the full metrics pipeline with a pre-built dashboard.
- **Cost:** AWS worker tasks doubled in resources (256/512 → 512/1024) to fit
  the ADOT sidecar. Acceptable — these are lightweight workers.
- **Traces pipeline is metrics-only for now.** The local collector exports
  metrics to Prometheus; traces are not exported locally until a trace backend
  (Jaeger/Tempo) is added. Cloud environments get traces via their native
  backends (X-Ray, Application Insights).
- **ADOT sidecar version and Grafana dashboard are pinned.** Upgrades require
  updating `infra/aws/ecs.tf` and `docker/grafana/dashboards/` respectively.

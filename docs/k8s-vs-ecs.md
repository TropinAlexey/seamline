# What Kubernetes Forced Me to Change That ECS Did Not

## 1. Health probes: one became three

ECS has a single health check per task. Kubernetes has liveness, readiness,
and startup — each with different semantics and failure consequences. Liveness
failure restarts the pod; readiness failure removes it from Service endpoints
but keeps it alive.

**Concrete change:** workers (`Seamline.Valuation.Worker`, `Seamline.Reporting.Worker`)
had zero HTTP endpoints — they were pure `Host.CreateApplicationBuilder` console
apps with Hangfire as a hosted service. Kubernetes probes need an HTTP target.

**Diff:** SDK changed from `Microsoft.NET.Sdk` to `Microsoft.NET.Sdk.Web`, builder
changed from `Host.CreateApplicationBuilder` to `WebApplication.CreateSlimBuilder`,
and each worker gained `/health/ready` (Postgres check) and `/health/live`
(always 200). The API gained the same split — previously only `/health` existed.

## 2. Migration ordering: implicit became explicit

ECS relies on `depends_on` in docker-compose (local) and Terraform task
ordering (prod). Both are coarse: "start after X is healthy." In Kubernetes,
pods start concurrently by default and there is no native `depends_on`.

**Concrete change:** a Kubernetes `Job` (`migration-job.yaml`) runs the API image
with `--migrate-only`. The bootstrap script waits for the Job to complete
before Deployments roll out. The flag itself required a four-line addition to
`Program.cs` — check `args.Contains("--migrate-only")`, return after migrations.

## 3. Graceful shutdown: implicit timeout became a recorded decision

ECS Fargate sends SIGTERM and waits 30 seconds (the `stopTimeout` default).
.NET's `HostOptions.ShutdownTimeout` is also 30 seconds. These happen to match,
but neither references the other — the relationship is invisible.

**Concrete change:** `terminationGracePeriodSeconds: 35` in every Deployment
manifest, set deliberately against the 30-second .NET drain window. The 5-second
buffer ensures the platform's SIGKILL deadline sits outside the application's
drain window. No bug was found — the value is making the implicit relationship
between two timeouts explicit and recorded. Also verified: all Dockerfiles
use exec-form `ENTRYPOINT ["dotnet", "..."]` so PID 1 is `dotnet`, not `sh`.

## 4. Data persistence: restart-safe by default became opt-in

ECS Fargate tasks get ephemeral storage. Persistent data goes to RDS — a
separate managed service, not a container concern. In Kubernetes, a pod's
filesystem is ephemeral by default. Postgres data must survive `kubectl delete pod`.

**Concrete change:** Postgres runs as a `StatefulSet` with a `PersistentVolumeClaim`
(1Gi). RabbitMQ stays as a `Deployment` with no PVC — messages are transient,
the outbox lives in Postgres.

## 5. Observability routing: direct export became a collector pipeline

docker-compose wired OTLP directly from app containers to Prometheus (metrics)
with no trace backend. Kubernetes forced the full pipeline: OTel Collector
receives OTLP from app pods, routes traces to Jaeger and metrics to Prometheus.
This matches the cloud pattern (Collector → Azure Monitor / CloudWatch) that
docker-compose never required.

**Concrete change:** `seamline-observability` namespace with OTel Collector,
Jaeger all-in-one, Prometheus (with RBAC for `kubernetes_sd_configs` scraping),
Grafana (provisioned datasources + dashboards), and kube-state-metrics.

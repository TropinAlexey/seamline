# ADR-0029: tini as PID 1 in every .NET image

## Status

Accepted

## Context

On a fresh k3s cluster the migration Job started before Postgres accepted
connections. `EnsureAppRoleAsync` threw, the exception went unhandled — and
the process never exited. It sat at 100% CPU until Helm timed out; the API
pod with the same failure was only recovered because its liveness probe
killed it after ~5 minutes. A Job has no liveness probe, so nothing did.

Root cause: on an unhandled exception the .NET runtime terminates via
`abort()` / `SIGABRT`. The kernel does not apply default signal actions to
PID 1 of a PID namespace, so when `dotnet` itself is PID 1 the abort is a
no-op and the runtime spins. Reproduced with
`docker run local/seamline-api --migrate-only` against an unreachable
Postgres: hangs indefinitely; the same image with `docker run --init` exits
with code 134 immediately.

This is not specific to k3s. Every deploy target (docker compose, k3s, ECS
Fargate, Azure Container Apps) runs our images with `dotnet` as PID 1, and
ECS / Container Apps have no liveness probe on the migration path either.

## Decision

Install `tini` in the runtime stage of every .NET Dockerfile and use it as
the entrypoint: `ENTRYPOINT ["tini", "--", "dotnet", "<App>.dll"]`.

Alternatives considered:

- **Platform init flags** (`init: true` in compose, `initProcessEnabled` in
  the ECS task definition, `shareProcessNamespace` in k8s). Four different
  mechanisms, one of which (Container Apps) has no equivalent; easy to miss
  on the next deploy target. The image is the one thing all targets share.
- **Handle every exception in code.** Done for the known case — startup DB
  initialization now retries with bounded backoff and exits with code 1
  explicitly — but it cannot cover every unhandled exception in four
  processes. tini is the safety net; explicit handling gives a readable log
  line and a clean exit code.

## Consequences

- Any unhandled exception now terminates the container, so the
  orchestrator's restart policy (Job `backoffLimit`, Deployment restarts, ECS
  service scheduler) takes over as designed.
- tini forwards `SIGTERM` to `dotnet`, so graceful shutdown
  (`HostOptions.ShutdownTimeout`, ADR-0026) is unchanged. It also reaps
  zombies, irrelevant today but free.
- One extra small apt package per image. A deploy config that replaces the
  entrypoint bypasses tini: pass arguments via k8s `args` or ECS `command`
  (appended to the entrypoint), never k8s `command` or ECS `entryPoint`.

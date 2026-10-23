# SEAMLINE — главный файл проекта (для всех агентов)

> Обновлять при каждом significant изменении. Утром первым делом читать этот файл.
> Подробные архитектурные правила — в `CLAUDE.md`. Это файл состояния, не правил.

## Что это
Multi-tenant mini-CTRM (power/gas forwards) на .NET 10 — модульный монолит
(`src/Modules/<Name>/` = impl + Contracts), 2 воркера вынесены осознанно
(Valuation, Reporting). Postgres (схема на модуль + RLS), MassTransit 8.5.10
(InMemory локально/тесты, RabbitMQ dev, ServiceBus в Azure), Hangfire для EOD.
Тонкий web UI (React 19 + Mantine, `web/`) за same-origin прокси `/api`.
4 таргета деплоя: docker compose, k3d+Helm, AWS ECS (Terraform), Azure CA (Bicep).

## Текущий статус (2026-10-23, ветка `main`, дерево чистое)
- **Фазы 1–5 завершены, Фаза 6 (Web UI) — Phase A и B закоммичены.** Phase C
  (SignalR) и D (облачный деплой web) — вне скоупа, пока не попросят.
- Тесты: 243 .NET (110 unit + 69 arch + 64 integration) + web 28 unit (Vitest)
  + 10 E2E (Playwright против compose). 31 ADR.
- CI — 4 job-ы, все зелёные: `build-and-test`, `api-contract` (OpenAPI не
  устарел), `web` (lint/format/typecheck/unit/build, TS-клиент == OpenAPI),
  `e2e` (compose с нуля + seed + Playwright, только push в main).
- Последний коммит — фикс гонки миграций при старте (`fd1160e`).

## Что сделано последним (хронология)
1. `d37719a` — бэкенд под UI: read-эндпоинты (blotter, history, exposure,
   latest valuation/stress, audit), 409/400 вместо 500, реальный approver
   через сагу, утечка credit reservation, EOD `run_id`, per-IP rate limit
   логина за доверенным прокси; ADR-0030, ADR-0031.
2. `170a78b` — `web/` (React + Mantine), `tools/seed/seed.cs`, nginx в
   compose/Helm, `docker-compose.override.yml` (demo-настройки, Hangfire на
   127.0.0.1:8082/8083), CI-job-ы `api-contract`/`web`/`e2e`.
3. `0f25af8` — README: Web UI, тур из 7 шагов, скриншоты.
4. `fd1160e` — `MigrationLock.MigrateWithLockAsync` (SharedKernel,
   `pg_advisory_lock`): API и воркеры гонялись за миграциями на пустой БД.
   Всплыло на первом прогоне `e2e` в CI.
5. `35a0cea`, этот файл — бейджи README на push в main, AGENTS.md в репо.

## Что дальше (приоритет сверху вниз)
1. [ ] Утренняя проверка: `dotnet build SeamlineCtrm.sln && dotnet test SeamlineCtrm.sln`,
       `gh run list --limit 3` — последний push зелёный.
2. [ ] Осознанно оставлено: amend-consumer двигает резервацию без advisory lock
       (amend не проходит кредитную проверку by design).
3. [ ] k3d: ingress отвечает connection reset после `cluster stop/start` —
       обход `kubectl port-forward`, причина не разобрана.
4. [ ] Опционально (бэклог): TLS на ALB/App Gateway, Argon2id вместо PBKDF2,
       HPA/NetworkPolicy для k8s, rate limit по аккаунту, а не только по IP
       (README «Known limitations» + «Security considerations»).
5. [ ] Любое архитектурное решение → новый ADR в `docs/adr/`
       (Context/Decision/Consequences), обновить счётчик в README.

## Быстрый старт утром
```bash
git status --short --branch && git log --oneline -5
dotnet build SeamlineCtrm.sln
dotnet test SeamlineCtrm.sln
docker compose up -d && dotnet run tools/seed/seed.cs   # UI на http://localhost:8081
cd web && npm run e2e                                   # Playwright против стека
```
Демо: тур по UI (7 шагов) и 5 curl-ов — в README («Web UI», «Quick demo»).

## Где что лежит
- Правила модулей: `CLAUDE.md` (обязательно к соблюдению, проверяется ArchTests в CI).
- Решения: `docs/adr/` (31 шт). Начать с 0001 (монолит), 0008 (saga),
  0005 (multi-tenancy), 0030 (web UI).
- Ежедневные логи: `.remember/today-*.done.md`, сводка — `.remember/recent.md`.
- API: `src/Seamline.Api`, модули: `src/Modules/`, воркеры: `src/Seamline.*.Worker/`.
- Web: `web/` (OpenAPI-контракт `web/openapi/seamline.json`, клиент
  генерируется в `web/src/api/schema.d.ts`), seed: `tools/seed/seed.cs`.

## Протокол обновления (для агента)
1. В конце рабочей сессии дописать 1–3 строки в «Что сделано последним» + обновить дату/статус шапки.
2. Галочки «Что дальше» отмечать `[x]` по факту, новые пункты — сверху вниз по приоритету.
3. Не дублировать сюда `CLAUDE.md` и README — только состояние и next steps.
4. Файл короткий (цель <100 строк). Детали — в `.remember/`, сюда — только выжимка.

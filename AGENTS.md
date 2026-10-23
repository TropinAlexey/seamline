# SEAMLINE — главный файл проекта (для всех агентов)

> Обновлять при каждом significant изменении. Утром первым делом читать этот файл.
> Подробные архитектурные правила — в `CLAUDE.md`. Это файл состояния, не правил.

## Что это
Multi-tenant mini-CTRM (power/gas forwards) на .NET 10 — модульный монолит
(`src/Modules/<Name>/` = impl + Contracts), 2 воркера вынесены осознанно
(Valuation, Reporting). Postgres (схема на модуль + RLS), MassTransit 8.5.10
(InMemory локально/тесты, RabbitMQ dev, ServiceBus в Azure), Hangfire для EOD.
4 таргета деплоя: docker compose, k3d+Helm, AWS ECS (Terraform), Azure CA (Bicep).

## Текущий статус (2026-09-02, ветка `main`, дерево чистое)
- **Все 5 фаз завершены.** 220 тестов (110 unit + 69 arch + 41 integration), 29 ADR.
- Последний коммит `8ad4e9c` — расширение MassTransit retry-окна (3.85s→8.7s) против saga race под нагрузкой CI.
- Перед этим: фикс 5 issues #30–34 (depends_on, Hangfire DDL, inbox idempotency, ADOT, helm values), append-only grant tests, audit RLS tests, ADR-0028 (testing strategy), README demo (5 curls).
- Открытых веток и незакоммиченных изменений нет (`git status` чистый, `main` == `origin/main`).

## Что сделано последним (хронология)
1. `ba20211` — credit-reservation race закрыт через `pg_advisory_xact_lock` (ADR-0027).
2. `f587d5f` — 12 arch-тестов на непокрытые инварианты (208 тестов).
3. `4f22df4` — 10 integration-тестов append-only grants + audit RLS (220 тестов).
4. `fb78ccc` — ADR-0028 + README (было заявлено 244, факт 220 — исправлено 2026-09-20).
5. README: quick demo, deploy-диаграмма, секция «What K8s forced to change».
6. `8ad4e9c` — MassTransit retry window fix (41 integration pass).

## Что дальше (приоритет сверху вниз)
1. [ ] Утренняя проверка: `dotnet build SeamlineCtrm.sln && dotnet test SeamlineCtrm.sln` — убедиться, что зелёное.
2. [ ] CI-бейджи / GitHub Actions: проверить, что последний push прошёл CI без флаков saga.
3. [ ] Опционально (бэклог, не обязательство): rate limiting (`RateLimiter` middleware), TLS на ALB/App Gateway, Argon2id вместо PBKDF2, HPA/NetworkPolicy для k8s (см. README «Known limitations» + «Security considerations»).
4. [ ] Любое архитектурное решение → новый ADR в `docs/adr/` (Context/Decision/Consequences), обновить счётчик в README.

## Быстрый старт утром
```bash
git status --short --branch && git log --oneline -5
dotnet build SeamlineCtrm.sln
dotnet test SeamlineCtrm.sln
docker compose up -d && ./scripts/load-test.sh --smoke
```
Демо-флоу (5 curls: login→counterparty→trade→submit→positions) — в README «Quick demo».

## Где что лежит
- Правила модулей: `CLAUDE.md` (обязательно к соблюдению, проверяется ArchTests в CI).
- Решения: `docs/adr/` (28 шт). Начать с 0001 (монолит), 0008 (saga), 0005 (multi-tenancy).
- Ежедневные логи: `.remember/today-*.done.md`, сводка — `.remember/recent.md`.
- API: `src/Seamline.Api`, модули: `src/Modules/`, воркеры: `src/Seamline.*.Worker/`.

## Протокол обновления (для агента)
1. В конце рабочей сессии дописать 1–3 строки в «Что сделано последним» + обновить дату/статус шапки.
2. Галочки «Что дальше» отмечать `[x]` по факту, новые пункты — сверху вниз по приоритету.
3. Не дублировать сюда `CLAUDE.md` и README — только состояние и next steps.
4. Файл короткий (цель <100 строк). Детали — в `.remember/`, сюда — только выжимка.

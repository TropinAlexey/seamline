# SEAMLINE — главный файл проекта (для всех агентов)

> Обновлять при каждом significant изменении. Утром первым делом читать этот файл.
> Подробные архитектурные правила — в `CLAUDE.md`. Это файл состояния, не правил.

## Что это
Multi-tenant mini-CTRM (power/gas forwards) на .NET 10 — модульный монолит
(`src/Modules/<Name>/` = impl + Contracts), 2 воркера вынесены осознанно
(Valuation, Reporting). Postgres (схема на модуль + RLS), MassTransit 8.5.10
(InMemory локально/тесты, RabbitMQ dev, ServiceBus в Azure), Hangfire для EOD.
Тонкий web UI (React 19 + Mantine, `web/`) за same-origin прокси `/api`.
Честно: ETRM-ядро + уголь-карго с фиксированной или формульной ценой +
хранилище газа (WACOG) — все CTRM-процессы из `docs/ctrm-gap-analysis.md`
реализованы.
4 таргета деплоя: docker compose, k3d+Helm, AWS ECS (Terraform), Azure CA (Bicep).

## Текущий статус (2026-10-23, ветка `main`)
- **Фазы 1–5 завершены, Фаза 6 (Web UI) — Phase A и B закоммичены.** Phase C
  (SignalR) и D (облачный деплой web) — вне скоупа, пока не попросят.
- **Фаза 7 (CTRM) завершена:** ADR-0032…0040, `docs/specs/00–03`, gap
  analysis. Шаги: 1 справочник товаров, 2 уголь-карго (ADR-0034 + 0037),
  3 формульная цена (ADR-0035 + 0038), 4 хранилище газа (ADR-0036 + 0040).
  40 ADR.
- Тесты: 341 .NET (159 unit + 78 arch + 104 integration) + web 29 unit
  (Vitest) + 15 E2E (Playwright против compose).
- CI — 4 job-ы, все зелёные: `build-and-test`, `api-contract` (OpenAPI не
  устарел), `web` (lint/format/typecheck/unit/build, TS-клиент == OpenAPI),
  `e2e` (compose с нуля + seed + Playwright, только push в main).
- Последний коммит — Фаза 7, шаг 4 (хранилище газа, ADR-0040). Фаза 7 завершена.

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
5. `35a0cea`, `a3e4c9d`, `c56d3e7`, `3444a7f` — бейджи README, AGENTS.md,
   rate limiting в Security, скриншот trade detail с REMIT.
6. Документы Фазы 7: gap analysis, спеки 00–03, ADR-0032…0036, обратные
   ссылки «Amended in part by» в ADR-0011/0012/0014/0018.
7. Фаза 7, шаг 1: `Commodity` в Reference (миграция `AddCommodity`, сид
   POWER/GAS для демо-тенантов, RLS), `/commodities`, 400 на неизвестный
   товар в `POST /trades`, CurveImportRunner без хардкода, UI: выбор товара
   из справочника, единицы в blotter/detail, страница Commodities.
8. Фаза 7, шаг 2: уголь-карго. Shipment в Trading (ADR-0037 — outbox один
   на шину, погрузка = поставка в одной транзакции), CargoTerms на сделке,
   ActualVolume, `/shipments`, Invoice Provisional/Final с балансом, Risk
   закрывает ContractVolume, REMIT пропускает уголь, сид COAL, seed-скрипт
   с примером из спеки, UI: панель Shipment, страница Shipments.
9. Фаза 7, шаг 3: формульная цена. ADR-0038 — события фиксинга нет (outbox
   только у Trading), финальную цену подтверждает BO (`/trades/{id}/fix-price`);
   расчёт только в Risk (`IFormulaPricingService`), фиксинги append-only в
   MarketData (`IIndexFixingDirectory`), FormulaTrade в Risk вне позиции,
   EOD/стресс по сделке, финальный счёт — по тому из двух событий, которое
   Trading снабдил обеими половинами. Сид: пример spec 03; сид ждёт окно
   rate limit (6 логинов при лимите 5/мин — баг с шага 2).
10. ADR-0039: интеграционные тесты ждут не таймер, а консьюмера
    (`ConsumeSignals` + `WaitUntilStarted`); убраны все 20 циклов опроса.
    Причина — флейк TradeToPositionTests под полным прогоном решения.
11. Ревью шага 3 (стандарты + spec), всё исправлено: гонка certify/fix-price
    (финальный счёт мог не появиться) — блокировка строки сделки
    (`TradeRowLock`), тест воспроизводил баг без неё; синтетический ряд
    фиксингов в импорте кривой (как в ADR-0035); `/pricing-exposure` только
    FO/MO; notional от неокруглённой цены; дубли выбора цены и округления
    в Risk; тип `CargoCertificate`; `formulaFinalPrice`/`confirmedPrice`;
    проект `Seamline.Modules.Settlement.Tests`.
12. Фаза 7, шаг 4: хранилище газа. Модуль Inventory (договор с остатком +
    append-only журнал движений, блокировка строки договора), правило WACOG
    вынесено из `Position` в SharedKernel (`WeightedAverageCost`), Risk
    оценивает запас в EOD (`inventory_valuation`, `/inventory/valuations/latest`),
    страница Storage, сид с примером spec 02. ADR-0040: событий нет (outbox
    только у Trading), WACOG хранится с 12 знаками — округление до 4 на
    каждом движении дрейфовало (29.0002 вместо 29.0000 после 30 закачек).

## Что дальше (приоритет сверху вниз)
1. [ ] Фаза 7 завершена. Дальше — по решению пользователя: бэклог ниже;
       outbox на модуль — общий revisit-триггер ADR-0037/0038/0040. Новый
       процесс — сначала spec + ADR, потом код.
2. [ ] Утренняя проверка: `dotnet build SeamlineCtrm.sln && dotnet test SeamlineCtrm.sln`,
       `gh run list --limit 3` — последний push зелёный.
3. [ ] Осознанно оставлено: amend-consumer двигает резервацию без advisory lock
       (amend не проходит кредитную проверку by design).
4. [ ] k3d: ingress отвечает connection reset после `cluster stop/start` —
       обход `kubectl port-forward`, причина не разобрана.
5. [ ] Опционально (бэклог): TLS на ALB/App Gateway, Argon2id вместо PBKDF2,
       HPA/NetworkPolicy для k8s, rate limit по аккаунту, а не только по IP
       (README «Known limitations» + «Security considerations»).
6. [ ] Любое архитектурное решение → новый ADR в `docs/adr/`
       (Context/Decision/Consequences), обновить счётчик в README. Нарушение
       старого ADR — только через новый ADR со ссылкой и «почему» + строка
       «Amended in part by» в старом.

## Быстрый старт утром
```bash
git status --short --branch && git log --oneline -5
dotnet build SeamlineCtrm.sln
dotnet test SeamlineCtrm.sln
docker compose up -d && dotnet run tools/seed/seed.cs   # UI на http://localhost:8081
cd web && npm run e2e                                   # Playwright против стека
```
Демо: тур по UI (10 шагов) и 5 curl-ов — в README («Web UI», «Quick demo»).

## Где что лежит
- Правила модулей: `CLAUDE.md` (обязательно к соблюдению, проверяется ArchTests в CI).
- Решения: `docs/adr/` (40 шт), спеки CTRM — `docs/specs/`. Начать с 0001 (монолит), 0008 (saga),
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

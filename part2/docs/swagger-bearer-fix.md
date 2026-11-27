# Исправление Swagger Bearer — 26.11.2026

Запрос P003: исправить применение Bearer в Swagger без изменения первой части, архитектуры, server auth и business logic. Ассистент: Codex / ChatGPT. Вся новая проверка сохранена отдельно в `artifacts/swagger-bearer-2026-11-26`; предыдущие логи/coverage21.11 не перезаписывались.

## Подтверждённый дефект

Bearer SecurityScheme уже был правильно зарегистрирован: `type:http`, `scheme:bearer`, `bearerFormat:JWT`. Но `ContractFilter` создавал `OpenApiSecuritySchemeReference("Bearer")` без текущего документа. Реальный HTTP GET swagger.json у отдельно запущенного Kestrel показал у всех15 protected операций:

```json
"security": [{}]
```

Пустой объект не связывает операцию с зарегистрированной схемой. Поэтому Swagger UI мог сохранить token и показывать Authorized, но не применял Bearer к операции. Старый тест проверял только длину массива — `[{}]` его удовлетворял. Прежние утверждения о проверке security не доказывали присутствие имени Bearer; дефект этой проверки явно исправлен.

## Минимальное исправление

В `src/Subscriptions.Api/SwaggerContract.cs` ссылка привязана к `context.Document`:

```csharp
new OpenApiSecuritySchemeReference("Bearer", context.Document)
```

Требование добавляется для `IAuthorizeData`, если нет `IAllowAnonymous`. Глобальный обязательный Bearer не вводился; public auth/health не помечены protected. Program.cs, регистрация/валидация JWT, routes, middleware, Domain/Application/Infrastructure не менялись. Архитектура не переделывалась.

В фактическом JSON после исправления у каждой protected операции строго:

```json
"security": [{"Bearer": []}]
```

Это метаданные, по которым штатный Swagger UI показывает замок и добавляет Authorization header. Визуальный browser click-through не использовался как доказательство: проверены фактический сериализованный JSON и реальные HTTP requests, перечисленные ниже.

## Регрессионные тесты

Новый `OpenApiSecurityTests.cs`:

- `ProtectedOperations_SerializeNamedBearerRequirementWithoutAnonymousAlternative`: все15 protected operations должны иметь ровно один requirement с ключом Bearer и пустым списком scopes; `[{}]` и anonymous alternative запрещены.
- `PublicOperations_RemainAnonymous_WithRegisteredHttpBearerScheme`: HTTP scheme registered correctly, глобальная security отсутствует,6 auth операций иhealth не требуют Bearer.

Существующий `OpenApiAndSwaggerExposeSchemasSecurityAndIdempotencyHeader` усилен проверкой ключа Bearer вместо непустого security array.

До исправления: новые2 tests дали1failed/1passed; ошибка protected test зафиксирована на всех15 operations. После исправления весь solution: **155passed,0failed,0skipped** —104 unit,51 вApi.Tests (47HTTP/API +4 infrastructure). Предыдущий полный прогон21.11 содержал153cases; добавлены2 регрессионных случая. Coverage бизнес-логики здесь повторно не измерялся: код бизнес-логики не менялся, сохранённый coverage остаётся результатом21.11.

## Реальные команды и проверки

Из part2:

```bash
dotnet test tests/Subscriptions.Api.Tests --filter FullyQualifiedName~OpenApiSecurityTests
dotnet build
dotnet test --no-build --logger trx \
  --results-directory artifacts/swagger-bearer-2026-11-26/test-results
python3 artifacts/swagger-bearer-2026-11-26/verify-runtime.py after
```

Фактически команды запускались через `scripts/run.py` с сохранением stdout/stderr/exit code. Build0errors/0warnings; полный test exit0. `verify-runtime.py` действительно запускает `dotnet run --project src/Subscriptions.Api --no-build --no-launch-profile` в Development на **http://127.0.0.1:5091**, создаёт отдельную временную SQLite, читает HTTP swagger.json, проверяет защищённые/публичные операции и выполняет login/create. Порт5091 выбран отдельно от пользовательского5087; пользовательская БД не использовалась. После проверки собственный процесс остановлен и временная БД удалена.

| Проверка | Фактический результат |
|---|---|
| Bearer definition | type=http, scheme=bearer, bearerFormat=JWT |
| Protected operation requirements |15/15 содержат `[{"Bearer":[]}]` |
| Public operations |7/7 без обязательного security; глобального requirement нет |
| GET health без токена |200 |
| POST login без токена |200 |
| POST subscriptions без Authorization |401 |
| POST subscriptions с Bearer от login |201 |

Token/password остаются в памяти runtime helper, в логи/артефакты не выводятся. Полный фактический OpenAPI сохранён без секретов: [до](../artifacts/swagger-bearer-2026-11-26/before-swagger.json), [после](../artifacts/swagger-bearer-2026-11-26/after-swagger.json). Команды, red/green TRX, server logs, runtime summaries и integrity proof — в той же папке; итог — [summary.json](../artifacts/swagger-bearer-2026-11-26/summary.json).

## Как проверить вручную

1. Перезапустить свой backend, чтобы работающий процесс загрузил новую сборку.
2. Обновить Swagger UI, при необходимости с очисткой кэша.
3. Выполнить новый login и скопировать только `accessToken`, без кавычек и без `Bearer `.
4. Authorize → Bearer → вставить token → Authorize → Close.
5. У защищённой операции проверить замок и в Execute/curl заголовок `Authorization: Bearer ...`.
6. Создать валидную подписку —201; без токена тот же маршрут —401. Login/register/health остаются публичными.

Development signing key меняется после restart; старый token может быть недействителен независимо от статуса Authorized. Поэтому после перезапуска нужен новый login. Ключи/JWT не помещать в отчёт или скриншоты.

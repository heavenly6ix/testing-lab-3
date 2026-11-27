# Практическая №3 — вторая часть: Subscriptions API

Реализован локальный backend F01–F15 по принятому ТЗ: auth/reset, подписки, ручные платежи и audit/idempotency, расходы и календарный прогноз. Реальных списаний/карточных реквизитов/FX нет. Первая часть не менялась. Основной документ — [PART2_REPORT.md](PART2_REPORT.md); [API-контракт](docs/api-contract.md), [трассировка](docs/requirements-traceability.md), [ревью](docs/code-review.md).

## Swagger Authorize — исправление 26.11.2026

После успешного login скопировать **только accessToken**, без префикса `Bearer`, открыть Authorize → Bearer → Authorize. Для защищённых операций Swagger UI должен показывать замок и автоматически добавлять `Authorization: Bearer <accessToken>`; auth endpoints и health остаются публичными. При restart Development ephemeral signing key меняется: выполнить login заново и повторно авторизовать Swagger, а не использовать прежний token. Перезапустить уже работающий backend и обновить страницу Swagger после применения исправления.

Причина дефекта: `OpenApiSecuritySchemeReference("Bearer")` без host document сериализовался в `security: [{}]`. Сам статус Authorized подтверждал лишь сохранение токена в UI. Исправлено на `OpenApiSecuritySchemeReference("Bearer", context.Document)` у операций с Authorize без AllowAnonymous. Регистрация HTTP Bearer и серверная JWT-проверка не менялись. Теперь operation security строго `[{"Bearer":[]}]`, public security отсутствует/пустой; глобальный обязательный Bearer не добавлен.

[Протокол исправления](docs/swagger-bearer-fix.md); фактические JSON до/после и новые логи находятся в `artifacts/swagger-bearer-2026-11-26/`. Исторические результаты `artifacts/final` от21.11 сохранены, не подменены новым прогоном. Предыдущий OpenAPI-тест проверял только длину массива, поэтому не замечал пустой объект; он усилен, добавлены проверки всех15 защищённых и7 публичных операций.

## Требования окружения

.NET10 SDK (фактически10.0.401/runtime10.0.12), macOS (проверено15.7 arm64), доступ NuGet для restore; Python3 и curl для smoke. NUnit/Moq/Sqlite/Argon2id входят как PackageReference, глобально устанавливать их не нужно. Docker опционален: здесь CLI есть, daemon недоступен, Linux не проверен.

Из папки `part2`:

```bash
dotnet restore
dotnet build
dotnet test
# Либо полный clean → restore → build → test, с логами (удаляет только part2 bin/obj):
python3 scripts/verify.py
```

Отдельно:

```bash
dotnet test tests/Subscriptions.Application.Tests
dotnet test tests/Subscriptions.Api.Tests
```

Unit проект не использует HTTP/настоящую БД/email. Api.Tests использует настоящий ASP.NET pipeline и временные отдельные SQLite-файлы, также содержит4 прямых infrastructure tests (Argon2id/secrets/unique/mailbox). Фактическое число cases берётся из TRX, не равно числу методов с TestCase.

## Запуск и БД

```bash
dotnet run --project src/Subscriptions.Api
```

Launch profile явно задаёт **Development и HTTP loopback**. Фактически проверенный URL: [http://127.0.0.1:5087](http://127.0.0.1:5087). [Swagger UI](http://127.0.0.1:5087/swagger/index.html), [OpenAPI JSON](http://127.0.0.1:5087/swagger/v1/swagger.json), [health](http://127.0.0.1:5087/api/v1/health). После завершения проверок процесс останавливается, URL — адрес для повторного запуска, а не внешняя публикация.

SQLite schema создаётся автоматически при **первом DB-запросе**, например health/register. Используются CREATE TABLE/INDEX IF NOT EXISTS, учебный аналог EnsureCreated; production migrations не заявлены. Default path: `src/Subscriptions.Api/data/subscriptions.db` при `dotnet run`. Decimal хранится в JSON text и суммируется в .NET, не SQLite REAL. При недоступном пути сервер остаётся запущенным и возвращает503; после исправления пути следующие запросы работают.

Для явного запуска без launch profile и с отдельной БД:

```bash
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS=http://127.0.0.1:5087 \
Database__Path="$PWD/.local/subscriptions.db" \
dotnet run --project src/Subscriptions.Api --no-launch-profile
```

Настройки environment: `Database__Path`, `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience`, `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_URLS`. В Development отсутствующий signing key создаётся в памяти и не логируется: restart инвалидирует прежние access JWT, хотя persistent refresh могут обновиться до истечения сессии. Вне Development требуется внешний `Jwt__Key`≥32 bytes, не хранить в репозитории/отчёте; HTTPS/TLS и real email sender относятся к внешнему deployment. Авторизация access15min, refresh абсолютный30days, reset15min.

Local MemoryEmailSender удерживает test-mail с token/expiry и примером client reset-link, доступен только DI тестов. Публичного inbox нет, почта реально не отправляется; frontend по адресу примера не реализован. Reset API реально проверен через pipeline, включая expiry/single-use/revocation. Нельзя выдавать пример client URL за работающий UI.

Reset **только development DB**, при остановленном сервере и если не задан другой Database__Path:

```bash
rm -f src/Subscriptions.Api/data/subscriptions.db \
      src/Subscriptions.Api/data/subscriptions.db-shm \
      src/Subscriptions.Api/data/subscriptions.db-wal
```

Для custom path удалять только свой соответствующий dev-файл. Backups/production DB эта команда не обслуживает.

## Реальные smoke через curl

В отдельном терминале при работающем сервере:

```bash
python3 scripts/smoke.py --base http://127.0.0.1:5087 --out artifacts/manual-smoke
# Короткие ручные проверки без credentials:
curl -i http://127.0.0.1:5087/api/v1/health
curl -i http://127.0.0.1:5087/api/v1/subscriptions
curl -s http://127.0.0.1:5087/swagger/v1/swagger.json
```

Runner выполняет31 настоящий curl-request, создаёт уникальных тестовых пользователей, проверяет ожидаемые statuses, correlation и важные body assertions. Credentials/Bearer передаются curl через stdin; в evidence токены/password явно REDACTED. Не копировать `[REDACTED]` как пароль: повторять runner либо задать собственные test credentials. Несколько повторов расходуют IP-limit30 login/15min, это ожидаемая защита. Не запускать smoke против чужого production API.

[Сохранённые smoke результаты](docs/smoke-tests.md), `artifacts/final/smoke/*.json`, runtime лог `artifacts/final/server.log`. API tests не выдаются за curl-smoke.

## Coverage

```bash
dotnet test tests/Subscriptions.Application.Tests --settings coverage.runsettings \
  --collect 'XPlat Code Coverage' --logger trx \
  --results-directory artifacts/recheck-coverage
dotnet tool install dotnet-reportgenerator-globaltool --tool-path .tools --version 5.6.1
.tools/reportgenerator '-reports:artifacts/recheck-coverage/**/coverage.cobertura.xml' \
  '-targetdir:artifacts/recheck-html' '-reporttypes:Html;JsonSummary;TextSummary'
```

Если tool уже установлен, повторная install не нужна. Не добавлять --allow-roll-forward: в проверенной версии5.6.1 это вызвало конфликт runtimeconfig. После сдачи `.tools` удалён как бинарный инструмент; HTML уже сохранён.

[Итог](artifacts/coverage/SUMMARY.md), [HTML](artifacts/coverage/html/index.html), Cobertura/OpenCover и summary.json — в artifacts/coverage. В знаменателе вся ручная Application logic; Contracts DTO/auto props исключены, Api/Infrastructure/пассивные Domain records не выдаются за бизнес-охват. [Методика и ограничения](docs/coverage-method.md). Line не называется statement: точная statement/instruction метрика not directly available. Analytics/Forecast оцениваются отдельно ≥90% branches.

`python3 scripts/summarize.py` пересчитывает **сохранённые финальные** тесты/coverage/smoke, не результаты произвольного recheck-run.

## Docker и эксплуатационные границы

Подготовлен Dockerfile. Реальный docker build здесь завершился ошибкой недоступного daemon (`artifacts/20-docker/build.log`). Только **containerization prepared**, не Linux verified. Когда Docker доступен:

```bash
docker build -t subscriptions-lab:part2 .
docker run --rm -p 127.0.0.1:5087:8080 \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -v subscriptions-lab-data:/data subscriptions-lab:part2
```

Этот пример — локальный Development, внешний TLS не проверяет. N05 performance, R01 availability, R02 backup/PITR, R03 RPO/RTO сохранены, но не реализованы/не испытаны как production guarantees. IP-state одного процесса теряется при restart; distributed limiter не заявлен. Доступность mail/TLS/Docker не выдумывается.

Сдавать source/tests/csproj/sln/docs/README/report и необходимые artifacts. Bin/obj/.tools/временные DB удаляются **после** всех проверок, восстанавливаются командами выше. Word-отчёт не создан, как и просил пользователь.

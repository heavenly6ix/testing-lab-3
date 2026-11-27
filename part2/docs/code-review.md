# Ревью реализации второй части

Ассистент Codex / ChatGPT, выполнение по реальному P002. Проверены production sources, unit/API/infrastructure tests, контракт и фактический OpenAPI. Это отдельный анализ после реализации, не пересказ требований. Подтверждения дефектов сохранены красными TRX/log; после изменений выполняются зелёные прогоны.

| Problem | Risk | Fix |
|---|---|---|
| Login повторно применял minimum password length регистрации | Неверный короткий пароль давал400 вместо401, что меняет F02 и учёт неудач | Login_ShortWrongPassword_IsStillUnauthorized сначала упал (04-auth/tests-red, 06-business/tests-red); вход допускает непустой пароль≤128, policy12–128 только register/reset |
| Первый вариант этого исправления использовал Text.Trim для проверки пароля | Валидный пароль из12 пробелов регистрировался, но не мог войти; пробелы значимы | PasswordWhitespaceIsSignificantNotTrimmed упал в10-integration/all-initial; заменено проверкой Length без trim |
| Канонизация суммы idempotency использовала0.## | Изменённый payload10.001 округлялся к10 и ошибочно возвращал replay | ChangedFractionalPayloadMustNotBecomeReplay упал в06-business/tests-red; G29 invariant сохраняет значение и убирает только незначащие trailing zeros |
| Команды без DTO принимали и молча игнорировали JSON-body | Body с неизвестным userId не отклонялся, хотя контракт запрещает лишний ввод | BodylessCommandsDoNotSilentlyAcceptUnknownPayload: ожидалось400, было200 (13-review/bodyless-red); transport проверяет наличие IAcceptsMetadata и отклоняет body у bodyless route |
| Public accessExpiresAt имел доли секунды, JWT exp — целые секунды | Клиент получал expiry чуть позже реальной границы JWT | AdvertisedAccessExpiryEqualsSignedJwtExpiry упал в13-review/expiry-red; expiry округляется вниз к JWT Unix-seconds до выдачи и подписи, предел≤15min сохранён |
| Eager database initialization в Program падала при unavailable path | R05 соблюдался после старта, но при недоступной БД в момент старта процесс не поднимался | StartupWithUnavailableDatabaseStillServes503 сначала завершился StorageUnavailableException (13-review/startup-db-red); схема создаётся лениво при первом DB request, сервер отдаёт503 и восстанавливается после исправления пути |
| DateOnly.MinValue ошибочно означал «поле не задано» | Запрещалась валидная0001-01-01, вопреки отсутствию произвольной нижней границы истории | Три CalendarBoundaryTests упали в13-review/calendar-red; default-value guards удалены, обязательность остаётся в JSON-required/query binding. Отрицательные тесты, закреплявшие запрет MinValue, исправлены; real API/SQLite min-date и missing query tested |
| Исходные pinned transitive OpenAPI/SQLite имели NU1903 | Известные уязвимые версии и warnings нарушали чистоту финальной сборки | Microsoft.Data.Sqlite10.0.12, SQLitePCLRaw.bundle_e_sqlite3 3.0.5, Swashbuckle10.3.1; повторные restore/build без предупреждений; исходный лог03-scaffold/restore сохранён |
| Ошибочный assert количества OpenAPI paths17 | Тест ошибочно обвинял корректный документ из19 путей | Исправлен тест, дополнительно проверяется22 операции; это дефект теста, не backend |
| ReportGenerator installation с --allow-roll-forward создала конфликт runtimeconfig | HTML coverage не генерировался (exit147), XML сам по себе оставался корректным | Переустановлен тот же ReportGenerator5.6.1 без флага, реальный html-fixed exit0; backend не менялся |

## Проверка архитектуры и безопасности

- ApiRoutes содержит только binding, извлечение principal, вызов сервиса и HTTP-response. Body size/JSON/query/correlation — transport rules. В Application нет ASP.NET/EF; csproj ссылается только на Domain. Все алгоритмы и state transitions находятся в сервисах.
- IStore/IData, IClock, IPasswordHasher, ISecrets, ITokenIssuer и IEmailSender отделяют инфраструктуру. В unit используется реальный сервис и Moq портов, memory fake только для данных и транзакционного намерения. Реальная атомарность доказывается отдельной SQLite integration проверкой, не fake rollback.
- Scoped All(owner) и Owned(id,owner) применяются ко всем ресурсам; owner приходит только из валидированного sub. API-тест ForeignResourcesLookAbsentForAllOperations проверяет detail/update/commands/archive/payment create/history filter/update/void/audit.
- SQL параметризован, PK и unique индекс реальны. BEGIN IMMEDIATE/commit/rollback объединяет Payment/Idempotency/Audit, correction+audit. После fault injection на каждой из3 записей отсутствует весь partial result, затем тот же key успешен. Race refresh и8 одинаковых payment requests подтверждены HTTP-тестами.
- Decimal не конвертируется в double; SQLite хранит JSON text, точное суммирование в .NET. Это сознательная учебная схема, не доказательство N05. DateOnly для дней, DateTimeOffset UTC для timestamps, UTC converter выдаётZ. Anchor не меняется при clamp, annual29Feb возвращается к29 в leap year.
- Konscious.Security.Cryptography.Argon2 1.3.1 реально исполнялся в тестах: соль16bytes, случайна, hash32bytes,64MiB,t3,p4. HashVerify и случайность соли/opaque secrets проверены. N02 не заменялось PasswordHasher. Это не benchmark/security audit библиотеки.
- JWT HS256, issuer/audience/signature/lifetime, ClockSkew0 и UUID sub. Неверные signature/issuer/audience/expired/missing sub дают401. Signing key не хранится в файлах; вне Development требуется внешняя конфигурация≥32byte. В Development ephemeral key, поэтому старые access перестанут работать после restart — это явно описано в README.
- Reset/refresh хранят только digest; reset token одноразовый15min, смена пароля/token consumption/revoke всех сессий транзакционны. Local test mailbox предоставляет пример client-link с fragment, но frontend по этому адресу не реализован и реальные email не отправляются. API-процесс не предоставляет публичный inbox. Forgot возвращает одинаковый202/body для known/unknown; константное время ответа не доказано.
- Request log пишет route template/status/duration/correlation; sensitive bodies, query token, Authorization/signing key не выводятся. SensitiveValuesNeverAppearInRequestLogs проверяет реальные password/access/refresh против captured logs. Smoke evidence сознательно redacted, raw tokens остаются в памяти runner.
- Middleware приводит ошибки application pipeline к Q04; headers correlation и Retry-After проверяются. Oversize Content-Length и chunked реально протестированы. Validation/unique/programming exception не маскируются как503. Реальный неоткрываемый SQLite-path →503, после восстановления health200.

## Сохранённые ограничения, не скрытые дефекты

1. Однопроцессный IP limiter теряет состояние при restart; распределённая гарантия N04 не заявлена. Account timestamps сохраняются в SQLite, exact rolling semantics и fake-clock границы проверены. Рост истории неактивных account failure rows требует production retention, а SQLite writer gate ограничивает throughput.
2. Typed JSON Records store читает наборы в память, не имеет нормализованных SQL foreign keys и production migrations. Существование/ownership проверяются в одной транзакции; физического удаления нет. Для1M payments потребуются иной профиль запросов/индексы/нагрузочные проверки; N05 не помечено implemented.
3. Мгновенный отзыв уже выданного access не обещан; logout/reset отзывают refresh. Account5-failure lockout может использоваться для блокировки известного email — это следствие принятого N04, не DDoS-защита.
4. Реальная почта, frontend reset UI, внешний TLS, Docker/Linux runtime, availability, backups/PITR и recovery не проверены здесь. Для Docker daemon недоступен, есть фактический failed build log, Dockerfile лишь prepared.
5. Coverlet line/branch/method и OpenCover sequence points не считаются точным statement/instruction coverage. Метрика statement остаётся not directly available, несмотря на высокий line процент. Никакие ветви бизнес-кода для роста процента не исключались.


## Дополнительное ревью Swagger, 26.11.2026

| Problem | Risk | Fix |
|---|---|---|
| Bearer reference создавался без context.Document | В фактическом JSON всех15 protected операций было security:[{}]; Swagger UI не видел применимую схему, несмотря на Authorized, и не отправлял header | Reference привязан к context.Document; AllowAnonymous исключён из protected metadata; схема регистрации и JWT pipeline оставлены без изменений |
| Старый OpenAPI-тест проверял только непустой security array | [{}] проходил assert, хотя Bearer отсутствовал | Существующий assert требует ключ Bearer; новый regression проверяет все15 operation requirements без anonymous альтернатив, второй тест — HTTP Bearer definition и7 public операций |

Регрессия сначала упала на сериализованном JSON (red.log:1 failed/1 passed). Запущенный отдельно Kestrel подтвердил исходный `[{}]` (before-swagger.json). Новый фактический JSON/HTTP-проверки и итог тестов — [swagger-bearer-fix.md](swagger-bearer-fix.md). Предыдущая отметка о проверке security не доказывала корректность имени схемы; здесь это исправлено явно. Бизнес-код/серверная JWT-конфигурация не изменялись.

Техническая опора: [Swashbuckle10.3.1, operation security filter](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/blob/v10.3.1/docs/configure-and-customize-swaggergen.md#add-security-definitions-and-requirements).

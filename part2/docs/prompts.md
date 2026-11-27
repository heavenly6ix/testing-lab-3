# Журнал реальных промптов второй части

Дата: 2026-11-21 (Asia/Krasnoyarsk). Ассистент: Codex / ChatGPT. Анализ выполняет основной ассистент этой беседы, без субагентов и других ИИ.

## P001 — исходное задание пользователя

Источник: пользовательский файл «Вставленный текст.txt». Далее полное фактическое содержимое без редактирования. SHA-256 оригинала и копии будет зафиксирован в artifacts/stage1/validation.json. Это один промпт пользователя, а не 15 якобы состоявшихся запросов по числу этапов.

````text
Нужно выполнить **только вторую часть практической работы №3** по дисциплине «Тестирование и контроль качества ПО».

Главный источник задания — файл:

`практическое задание 3.pdf`

Перед началом работы обязательно открыть этот файл и ещё раз проверить требования раздела «Часть 2».

Первая часть лабораторной уже выполнена. Её не изменять.

Вторая часть должна выполняться на основе требований, разработанных мной в практической работе №2. Они приведены ниже и являются исходной спецификацией системы.

Основной стек:
- C#;
- .NET 10;
- ASP.NET Core Web API;
- NUnit;
- Moq;
- желательно SQLite для локального учебного хранения данных;
- OpenAPI/Swagger для документации API.

Работа выполняется на macOS.

Главный принцип: сначала проанализировать и уточнить требования, затем зафиксировать API-контракт, и только после этого писать backend.

Не начинать генерацию кода до завершения этапов 1 и 2.

Все реальные промпты, использованные при выполнении этой части, сохранять в:

`docs/prompts.md`

Указать, что использовался Codex / ChatGPT или фактическое название применяемого ассистента.

После каждого этапа:
1. перечислять созданные и изменённые файлы;
2. фиксировать фактические результаты;
3. запускать сборку и тесты там, где это применимо;
4. не придумывать результаты;
5. сохранять реальные логи в `artifacts`;
6. останавливаться после каждого крупного этапа и выдавать краткий статус.

---

# Исходное назначение системы

Backend-сервис предназначен для:

- регистрации и авторизации пользователей;
- управления пользовательскими подписками;
- хранения информации о фактических платежах;
- анализа фактических расходов;
- прогноза будущих платежей.

Сервис:
- НЕ выполняет реальные банковские списания;
- НЕ хранит реквизиты банковских карт;
- платежи вводятся пользователем;
- расчёты по разным валютам выполняются отдельно;
- автоматическая конвертация валют не выполняется.

---

# Исходные функциональные требования

Сохранить связь итогового ТЗ с этими идентификаторами.

## F01
API регистрации принимает email и пароль, создаёт пользователя с уникальным ID. Повторный email после нормализации регистра и пробелов должен возвращать HTTP 409 и не создавать запись.

## F02
API входа выдаёт access- и refresh-токены при корректных учётных данных. Неверные данные возвращают HTTP 401 без указания, существует ли email.

## F03
Обновление сессии выдаёт новую пару токенов и делает использованный refresh-токен недействительным. Повторное использование токена должно отклоняться.

## F04
Выход отзывает refresh-токен текущей сессии. После выхода обновление сессии этим токеном невозможно.

## F05
Восстановление пароля использует одноразовую ссылку сроком 15 минут. После успешной смены пароля все refresh-токены пользователя отзываются. Ответ запроса восстановления одинаков для известных и неизвестных email.

## F06
Авторизованный пользователь может создать подписку.

Обязательные параметры:
- название 1–100 символов;
- положительная сумма;
- валюта RUB, USD или EUR;
- период месяц или год;
- дата начала;
- дата следующего платежа;
- категория.

Некорректные данные возвращают HTTP 400 без создания записи.

## F07
API возвращает только подписки текущего пользователя.

Нужны:
- фильтр по статусу;
- фильтр по категории;
- сортировка по дате следующего платежа;
- пагинация 1–100 записей.

Попытка получить чужой объект должна выглядеть как отсутствие объекта и возвращать HTTP 404.

## F08
Пользователь может изменять:
- название;
- сумму;
- валюту;
- период;
- категорию;
- будущую дату платежа.

Изменение подписки не должно изменять уже сохранённые фактические платежи.

## F09
Подписку можно:
- приостановить;
- возобновить;
- отменить.

Статус сохраняется.

Приостановленные и отменённые подписки не входят в прогноз будущих платежей.

## F10
Удаление подписки реализуется как архивация.

Архивная подписка:
- скрывается из обычного списка;
- сохраняет историю платежей;
- её фактические платежи продолжают участвовать в аналитике расходов.

## F11
Для собственной подписки пользователь может зарегистрировать фактический платёж.

Поля:
- положительная сумма;
- валюта должна совпадать с валютой подписки;
- дата;
- статус «успешный» или «неуспешный».

Использовать `Idempotency-Key`.

Одинаковый ключ + одинаковое тело:
- возвращает первоначальный результат;
- не создаёт дубль.

Одинаковый ключ + другое тело:
- HTTP 409.

## F12
История платежей доступна с:
- фильтром по подписке;
- фильтром по статусу;
- диапазоном дат;
- пагинацией.

Возвращаются только платежи текущего пользователя.

## F13
Платёж можно:
- исправить;
- аннулировать.

Необходимо сохранять:
- автора изменения;
- время;
- предыдущие значения.

Аннулированный платёж не участвует в аналитических итогах.

## F14
Аналитика за период возвращает:

- сумму успешных неаннулированных платежей;
- разбивку по месяцам;
- разбивку по категориям;
- разбивку по подпискам;
- результаты отдельно для каждой валюты.

Пустой период возвращает нулевые итоги.

## F15
API прогноза показывает платежи активных подписок на ближайшие 30 дней.

Ответ содержит:
- дату;
- подписку;
- сумму;
- валюту;
- итоги по валютам.

Для исходного дня 29–31:
если такого дня в месяце нет, используется последний день этого месяца;
в следующем месяце расчёт снова должен выполняться от исходного расчётного дня.

---

# Нефункциональные требования

## N01
Внешний API должен поддерживать HTTPS с TLS 1.2+.

Для локальной разработки допускается HTTP localhost, если отдельно указано, что это development-режим.

## N02
Пароли не хранить в открытом виде.

Использовать Argon2id с индивидуальной случайной солью не менее 16 байт, если это практически реализуемо выбранной библиотекой.

Если используется другая безопасная стандартная реализация ASP.NET Core PasswordHasher, отдельно объяснить отклонение от исходного требования.

## N03
Access token ≤ 15 минут.

Refresh token ≤ 30 дней.

Проверять:
- подпись;
- issuer;
- audience;
- expiration.

## N04
Не более:
- 5 неудачных попыток входа на аккаунт за 15 минут;
- 30 попыток на IP за 15 минут.

После превышения:
HTTP 429 + `Retry-After`.

Если IP-rate-limit существенно усложняет учебную реализацию, сначала предложить способ реализации и отметить стоимость требования. Не удалять его молча.

## N05
Требование производительности:
- p95 CRUD ≤ 300 мс;
- аналитика ≤ 1 с;
- 5xx < 1%;
- 100 req/s;
- 100 параллельных клиентов;
- 1 млн платежей.

На данном этапе не заявлять выполнение этого требования без реального нагрузочного теста.

Отнести его к требованиям, требующим отдельного performance testing.

## N06
API:
- JSON UTF-8;
- `/api/v1`;
- DateTime в ISO 8601 UTC;
- даты как YYYY-MM-DD;
- денежные значения через `decimal`, не `double`.

## N07
Ограничения:
- request body ≤ 1 MiB;
- page size ≤ 100;
- аналитический диапазон ≤ 366 дней.

Документировать HTTP 400/413.

## N08
Каждый ответ должен иметь correlation ID.

Логи:
- route;
- status;
- duration;
- correlation ID.

Не логировать:
- пароль;
- токены;
- секреты;
- тело регистрации.

## N09
Проект должен быть пригоден для контейнеризации.

Добавить Dockerfile, если это не мешает основной работе.

Не заявлять успешную проверку Linux x64/arm64 без фактического запуска.

## N10
OpenAPI должен содержать:
- endpoints;
- request/response schemas;
- status codes;
- authorization.

---

# Требования качества

## Q01
Для F01–F15 должны быть предусмотрены позитивные и негативные проверки.

Не обязательно превращать все 30+ случаев именно в unit tests, если часть требований относится к HTTP/API.

Разделить:
- unit tests;
- integration/API tests;
- smoke tests.

## Q02
Модули:
- аналитики расходов;
- прогноза платежей

должны быть покрыты тестами минимум на 90% ветвей.

Обязательно проверить:
- високосный год;
- конец месяца;
- пустой период;
- разные валюты;
- аннулированные платежи.

## Q03
Бизнес-логика должна зависеть от абстракций.

Unit tests не должны требовать:
- сети;
- реальной внешней БД;
- отправки email.

## Q04
Ошибки API должны иметь единый формат:

```json
{
  "code": "...",
  "message": "...",
  "correlationId": "...",
  "fields": {}
}
```

`fields` обязательно только для ошибок валидации.

## Q05
Перед финальной сдачей:
- сборка без ошибок;
- желательно без предупреждений;
- нет доступа к чужим ресурсам;
- основные маршруты с resource ID должны проверять ownership.

---

# Требования надёжности

## R01
Availability ≥ 99.5%.

Не выдавать это за проверенный результат лабораторной без длительного мониторинга.

Описать как эксплуатационное требование.

## R02
Ежедневный backup БД и transaction log ≤ 15 минут.

Для SQLite определить реалистичную учебную интерпретацию либо отметить требование как production-level.

## R03
RPO ≤ 15 минут, RTO ≤ 60 минут.

Не заявлять выполнение без восстановления backup.

## R04
Создание платежа + idempotency key + audit должны быть атомарными.

Использовать транзакцию.

Проверить unit/integration тестом отсутствие частичного результата.

## R05
При недоступной БД:
- HTTP 503;
- не сообщать об успешной записи;
- приложение не должно падать.

---

# Этап 1. Ревью требований

Сначала провести тестирование требований средствами ИИ, как указано в `практическое задание 3.pdf`.

Найти:

- неоднозначности;
- противоречия;
- требования, которые невозможно полноценно проверить в рамках учебной лабораторной;
- требования, которым не хватает API-деталей;
- чрезмерно сложные требования;
- потенциальные конфликты между требованиями.

НЕ изменять требования молча.

Подготовить файл:

`docs/requirements-review.md`

Для каждого спорного требования использовать таблицу:

| ID | Проблема | Предлагаемое уточнение | Причина |

После анализа подготовить:

`docs/final-requirements.md`

В нём явно разделить:

1. требования, вошедшие без изменений;
2. требования, уточнённые;
3. требования, упрощённые для учебной реализации;
4. требования, оставленные как production/non-functional, но фактически не проверяемые в рамках этой работы.

Критически важно:
не удалять N05, R01–R03 и другие сложные требования только потому, что они не реализуются полностью.
Нужно честно указать границы лабораторной.

После этого остановиться и предоставить мне ревью требований.

Не переходить к генерации backend без фиксации итогового ТЗ.

---

# Этап 2. Проектирование API

На основе `docs/final-requirements.md` спроектировать API.

Создать:

`docs/api-contract.md`

Для каждого endpoint указать:

- Requirement ID;
- HTTP Method;
- Route;
- требуется ли Bearer authentication;
- path/query parameters;
- request body;
- successful response;
- error responses;
- status codes.

Минимально предусмотреть:

## Auth

- POST `/api/v1/auth/register`
- POST `/api/v1/auth/login`
- POST `/api/v1/auth/refresh`
- POST `/api/v1/auth/logout`
- POST `/api/v1/auth/password/forgot`
- POST `/api/v1/auth/password/reset`

## Subscriptions

- POST `/api/v1/subscriptions`
- GET `/api/v1/subscriptions`
- GET `/api/v1/subscriptions/{id}`
- PUT/PATCH `/api/v1/subscriptions/{id}`
- команды pause/resume/cancel
- archive/delete согласно выбранному контракту

## Payments

- POST `/api/v1/subscriptions/{subscriptionId}/payments`
- GET `/api/v1/payments`
- PUT/PATCH `/api/v1/payments/{id}`
- endpoint аннулирования платежа

## Analytics

- GET `/api/v1/analytics/expenses`
- GET `/api/v1/analytics/forecast`

Не считать эти маршруты обязательными дословно.
Если есть более чистый REST-контракт, предложить его и объяснить.

Подготовить таблицу трассировки:

| Requirement | Endpoint / Service |

для F01–F15.

После завершения остановиться и показать API-контракт.

---

# Этап 3. Создание проекта

После утверждения требований и API создать отдельную структуру второй части.

Предлагаемая структура:

```text
part2/
├── Subscriptions.sln
├── src/
│   ├── Subscriptions.Api/
│   ├── Subscriptions.Application/
│   ├── Subscriptions.Domain/
│   └── Subscriptions.Infrastructure/
├── tests/
│   ├── Subscriptions.Application.Tests/
│   └── Subscriptions.Api.Tests/
├── docs/
└── artifacts/
```

Допускается более простая архитектура:

```text
Subscriptions.Api
Subscriptions.Services
Subscriptions.Data
Subscriptions.Services.Tests
```

если она лучше подходит размеру лабораторной.

Не создавать избыточную enterprise-архитектуру.

Главные требования:
- бизнес-логика не находится в controllers;
- инфраструктура отделена;
- зависимости бизнес-логики можно mock-ировать;
- API зависит от сервисов, а не наоборот.

Использовать SQLite для локальной базы.

Настроить создание БД автоматически:
- migrations;
или
- EnsureCreated для учебного проекта, если это явно объяснено.

После создания выполнить:

```bash
dotnet restore
dotnet build
```

Сборка должна пройти до перехода дальше.

---

# Этап 4. Реализация Auth

Реализовать:

- F01 registration;
- F02 login;
- F03 refresh;
- F04 logout;
- F05 password reset.

Соблюдать:
- email normalization;
- unique email;
- password hashing;
- JWT access token;
- refresh token rotation;
- revoke refresh tokens;
- одинаковый forgot-password response.

Не отправлять реальный email.
Можно использовать abstraction `IEmailSender` и тестовую/in-memory реализацию.

После реализации:
- unit tests;
- API tests;
- build;
- test.

Зафиксировать результаты.

---

# Этап 5. Subscriptions

Реализовать F06–F10.

Модель должна поддерживать минимум:

- Id;
- UserId;
- Name;
- Amount (`decimal`);
- Currency;
- BillingPeriod;
- Category;
- StartDate;
- NextPaymentDate;
- original billing day;
- Status;
- Archived flag / status.

Проверить ownership.

Чужой ID → 404.

Проверить:
- create;
- get;
- list;
- filters;
- sort;
- pagination;
- update;
- pause;
- resume;
- cancel;
- archive.

История платежей при изменении/архивировании подписки не должна изменяться.

После этапа:
- unit tests;
- API tests;
- build;
- test.

---

# Этап 6. Payments

Реализовать F11–F13.

Платёж должен содержать минимум:

- Id;
- SubscriptionId;
- UserId;
- Amount;
- Currency;
- Date;
- Status;
- IsVoided / equivalent;
- CreatedAt;
- UpdatedAt.

Реализовать `Idempotency-Key`.

Проверить:

- same key + same payload → тот же результат;
- same key + changed payload → 409;
- different key → новая операция.

Создание:
- payment;
- idempotency record;
- audit record

должно быть транзакционным.

Реализовать:
- payment history;
- filters;
- correction;
- void/cancel;
- audit trail.

Проверить ownership.

---

# Этап 7. Analytics

Реализовать F14.

Расходы учитывают только:

- успешные;
- неаннулированные;
- платежи в диапазоне дат.

Разделять валюты.

Вернуть:
- totals by currency;
- by month;
- by category;
- by subscription.

Пустая выборка → нулевые итоги.

Границы периода определить явно и записать в API-contract.

Добавить тесты:
- пустой период;
- начало/конец диапазона;
- разные валюты;
- failed payment;
- voided payment;
- archived subscription;
- несколько категорий.

---

# Этап 8. Forecast

Реализовать F15.

Учитывать только активные подписки.

Не учитывать:
- paused;
- cancelled;
- archived.

Период прогноза:
ближайшие 30 дней.

Обязательно протестировать billing day:

- 28;
- 29;
- 30;
- 31;
- февраль обычного года;
- февраль високосного года;
- переход через конец года.

Если исходный день 31:
- февраль → последний день февраля;
- март → снова 31 марта.

Не изменять оригинальный billing day после короткого месяца.

---

# Этап 9. Единая обработка ошибок и инфраструктура

Реализовать:

- Problem/Error response;
- correlation ID;
- validation;
- request logging без секретов;
- pagination limits;
- analytics range limit;
- authorization;
- ownership.

Обработка недоступной БД должна по возможности возвращать 503.

Добавить OpenAPI/Swagger.

Проверить N06, N07, N08, N10.

---

# Этап 10. Ревью кода

После реализации провести отдельное code review.

Создать:

`docs/code-review.md`

Проверить:

1. controllers не содержат бизнес-логику;
2. нет прямых зависимостей Application/Services от EF/HTTP;
3. ownership проверяется в одном понятном месте;
4. monetary values — decimal;
5. DateTime/UTC используются последовательно;
6. calendar dates не смешиваются с timestamp;
7. исключения не раскрывают внутренние детали;
8. пароли и токены не логируются;
9. transaction boundaries корректны;
10. идемпотентность реализована атомарно;
11. сервисы пригодны для unit testing.

Для каждой найденной проблемы:

| Problem | Risk | Change |

Исправить подтверждённые проблемы.

---

# Этап 11. Запуск backend

Подготовить README.

Запуск должен быть воспроизводим примерно командами:

```bash
dotnet restore
dotnet build
dotnet run --project src/Subscriptions.Api
```

Сообщить фактический URL.

Проверить Swagger/OpenAPI.

Сохранить лог запуска в `artifacts`.

---

# Этап 12. Smoke tests

Согласно `практическое задание 3.pdf` провести ручные smoke-тесты через `curl` или Postman.

Желательно использовать curl, чтобы тесты можно было повторить.

Создать:

`docs/smoke-tests.md`

Для каждого сценария записать:

| Requirement | Request | Expected | Actual Status | Actual Result |

Минимально проверить:

1. F01 успешная регистрация;
2. F01 duplicate registration → 409;
3. F02 login;
4. F02 invalid login → 401;
5. запрос без token → 401;
6. F06 create subscription;
7. F07 list;
8. F08 update;
9. F09 pause/resume;
10. F09 cancel;
11. F10 archive;
12. F11 create payment;
13. F11 повтор idempotency request;
14. F11 conflict same key different body;
15. F12 history;
16. F13 update/void payment;
17. F14 analytics;
18. F14 empty period;
19. F15 forecast;
20. пользователь B пытается получить ресурс пользователя A → 404.

Использовать реальные команды и реальные ответы.

Не создавать фиктивные screenshots/responses.

---

# Этап 13. Unit tests бизнес-логики

Согласно PDF нужно покрыть бизнес-логику unit tests.

Не mock-ировать сам SUT.

Mock-ировать только:
- repositories;
- clock;
- token generator;
- email sender;
- infrastructure dependencies.

Тестировать Application/Services без HTTP и реальной БД.

Основные группы:

## Auth
- normalization;
- duplicate;
- invalid credentials;
- token rotation;
- revoked token;
- expired token;
- logout;
- reset token.

## Subscription
- create;
- validation;
- ownership;
- update;
- status transitions;
- archive.

## Payment
- validation;
- ownership;
- idempotency;
- conflict;
- correction;
- void;
- audit.

## Analytics
- range;
- successful only;
- void excluded;
- currency grouping;
- category grouping;
- empty results.

## Forecast
- statuses;
- 29–31;
- leap year;
- year boundary.

---

# Этап 14. Coverage

Измерить coverage именно бизнес-логики.

Не включать в знаменатель:
- migrations;
- generated code;
- Program.cs;
- DTO без логики;
- infrastructure boilerplate,
если это обосновано и отражено в отчёте.

Главная область:
Application / Services / Domain business logic.

Цель из `практическое задание 3.pdf`:

- branch coverage > 80%;
- statement coverage > 90%.

Если Coverlet показывает:
- line coverage;
- branch coverage,

НЕ называть line coverage автоматически statement coverage.

Проверить возможность получить statement/instruction coverage через другой формат или инструмент.

Если точную statement coverage получить невозможно:
- честно это указать;
- привести line coverage;
- branch coverage;
- объяснить ограничение.

Для analytics/forecast дополнительно выполнить Q02:
branch coverage ≥ 90%.

Сохранить:
- cobertura XML;
- OpenCover XML, если используется;
- HTML ReportGenerator;
- summary.

Не придумывать значения.

---

# Этап 15. Финальная трассировка требований

Создать:

`docs/requirements-traceability.md`

Таблица:

| Requirement | Implementation | Unit test | API/Smoke test | Status |

Минимально F01–F15.

Для N/Q/R:
отдельно отметить:

- implemented;
- partially implemented;
- documented only;
- requires production/load/operational testing.

Не заявлять N05, R01, R02 или R03 выполненными без соответствующего реального тестирования.

---

# Финальная проверка второй части

Выполнить:

```bash
dotnet restore
dotnet build
dotnet test
```

Проверить:

- build successful;
- 0 failed tests;
- backend запускается;
- OpenAPI доступен;
- smoke tests выполнены;
- coverage измерен;
- final requirements сохранены;
- prompts сохранены;
- code review сохранён.

Подготовить итоговый:

`PART2_REPORT.md`

В него включить:

1. анализ требований;
2. какие требования были изменены;
3. итоговое ТЗ;
4. API endpoints;
5. архитектуру;
6. реализацию;
7. результаты code review;
8. использованный AI assistant;
9. основные использованные prompts;
10. smoke tests;
11. unit tests;
12. coverage;
13. невыполненные/частично выполненные N/Q/R;
14. команды запуска;
15. что нужно перенести в итоговый отчёт.

Также создать README с инструкцией:

- restore;
- build;
- tests;
- database creation;
- backend run;
- Swagger;
- smoke test examples;
- coverage.

В сдаваемой копии не должно быть:

- `bin`;
- `obj`;
- временных логов, не нужных для отчёта;
- секретов;
- реальных JWT signing keys;
- NuGet binaries.

Можно хранить:
- SQLite database seed или migration scripts;
- artifacts с результатами тестов;
- coverage reports;
- smoke test logs.

После завершения второй части остановиться.

Не создавать итоговый Word-отчёт, пока я отдельно не попрошу.
````

## Фактический объём выполнения P001

Только этап 1: повторное чтение PDF, ревью исходных требований и предлагаемый baseline. API и backend не генерировались. Промптов на реализацию, ревью backend, исправление тестов или Word-отчёт ещё не было. Будущие примеры запросов не выдаются за реальные. Следующие уточнения пользователя будут добавляться отдельными записями по мере получения, а не задним числом.


## P002 — фактическое продолжение: выполнить оставшуюся часть полностью

Пользователь утвердил baseline и отменил промежуточные остановки. Ассистент: Codex / ChatGPT. Полный реальный запрос:

````text
Продолжи выполнение **второй части практической работы №3** по дисциплине «Тестирование и контроль качества ПО».

Этап 1 — ревью требований — уже выполнен и принят за основу.

Использовать существующие файлы:
- `docs/requirements-review.md`
- `docs/final-requirements.md`
- `docs/prompts.md`
- исходные требования пользователя

Главный источник задания — файл:

`практическое задание 3.pdf`

Перед продолжением ещё раз сверить с ним требования именно раздела «Часть 2».

Теперь нужно **выполнить оставшуюся вторую часть полностью**, без остановки после каждого этапа.

При этом:
- не возвращаться к первой части;
- не менять первую часть;
- не удалять ранее подготовленные требования;
- не придумывать результаты тестов;
- все результаты подтверждать реальными командами;
- все фактические промпты продолжать сохранять в `docs/prompts.md`;
- все логи, coverage, smoke outputs сохранять в `artifacts`.

---

# 1. Утвердить итоговое ТЗ

Использовать текущий:

`docs/final-requirements.md`

как рабочее итоговое ТЗ.

Не ждать отдельного подтверждения пользователя.

Считать предложенные уточнения принятыми для учебной реализации, кроме требований, которые уже помечены как:
- production-only;
- performance-only;
- operational;
- требует отдельного нагрузочного/эксплуатационного тестирования.

Отдельно сохранить список:
- реализуется полностью;
- реализуется частично;
- только документируется.

Не удалять N05, R01, R02, R03.

---

# 2. Создать API-контракт

Создать:

`docs/api-contract.md`

Для каждого endpoint указать:

- Requirement ID;
- HTTP method;
- route;
- authorization;
- path/query parameters;
- request JSON;
- response JSON;
- success status;
- возможные ошибки;
- error codes;
- ownership rules.

Обязательно покрыть F01–F15.

Минимальный API:

## Auth

- `POST /api/v1/auth/register`
- `POST /api/v1/auth/login`
- `POST /api/v1/auth/refresh`
- `POST /api/v1/auth/logout`
- `POST /api/v1/auth/password/forgot`
- `POST /api/v1/auth/password/reset`

## Subscriptions

- `POST /api/v1/subscriptions`
- `GET /api/v1/subscriptions`
- `GET /api/v1/subscriptions/{id}`
- `PATCH /api/v1/subscriptions/{id}`
- `POST /api/v1/subscriptions/{id}/pause`
- `POST /api/v1/subscriptions/{id}/resume`
- `POST /api/v1/subscriptions/{id}/cancel`
- endpoint архивации

## Payments

- `POST /api/v1/subscriptions/{subscriptionId}/payments`
- `GET /api/v1/payments`
- `PATCH /api/v1/payments/{id}`
- endpoint void/annul
- endpoint истории audit, если он нужен для F13

## Analytics

- `GET /api/v1/analytics/expenses`
- `GET /api/v1/analytics/forecast`

Также сделать таблицу:

| Requirement | Endpoint / Service |

для F01–F15.

---

# 3. Создать solution и архитектуру

Использовать C# / .NET 10 / ASP.NET Core.

Создать отдельную вторую часть:

```text
part2/
├── Subscriptions.sln
├── src/
│   ├── Subscriptions.Api/
│   ├── Subscriptions.Application/
│   ├── Subscriptions.Domain/
│   └── Subscriptions.Infrastructure/
├── tests/
│   ├── Subscriptions.Application.Tests/
│   └── Subscriptions.Api.Tests/
├── docs/
├── artifacts/
└── README.md
```

Допускается более простая архитектура, если она лучше подходит учебному проекту, но обязательно:

- business logic не в controllers;
- Domain/Application не зависят от HTTP;
- Application не зависит напрямую от EF Core;
- инфраструктура подключается через интерфейсы;
- сервисы можно unit-test через mock dependencies.

Использовать SQLite.

Настроить автоматическое создание локальной БД через migrations или понятный учебный механизм.

После создания выполнить:

```bash
dotnet restore
dotnet build
```

Если сборка падает — исправить до продолжения.

---

# 4. Реализовать Auth: F01–F05

Реализовать:

## F01 Registration

- email normalization;
- unique normalized email;
- duplicate → 409;
- invalid → 400.

## F02 Login

- корректные credentials → access + refresh;
- неверные → 401;
- одинаковая ошибка для неизвестного email и неверного пароля.

## F03 Refresh

- rotation refresh token;
- старый token становится недействительным;
- повторное использование → 401;
- absolute session lifetime ≤30 дней.

## F04 Logout

- revoke текущего refresh token;
- повторный logout безопасен;
- другие сессии не затрагиваются.

## F05 Password reset

- forgot-response одинаков для существующего/несуществующего email;
- test/in-memory email sender;
- reset token 15 минут;
- одноразовый;
- после reset отозвать все refresh token пользователя.

### Password hashing

Сохранять Argon2id, если есть совместимая стабильная .NET-библиотека.

Не писать собственную криптографию.

Если Argon2id невозможно разумно использовать:
- применить ASP.NET Core PasswordHasher;
- явно зафиксировать это как отклонение от N02;
- описать в `docs/code-review.md` и `requirements-traceability.md`.

---

# 5. Реализовать rate limiting N04

Реализовать учебную версию:

- account rate limit:
  - 5 неверных credentials / 15 минут;
  - следующая попытка → 429;
- IP:
  - 30 login attempts / 15 минут;
  - 31-я → 429;
- `Retry-After`.

Использовать clock abstraction для тестируемости.

Допускается in-memory IP state одного процесса, как указано в итоговом ТЗ.

Не заявлять это production distributed rate limiter.

---

# 6. Реализовать subscriptions F06–F10

Модель подписки должна содержать минимум:

- Id
- UserId
- Name
- Amount decimal
- Currency
- BillingPeriod
- Category
- StartDate
- NextPaymentDate
- OriginalBillingDay
- OriginalBillingMonth при необходимости
- Status
- Archived

Реализовать:

- create;
- get by id;
- list;
- filter by status;
- filter by category;
- sorting;
- pagination;
- update;
- pause;
- resume;
- cancel;
- archive.

Ownership обязателен.

Чужой ID → 404.

Archive:
- не удаляет запись физически;
- скрывает из обычного списка;
- сохраняет history и analytics.

---

# 7. Реализовать payments F11–F13

Модель payment:

- Id
- UserId
- SubscriptionId
- Amount
- Currency
- Date
- Status
- IsVoided
- CreatedAt
- UpdatedAt
- snapshot Name
- snapshot Category

Реализовать:

## Idempotency-Key

- обязателен;
- same key + same payload → тот же первоначальный response;
- не создаёт второй payment;
- same key + different payload → 409;
- different key → новый payment.

Создание:
- Payment;
- IdempotencyRecord;
- AuditRecord

должно выполняться транзакционно.

## History

- фильтр subscription;
- status;
- dates;
- pagination.

## Correction

Разрешить изменение допустимых полей.

Создать audit:
- actor;
- time;
- previous;
- new.

## Void

- `IsVoided=true`;
- строка не удаляется;
- повтор void идемпотентен;
- voided payment не входит в analytics.

---

# 8. Реализовать analytics F14

Расходы учитывать только:

- текущего пользователя;
- status=successful;
- `IsVoided=false`;
- date внутри включительного диапазона.

Возвращать:

- totals per currency;
- breakdown by month;
- category;
- subscription.

Не складывать RUB/USD/EUR вместе.

Пустая выборка:
- totals RUB/USD/EUR = 0;
- breakdown пустые.

Максимальный диапазон — 366 дней.

---

# 9. Реализовать forecast F15

Forecast:

- окно `[today, today+30)`;
- только Active;
- не Archived;
- Paused/Cancelled исключены.

Проверить алгоритм для:

- day 28;
- day 29;
- day 30;
- day 31;
- обычный февраль;
- високосный февраль;
- переход года;
- yearly 29 February.

Ключевое поведение:

31 Jan → 28/29 Feb → 31 Mar

То есть после clamp исходный anchor day не должен теряться.

---

# 10. Общая инфраструктура API

Реализовать:

- authorization JWT;
- validation;
- ownership;
- единый error JSON:

```json
{
  "code": "...",
  "message": "...",
  "correlationId": "...",
  "fields": {}
}
```

`fields` только для validation errors.

Каждый application-generated response:
- `X-Correlation-ID`.

Логи:
- route;
- status;
- duration;
- correlation ID.

Не логировать:
- password;
- JWT;
- refresh token;
- reset token;
- signing key;
- sensitive bodies.

N07:
- body ≤1 MiB;
- pageSize ≤100;
- analytics ≤366 days.

N10:
- Swagger/OpenAPI;
- schemas;
- responses;
- authorization scheme.

---

# 11. Обработка недоступной БД

Реализовать R05 настолько, насколько возможно в учебном проекте.

При реальной инфраструктурной DB error:

- 503;
- generic error;
- приложение не падает;
- не возвращает ложный 2xx.

Не превращать:
- validation;
- unique constraint;
- programming bugs

в 503.

---

# 12. Code review

После реализации создать:

`docs/code-review.md`

Провести ревью собственного кода.

Проверить:

- controllers thin;
- бизнес-логика вне HTTP;
- Application без EF dependency;
- repositories/clock/email/token abstractions;
- decimal;
- DateOnly vs DateTime;
- UTC;
- ownership;
- transactions;
- idempotency;
- audit;
- logging secrets;
- error handling;
- DB failure;
- testability.

Таблица:

| Problem | Risk | Fix |

Все значимые найденные проблемы исправить.

---

# 13. Unit tests бизнес-логики

Создать полноценные unit tests.

Использовать NUnit + Moq.

Не mock-ировать SUT.

Mock только dependencies.

Минимальные наборы:

## Auth
- normalization;
- duplicate;
- invalid login;
- refresh rotation;
- old refresh rejected;
- logout;
- password reset;
- expired reset;
- revoke sessions;
- account rate limit.

## Subscription
- valid create;
- invalid create;
- ownership;
- update;
- status transitions;
- archive;
- pagination/filters business rules.

## Payment
- validation;
- ownership;
- currency;
- idempotency replay;
- idempotency conflict;
- correction;
- void;
- audit;
- transaction failure.

## Analytics
- success only;
- failed excluded;
- voided excluded;
- archived included;
- dates inclusive;
- currencies separate;
- category;
- subscription;
- empty.

## Forecast
- Active only;
- paused excluded;
- cancelled excluded;
- archived excluded;
- 28/29/30/31;
- leap year;
- non-leap;
- year boundary;
- anchor preservation.

---

# 14. API/integration tests

Создать API tests через реальный ASP.NET pipeline.

Использовать отдельную тестовую SQLite database.

Проверить минимум:

- register;
- duplicate 409;
- login;
- bad login 401;
- bearer authorization;
- ownership 404;
- create subscription;
- update;
- statuses;
- archive;
- create payment;
- Idempotency-Key replay;
- Idempotency-Key conflict;
- void;
- analytics;
- forecast;
- correlation ID;
- error schema.

R04 проверить integration test:
ошибка внутри transaction → нет partial payment/idempotency/audit records.

---

# 15. Запустить backend

Выполнить:

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/Subscriptions.Api
```

Зафиксировать фактический URL.

Проверить:

- приложение действительно стартует;
- SQLite создаётся;
- Swagger UI доступен;
- OpenAPI JSON доступен.

Сохранить реальные логи запуска.

---

# 16. Smoke tests через curl

Согласно `практическое задание 3.pdf` провести реальные ручные smoke tests.

Не заменять их unit/API test.

Запустить реальный процесс API и выполнить curl.

Создать:

`docs/smoke-tests.md`

Для каждого:

| ID | Request | Expected | Actual HTTP | Actual Result |

Минимум:

1. register;
2. duplicate register →409;
3. login;
4. invalid login →401;
5. unauthorized →401;
6. create subscription;
7. get/list;
8. update;
9. pause;
10. resume;
11. cancel;
12. archive;
13. create payment;
14. same Idempotency-Key same body;
15. same key different body →409;
16. history;
17. correct payment;
18. void;
19. analytics;
20. empty analytics;
21. forecast;
22. second user access →404.

Все ответы должны быть реальными.

Сохранить команды curl и ответы в artifacts.

---

# 17. Coverage

Измерить только бизнес-логику:

- Application;
- Services;
- Domain logic.

Не включать:
- Program;
- migrations;
- generated;
- DTO без логики;
- boilerplate infrastructure.

Получить:

- line coverage;
- branch coverage;
- statement/instruction coverage, если инструмент реально предоставляет.

Цель из PDF:

- branch >80%;
- statements >90%.

Дополнительно:

- Analytics ≥90% branches;
- Forecast ≥90% branches.

Если точной statement-метрики нет:
- не называть line statement;
- явно написать `statement coverage not directly available`;
- показать line + branch.

Сохранить:

- Cobertura;
- OpenCover;
- ReportGenerator HTML;
- summary.

---

# 18. Проверить Q01

Для каждого F01–F15 должна существовать:

- минимум одна позитивная проверка;
- минимум одна негативная или граничная проверка.

Они могут быть распределены между:
- unit;
- API integration;
- smoke.

Создать матрицу:

`docs/requirements-traceability.md`

Формат:

| Requirement | Implementation | Unit Test | API Test | Smoke | Status |

---

# 19. Проверить N/Q/R

В traceability отдельно указать статус:

- implemented;
- partially implemented;
- documented only;
- requires external testing.

Не заявлять выполненными без реального теста:

- N05;
- R01;
- R02;
- R03;
- Docker Linux x64/arm64, если реально не запускалось.

Если Dockerfile создан — написать только `containerization prepared`, а не `Linux verified`.

---

# 20. Docker

Если Docker доступен:

создать Dockerfile и попытаться реально:

```bash
docker build ...
docker run ...
```

Если Docker недоступен:
- Dockerfile можно оставить;
- отметить N09 partial;
- не придумывать успешный build.

---

# 21. README

Создать:

`part2/README.md`

В нём указать:

- prerequisites;
- restore;
- build;
- test;
- database;
- run;
- фактический URL;
- Swagger;
- curl smoke example;
- coverage;
- reset development DB;
- environment configuration.

---

# 22. Финальный PART2_REPORT.md

Создать:

`PART2_REPORT.md`

В нём обязательно:

1. требования из лабораторной №2;
2. результаты их ревью;
3. какие требования уточнены;
4. итоговый API;
5. структура проекта;
6. архитектура;
7. что реализовано;
8. code review;
9. найденные и исправленные проблемы;
10. использованный AI;
11. реальные prompts;
12. smoke tests;
13. unit/API tests;
14. итоговое число tests;
15. coverage;
16. Analytics/Forecast coverage;
17. трассировка F01–F15;
18. N/Q/R status;
19. что не проверялось;
20. команды воспроизведения;
21. какие материалы нужно включить в итоговый Word-отчёт.

---

# 23. Финальная проверка

В чистом состоянии выполнить:

```bash
dotnet restore
dotnet build
dotnet test
```

Должно быть:

- build succeeded;
- 0 failed tests.

Затем реально запустить API и повторить минимальный smoke.

Перед завершением проверить, что:

- backend существует и запускается;
- API endpoints соответствуют contract;
- F01–F15 реализованы либо явно имеют статус;
- OpenAPI работает;
- SQLite работает;
- ownership работает;
- idempotency работает;
- analytics работает;
- forecast работает;
- smoke выполнен;
- coverage измерен;
- prompts сохранены;
- review сохранён.

---

# 24. Очистка сдаваемой копии

После всех проверок удалить:

- `bin`;
- `obj`;
- временные локальные DB, если они не нужны;
- реальные secrets;
- JWT signing secrets;
- NuGet cache/binaries.

Не удалять:

- csproj;
- sln;
- source code;
- tests;
- migrations;
- docs;
- README;
- coverage summary;
- необходимые artifacts подтверждения.

---

# Итоговый ответ

После завершения выдать один подробный итог:

1. созданная структура;
2. список endpoints;
3. количество unit tests;
4. количество API/integration tests;
5. smoke test result;
6. build result;
7. test result;
8. coverage line/branch/statement;
9. отдельно Analytics coverage;
10. отдельно Forecast coverage;
11. F01–F15 status;
12. N01–N10 status;
13. Q01–Q05 status;
14. R01–R05 status;
15. частично/непроверенные требования;
16. использованный AI;
17. список реальных prompts;
18. все команды, которыми я могу перепроверить результат вручную.

Не останавливайся после создания API-контракта.

Выполни вторую часть до конца.
````


## Фактический результат выполнения P002

API-контракт был записан до source; создан и проверен backend; реальные red/green regressions, unit/API/SQLite tests, запуск Kestrel и curl smoke, Coverlet/OpenCover/HTML, review/traceability/README/PART2_REPORT. Все команды и результаты — artifacts. Отдельные дополнительные пользовательские промпты не придумывались. Docker daemon оказался недоступен, statement/instruction отдельно не измерены; эти границы сохранены. Word не создавался.


## P003 — Swagger Bearer regression, 2026-11-26

Реальный запрос пользователя; воспроизведён текст сообщения с нормализацией пустых строк. Ассистент Codex / ChatGPT. Только конфигурация OpenAPI и связанные тесты/документация.

````text
При ручном тестировании второй части практической работы обнаружен дефект конфигурации Swagger/OpenAPI.

После успешного входа через POST /api/v1/auth/login я скопировал accessToken и авторизовался через кнопку Authorize. В окне авторизации отображается статус Authorized.

Однако:
- возле защищённых endpoints отсутствуют значки замка;
- Swagger не добавляет заголовок Authorization: Bearer в генерируемую команду curl;
- POST /api/v1/subscriptions возвращает HTTP 401 Unauthorized.

Проверь регистрацию Bearer SecurityScheme и применение SecurityRequirement к защищённым операциям OpenAPI. Проверь фактически генерируемый swagger.json.

Нужно исправить конфигурацию таким образом, чтобы:
- защищённые endpoints отображались со значками замка;
- Swagger автоматически передавал accessToken в заголовке Authorization;
- публичные endpoints оставались доступными без токена;
- существующая серверная авторизация и бизнес-логика не изменялись.

Добавь регрессионный тест, проверяющий наличие security requirements непосредственно у защищённых операций OpenAPI.

После исправления выполни:
1. dotnet build;
2. dotnet test;
3. запуск backend;
4. проверку фактического OpenAPI JSON.

Обнови документацию и журнал промптов с учётом найденного дефекта и новых результатов тестирования.

Первую часть лабораторной не изменять. Не переделывать архитектуру backend. Исправить только обнаруженную проблему и связанные с ней тесты.

````


### Фактическое выполнение P003

Воспроизведён реальный swagger.json с [{}]; regression сначала red. Исправлена привязка Bearer reference к context.Document, усилен существующий тест и добавлены2 новых. Build0warnings/errors;155tests passed; реальный Kestrel JSON/HTTP401→201 проверен на временной отдельной БД без сохранения tokens. Source hashes server auth/business/части1 совпадают; доказательства в artifacts/swagger-bearer-2026-11-26.

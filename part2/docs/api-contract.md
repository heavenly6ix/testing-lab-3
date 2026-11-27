# API-контракт v1 — зафиксирован до реализации

Основание: рабочее final-requirements.md, принятое P002. Контракт создаётся до backend. Далее разрешена реализация без остановок. Все пути ниже относительны к `/api/v1`; JSON UTF-8, camelCase, даты YYYY-MM-DD, timestamps UTC с Z, decimal JSON number. Unknown JSON fields, null values и malformed JSON запрещены (400). PATCH — объект с одним или более допустимых полей, пропуск означает «не менять», explicit null запрещён.

## Общие схемы

- `Credentials`: `{ "email":"student@example.test", "password":"Example-pass-123" }`.
- `User`: `{ "id":"uuid", "email":"student@example.test" }`; без hash/секретов.
- `Tokens`: `{ "accessToken":"...", "refreshToken":"...", "accessExpiresAt":"...Z", "refreshExpiresAt":"...Z" }`. Cache-Control:no-store.
- `SubscriptionInput`: `{ "name":"Music", "amount":199.99, "currency":"RUB", "billingPeriod":"monthly", "category":"Media", "startDate":"2026-01-31", "nextPaymentDate":"2026-01-31" }`.
- `SubscriptionPatch`: любые name/amount/currency/billingPeriod/category/nextPaymentDate из Input, без startDate/userId. Изменение периода требует nextPaymentDate. Новая дата > UTC today.
- `Subscription`: Input + `{ "id":"uuid", "userId":"uuid", "originalBillingDay":31, "originalBillingMonth":1, "status":"active", "archived":false }`.
- `PaymentInput`: `{ "amount":199.99,"currency":"RUB","date":"2026-01-31","status":"successful" }`.
- `PaymentPatch`: одно/несколько amount/date/status; currency/subscriptionId/owner/snapshots менять нельзя.
- `Payment`: Input + `{ "id":"uuid","userId":"uuid","subscriptionId":"uuid","isVoided":false,"snapshotName":"Music","snapshotCategory":"Media","createdAt":"...Z","updatedAt":"...Z" }`.
- `Page<T>`: `{ "items":[T],"total":1,"page":1,"pageSize":20 }`.
- `Audit`: `{ "id":"uuid","userId":"uuid","paymentId":"uuid","action":"create|correct|void","at":"...Z","previous":null,"current":Payment }`. previous при correction/void — предыдущий Payment. GET audit возвращает массив, at asc + id.
- `Expenses`: `{ "totals":{"RUB":199.99,"USD":0,"EUR":0},"byMonth":[{"key":"2026-01","currency":"RUB","amount":199.99}],"byCategory":[{"key":"Media","currency":"RUB","amount":199.99}],"bySubscription":[{"key":"uuid","currency":"RUB","amount":199.99}] }`.
- `Forecast`: `{ "from":"YYYY-MM-DD","toExclusive":"YYYY-MM-DD","items":[{"date":"...","subscriptionId":"uuid","name":"Music","amount":199.99,"currency":"RUB"}],"totals":{"RUB":199.99,"USD":0,"EUR":0} }`.
- `Error`: `{ "code":"validation","message":"Invalid request.","correlationId":"...","fields":{"amount":["..."]} }`. fields только validation 400. Остальные code: unauthorized(401), forbidden(403), not_found(404), duplicate_email/idempotency_conflict/invalid_state(409), body_too_large(413), rate_limited(429), unavailable(503), internal_error(500). Invalid reset token —400 validation с одинаковым сообщением.

Ограничения: name trimmed1–100, category trimmed1–50, деньги >0≤999999999.99 с ≤2 ненулевыми дробными знаками, currency RUB/USD/EUR, billingPeriod monthly/yearly, status subscription active/paused/cancelled, status payment successful/failed. page≥1, pageSize1–100 (defaults1/20), ID UUID; invalid ID/query/body→400. Anonymous protected route→401 (WWW-Authenticate:Bearer); valid JWT чужого владельца→404. Каждый app response X-Correlation-ID; ошибочный JSON correlationId совпадает. 429 Retry-After целые seconds≥1. Body >1048576 bytes→413, включая без Content-Length.

## Endpoints

Во всех защищённых операциях user берётся из JWT, не из request. Общие ошибки:400 validation/query/UUID, 401 (если Bearer), 413 при теле, 503 DB unavailable, 500 unexpected; эти ответы используют Error. В таблице перечислены дополнительные/основные ошибки. Authorization `public` означает без Bearer; refresh/reset являются самостоятельными секретами. Все timestamp/token bounds — итоговое ТЗ.

| ID | Method + route | Auth | Path/query | Request JSON / header | Success + JSON | Дополнительные ошибки / ownership |
|---|---|---|---|---|---|---|
| F01 | POST /auth/register | public | — | Credentials | 201 User | 400 validation;409 duplicate_email; unique normalized email |
| F02,N04 | POST /auth/login | public | — | Credentials | 200 Tokens | 401 unauthorized одинаковый known/unknown;429 rate_limited; IP/account rolling15min |
| F03 | POST /auth/refresh | public | — | `{"refreshToken":"..."}` | 200 Tokens | 401 unauthorized для invalid/expired/used/revoked; atomic rotation |
| F04 | POST /auth/logout | public | — | `{"refreshToken":"..."}` | 204 без JSON | Повтор/неизвестный корректный токен204; только предъявленная session chain отзывается, другая session жива |
| F05 | POST /auth/password/forgot | public | — | `{"email":"student@example.test"}` | 202 `{"message":"If the account exists, recovery instructions have been sent."}` | Синтаксически валидный unknown тот же ответ; реальных email нет |
| F05 | POST /auth/password/reset | public | — | `{"token":"...","password":"New-example-123"}` | 204 | invalid/used/expired400 validation, без различения; отзывает все refresh |
| F06 | POST /subscriptions | Bearer | — | SubscriptionInput | 201 Subscription, Location | 400 validation; владельца задаёт principal |
| F07 | GET /subscriptions | Bearer | status?,category?,includeArchived=false,page=1,pageSize=20 | — | 200 Page<Subscription> | status/category точные, next date asc+id; только свои |
| F07 | GET /subscriptions/{id} | Bearer | id UUID | — | 200 Subscription | 404 not_found; own archived доступна |
| F08 | PATCH /subscriptions/{id} | Bearer | id UUID | SubscriptionPatch | 200 Subscription | 404 foreign/missing;409 invalid_state cancelled/archived;400 validation |
| F09 | POST /subscriptions/{id}/pause | Bearer | id UUID | нет тела | 200 Subscription | 404;409 invalid_state для cancelled/archived; повтор paused безопасен |
| F09 | POST /subscriptions/{id}/resume | Bearer | id UUID | нет тела | 200 Subscription | 404;409 cancelled/archived; повтор active безопасен |
| F09 | POST /subscriptions/{id}/cancel | Bearer | id UUID | нет тела | 200 Subscription | 404;409 archived; повтор cancelled безопасен |
| F10 | DELETE /subscriptions/{id} | Bearer | id UUID | — | 204 | archive flag, не физическое удаление; foreign404; repeat204 |
| F11,R04 | POST /subscriptions/{subscriptionId}/payments | Bearer | subscriptionId UUID | PaymentInput; Idempotency-Key:1–128 ASCII без пробелов | 201 Payment + Location; replay тот же201/body | 404 foreign/missing,409 idempotency_conflict,400 validation/currency/key. Повтор проверяет original payload до текущей currency validation, возвращает snapshot первоначального ответа |
| F12 | GET /payments | Bearer | subscriptionId?,status?,from?,to?,page=1,pageSize=20 | — | 200 Page<Payment> | own only, from/to включены, date desc+id; foreign subscription404; void/archived history сохраняется |
| F13 | PATCH /payments/{id} | Bearer | id UUID | PaymentPatch | 200 Payment | 404 foreign/missing;409 invalid_state voided; validation400. Старая currency не сравнивается с новой currency подписки |
| F13 | POST /payments/{id}/void | Bearer | id UUID | — | 200 Payment | 404; repeat200 без второго audit; correction после void409 |
| F13 | GET /payments/{id}/audit | Bearer | id UUID | — | 200 Audit[] | 404 foreign/missing; все actor/time/before/after доступны только владельцу |
| F14 | GET /analytics/expenses | Bearer | from,to обязательны; inclusive≤366days | — | 200 Expenses | invalid/reversed/too long400; own successful !voided; archived included |
| F15 | GET /analytics/forecast | Bearer | нет | — | 200 Forecast | UTC [today,today+30), active !archived; original day/month anchor не мутирует |
| R05 | GET /health | public | — | — | 200 `{"status":"ok"}` | 503 unavailable при DB failure; не свидетельство availability99.5% |

Несуществующий route→404 not_found, неподдерживаемый method→405 method_not_allowed; ошибки framework приводятся к общей схеме в пределах app pipeline. Query, которого нет в контракте, отклоняется400, а не используется как UserId. Дата reset link относится к локальному тестовому sender, публичного inbox endpoint нет. Из исходного minimum добавлен только audit read, health — проверка доступности БД, не production monitoring.

## Трассировка F01–F15

| Requirement | Endpoint / Service |
|---|---|
| F01 | register / AuthService.Register |
| F02 | login / AuthService.Login |
| F03 | refresh / AuthService.Refresh |
| F04 | logout / AuthService.Logout |
| F05 | forgot/reset / AuthService.Forgot, Reset |
| F06 | POST subscriptions / SubscriptionService.Create |
| F07 | GET subscriptions/list/detail / SubscriptionService.Get, List |
| F08 | PATCH subscription / SubscriptionService.Update |
| F09 | pause/resume/cancel / SubscriptionService.ChangeStatus |
| F10 | DELETE subscription / SubscriptionService.Archive |
| F11 | POST payment / PaymentService.Create + transactional store |
| F12 | GET payments / PaymentService.List |
| F13 | PATCH/void/audit / PaymentService.Update, Void, Audit |
| F14 | expenses / AnalyticsService.Expenses |
| F15 | forecast / ForecastService.Forecast |

OpenAPI будет строиться из реальных typed endpoints с request/response/error schemas и Bearer security; после реализации фактический документ сравнивается с этой таблицей. Контракт не заменяется предположениями о полученных ответах. Согласие на продолжение без остановок дано пользователем в P002.


## Уточнение проверки Bearer, 26.11.2026

Для каждой из15 защищённых HTTP-операций сериализованный operation-level JSON обязан содержать `"security":[{"Bearer":[]}]`. Недостаточно наличия `components.securitySchemes.Bearer` или непустого массива `[{}]`. Ни пустой объект как anonymous alternative, ни обязательный глобальный Bearer не допускаются. У6 auth endpoints иhealth security отсутствует или `[]`. Scheme — type=http, scheme=bearer, bearerFormat=JWT. Модель доступа и server auth не менялись; исправлена только ссылка схемы на текущий документ. Подтверждение — docs/swagger-bearer-fix.md и actual after-swagger.json.

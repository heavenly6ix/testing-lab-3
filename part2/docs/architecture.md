# Архитектура

Domain — пассивные immutable records подписки/факта/аудита/сессии. Application — валидация и бизнес-сервисы Auth, Subscription, Payment, Analytics, Forecast; порты IStore/IData, IClock, IPasswordHasher, ITokenIssuer, ISecrets, IEmailSender. Нет EF/HTTP/NuGet dependencies в Application.

Infrastructure — Microsoft.Data.Sqlite, Argon2id через Konscious, JWT, системные часы/криптографические secrets и локальный mailbox. Api — тонкие typed endpoints, transport validation, JWT Bearer, единая ошибка/correlation/logging/body limit/OpenAPI.

SQLite использует простую таблицу Records(Kind,Id,UserId,UniqueKey,Json). PK(Kind,Id), unique(Kind,UniqueKey). Это учебный typed document-store поверх SQLite, не EF-модель и не масштабируемая схема для N05. Query по Kind/UserId ограничивает owner; ID-проверки в Application. Точное decimal хранится в JSON без SQLite REAL-арифметики; суммы вычисляются decimal в сервисах. БД создаётся идемпотентным CREATE TABLE/INDEX IF NOT EXISTS, аналог учебного EnsureCreated, не системой production migrations.

IStore.Read выполняет согласованное чтение, IStore.Write — BEGIN IMMEDIATE/commit или rollback на любой exception. Несколько записей payment/key/audit выполняются одним IData одной транзакции; SQLite serializes writers, unique защищает конечный инвариант. IWriteObserver используется только DI integration fault injection после реального write, не публичный API и не конфигурация production. Нагрузка/несколько реплик не заявлены.

Пароли Argon2id (salt16,64MiB,iterations3,parallelism4,hash32), digest refresh/reset — SHA256 криптографически случайного opaque32-byte секрета. JWT HS256 ключ≥32byte из окружения; Development может создать ephemeral в памяти, ключ не печатается/не сохраняется. Tests заменяют hash/token infrastructure в unit, реальные реализации проверяются integration.

Источники решения: [Argon2 library](https://github.com/kmaragon/Konscious.Security.Cryptography), [SQLite transactions](https://sqlite.org/lang_transaction.html), [Swashbuckle v10](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/blob/master/docs/migrating-to-v10.md). Совместимость и корректность будут подтверждены локальными сборками и тестами, не только документацией.

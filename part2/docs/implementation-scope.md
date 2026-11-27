# Рабочий объём после P002

Полностью реализуется локально: F01–F15; N02 Argon2id, N03 token validation/lifetime, N06 formats, N07 limits, N08 correlation/logging, N10 OpenAPI; Q01/Q03/Q04/Q05; R04 transaction и R05 классификация инфраструктурных ошибок. Q02 и бизнес-coverage подтверждаются только фактическим замером, не планом.

Частично/с границами: N01 HTTP localhost Development (внешний TLS отдельно); N04 exact rolling account/IP одного процесса, IP-state in-memory и reset при рестарте; N09 Dockerfile и попытка build/run при доступном daemon. Нет claims о multi-node limiter, real-email delivery, внешнем TLS или неподтверждённой архитектуре Docker.

Только документируется, требуется отдельное испытание: N05 (все исходные численные пределы сохранены), R01 availability99.5%, R02 daily backup/transaction-log≤15min и возможный SQLite snapshot, R03 RPO15/RTO60. Здесь не реализуются scheduler backups/production PITR и не имитируются нагрузка/мониторинг/восстановление.

Изменения требований не удаляют их ID. Фактический финальный статус каждого ID и доказательства отражаются отдельно в requirements-traceability.md.

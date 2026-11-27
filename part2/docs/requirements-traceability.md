# Трассировка требований и фактические статусы

Источник: принятое рабочее ТЗ и P002. **Implemented** ниже означает локально реализованное и проверенное поведение, не production certification. Полный перечень реальных test cases/outcomes — artifacts/final/summary.json и tests TRX; curl — artifacts/final/smoke. Unit≠API≠smoke. Названия без параметров обозначают весь TestCase-набор. Все файлы кода относительны src/Subscriptions.Application, если не указано иначе.

## F01–F15 и Q01

| Requirement | Implementation | Unit Test (positive / negative) | API Test | Smoke | Status |
|---|---|---|---|---|---|
| F01 | AuthService.Register; SQLite unique normalized email | Registration_NormalizesAndRejectsDuplicate; Registration_InvalidEmail; Registration_InvalidPassword | RegistrationNormalizationDuplicateAndValidation; ConcurrentNormalizedRegistrationsCreateOneUser | register201,duplicate409 | implemented |
| F02 | AuthService.Login, Argon2id/JwtIssuer | Login_CorrectIssuesPairWithBounds; Login_InvalidCredentials_AreUniform; Login_ShortWrongPassword_IsStillUnauthorized | LoginErrorsAreUniformAndBearerRequired; AdvertisedAccessExpiryEqualsSignedJwtExpiry | login200,invalid-login401 | implemented |
| F03 | AuthService.Refresh/NewSession, transactional records | Refresh_RotatesAndPreservesAbsoluteExpiry; Refresh_InvalidStateRejected | RefreshRaceRotationLogoutRevokesChainNotOtherSessions | —, unit/API | implemented |
| F04 | AuthService.Logout, revoke whole presented session chain | Logout_IdempotentRevokesSessionChainOnly (repeat/unknown/other session) | RefreshRaceRotationLogoutRevokesChainNotOtherSessions | —, unit/API | implemented; issued access expires naturally |
| F05 | AuthService.Forgot/Reset, MemoryEmailSender, digest reset15min | Reset_ChangesPasswordConsumesTokenRevokesAllSessions; Reset_InvalidToken; Forgot_UnknownDoesNotSend_NewRequestInvalidatesPrevious | ResetSenderExpiryOneUseAndAllRefreshRevoked; MailboxExpiresAndReplacesPerEmailWithoutPublicInbox | —, unit/API | backend implemented; fake delivery accepted; frontend/real email out of scope |
| F06 | SubscriptionService.Create, validation Rules | Create_TrimsAndCapturesAnchor; InvalidCreationWritesNothing | SubscriptionCrudFiltersStatusAndArchive; InvalidCreateAndMissingKeyDoNotWrite | create-subscription201 | implemented |
| F07 | SubscriptionService.Get/List, Owned/SQL owner scope, stable paging | List_FiltersStablePaginationArchiveAndOwner; OwnershipAllSubscriptionOperations; InvalidPagination; InvalidStatusFilter | ForeignResourcesLookAbsentForAllOperations; InvalidQueriesHaveUnifiedValidation | get/list200,foreign-resource404 | implemented |
| F08 | SubscriptionService.Update, schedule anchors, immutable Payment snapshots | UpdateAllFieldsAndAnchor_PreservesHistoricalPayment; InvalidUpdateDoesNotChangeSavedObject | SubscriptionCrudFiltersStatusAndArchive; PaymentReplayCorrectionVoidAuditAndHistoricalCurrency | update200 | implemented |
| F09 | SubscriptionService.ChangeStatus | StatusTransitionsArePersistentAndIdempotent; ArchivedIsReadOnlyButHistoryStays | SubscriptionCrudFiltersStatusAndArchive; AnalyticsAndForecastUseOwnCurrencySnapshotsAndArchiveRules | pause/resume/cancel200,cancelled-resume409 | implemented |
| F10 | SubscriptionService.Archive, no physical delete | ArchivedIsReadOnlyButHistoryStays; OwnershipAllSubscriptionOperations | SubscriptionCrudFiltersStatusAndArchive; AnalyticsAndForecastUseOwnCurrencySnapshotsAndArchiveRules | archive204,archived-history200 | implemented |
| F11 | PaymentService.Create, IdempotencyRecord, IStore.Write | CreateAtomicTripletAndOriginalResponseReplay; DifferentKeyCreatesNew_ChangedPayloadConflicts; ChangedFractionalPayloadMustNotBecomeReplay; InvalidCreateLeavesNoTriplet | PaymentReplayCorrectionVoidAuditAndHistoricalCurrency; ConcurrentSameKeyCreatesExactlyOneTriplet; TransactionFailureReallyRollsBackAllRows | create/replay201,conflict409 | implemented |
| F12 | PaymentService.List | HistoryFiltersPagesVoidsAndOwnOnly; HistoryInvalidFilters; OwnershipAppliesToCreateFilterUpdateVoidAudit | PaymentReplayCorrectionVoidAuditAndHistoricalCurrency; ForeignResourcesLookAbsentForAllOperations | history/archived-history200 | implemented |
| F13 | PaymentService.Update/Void/Audit, same transaction audit | CorrectionPreservesCurrencyAndSnapshotsAuditPrevious; InvalidCorrectionRollsBack; OwnershipAppliesToCreateFilterUpdateVoidAudit | CorrectionAuditFailureRollsBackPayment; PaymentReplayCorrectionVoidAuditAndHistoricalCurrency; ForeignResourcesLookAbsentForAllOperations | correct/void/audit200 | implemented |
| F14 | AnalyticsService.Expenses + MoneyTotals | Expenses_ExactDatesCurrenciesStatusesSnapshotsAndOwners; Expenses_EmptyReturnsZeroAndNoGroups; Expenses_InvalidRanges; ValidMinimumCalendarDateIsNotMissingInput; ArchivedIsReadOnlyButHistoryStays | AnalyticsAndForecastUseOwnCurrencySnapshotsAndArchiveRules; InvalidQueriesHaveUnifiedValidation; EarliestHistoricalDateIsValidAndSurvivesSqliteAndJson | analytics/empty-analytics/void-excluded200 with totals assertions | implemented |
| F15 | ForecastService.Forecast, original day/month anchor | Forecast_FebruaryAnchors; Forecast_ClampDoesNotDriftAndMayContainTwoOccurrences; Forecast_AnnualLeapAnchorReturnsOnLeapYear; Forecast_OnlyOwnActiveUnarchivedAndFutureSchedules; Forecast_YearBoundaryAndExclusiveEnd | AnalyticsAndForecastUseOwnCurrencySnapshotsAndArchiveRules | forecast200 contains active subscription | implemented |

Для всех15 F есть позитивная и негативная/граничная проверка; Q01 закрыт. «—» у smoke не означает вымышленный curl по F03–F05: их реализация проверена unit/API, что допускается Q01. В smoke только31 реально выполненный запрос.

## N01–N10

| Requirement | Status | Реализация / доказательство / граница |
|---|---|---|
| N01 | partially implemented; requires external testing | Local HTTP loopback Development проверен реальным Kestrel/curl. Production HSTS/HTTPS redirect подготовлены, внешний TLS1.2+/сертификат/прокси не испытывались |
| N02 | implemented locally | Konscious Argon2id1.3.1,16-byte random salt,64MiB,t3,p4,32-byte hash; Argon2idUsesDistinctRandomSixteenByteSaltAndVerifies. PasswordHasher deviation нет. Независимый crypto audit/benchmark не проводился |
| N03 | implemented locally | JWT signature/algorithm/issuer/audience/expiry/UUID sub, ClockSkew0; JwtChecksRejectInvalidTokens, AdvertisedAccessExpiryEqualsSignedJwtExpiry; unit TTL/rotation/absolute30days/reset15min. Secrets only env/in-memory |
| N04 | partially implemented (accepted educational scope) | Account5 failures/15min persisted SQLite; IP30 attempts/15min in-memory singleton, Retry-After, exact rolling/fake clock/parallel unit tests + AccountLimitFiveFailuresThen429AndRollingExpiry и IpLimitCountsThirtyAttemptsIncludingBadBodies. Не distributed, IP reset при restart |
| N05 | requires external performance testing | Все числа сохранены: CRUDp95≤300ms,analytics≤1s,5xx<1%,100req/s,100clients,1M payments. Ни load run, ни1M dataset не создавались; request durations не выдаются за N05 |
| N06 | implemented | `/api/v1`, camelCase UTF-8 JSON, decimal, DateOnly, UTC Z converter; точные0.1+0.2 и разделение currencies проверены unit/API, raw curl JSON сохранён |
| N07 | implemented locally | Real body cap1MiB incl chunked→413; pageSize>100/page0/range>366→400; OversizeBothLengthAndChunkedAndCorrelationValidation и InvalidQueriesHaveUnifiedValidation |
| N08 | implemented locally | Transport.Boundary correlation headers/error field, route template/status/duration; SensitiveValuesNeverAppearInRequestLogs проверяет реальные secret values; реальные server logs не содержат body/tokens |
| N09 | partially implemented: containerization prepared | Dockerfile есть; docker info/build реально failed, daemon socket отсутствует. Docker run и Linux x64/arm64 не проверялись. Mac arm64 запуск не заменяет Linux |
| N10 | implemented locally |19 paths/22 operations, request/response/error schemas, Bearer security и Idempotency-Key header; OpenApiAndSwaggerExposeSchemasSecurityAndIdempotencyHeader, реальные curl OpenAPI/UI200 |

## Q01–Q05

| Requirement | Status | Доказательство |
|---|---|---|
| Q01 | implemented | Матрица15F выше; реальные positive/negative cases, уровни разделены |
| Q02 | implemented for required module branches | AnalyticsService и ForecastService каждый≥90% branch по фактическому XML, отдельно от общего процента. Leap/end-of-month/empty/void/currencies включены. Общая statement-цель PDF отдельно не подтверждена метрикой |
| Q03 | implemented | Application зависит только от Domain/портов; unit Moq dependencies, реальный SUT; нет HTTP/реальной БД/email в unit. SQLite только API/infrastructure suites |
| Q04 | implemented within application HTTP pipeline | ErrorResponse и central middleware + JWT events/status pages; validation fields обязательны, nonvalidation fields отсутствуют;401/404/409/413/429/500/503 проверены |
| Q05 | implemented locally | Final clean restore/build/test successful,0 warnings/failed; ForeignResourcesLookAbsentForAllOperations проверяет все owner-bound ID operations/filter/audit; контрактные validation и races покрыты |

## R01–R05

| Requirement | Status | Доказательство / что не заявлено |
|---|---|---|
| R01 | documented only; requires operational testing | ≥99.5% availability сохранено; длительный мониторинг/SLO measurement не проводился |
| R02 | documented only; requires operational testing | Daily backup + transaction-log interval≤15min сохранены; описана SQLite snapshot-альтернатива. Scheduler/backup/PITR не реализованы, WAL не объявлен backup |
| R03 | documented only; requires operational testing | RPO≤15min/RTO≤60min сохранены; реальный backup restore drill не выполнялся |
| R04 | implemented and integration-tested | BEGIN IMMEDIATE/commit/rollback; real fault after Payment/Idempotency/Audit leaves0 partial rows; retry same key succeeds; CorrectionAuditFailureRollsBackPayment; unique/race tests |
| R05 | implemented locally | Real cannot-open SQLite path→503 generic, нет2xx/partial write, process recovers; также startup with bad DB не убивает server. Unique→409, injected programming failure→500, не503 |

Итоговые labels не означают production readiness. Не проверенные здесь TLS/load/monitoring/backup/Linux/real-email и statement metrics не скрыты и не удалены из требований.


## Актуализация N10/Q05 — 26.11.2026

Swagger-конфигурация исправлена по P003: важен именованный requirement `{"Bearer":[]}`, а не просто непустой security array. Новые OpenApiSecurityTests проверяют15protected и7public операций в сериализованном JSON. Последний полный прогон155passed/0failed/skipped, build0warnings; server JWT и business classes не менялись. Актуальные доказательства — [swagger-bearer-fix.md](swagger-bearer-fix.md) и artifacts/swagger-bearer-2026-11-26; таблица coverage и предыдущий final/summary остаются историческими от21.11.

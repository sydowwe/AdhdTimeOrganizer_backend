# Routines — Risks & Rollout (03)

Synthesis. IDs refer to `02-findings.md` (detail) and `01-testing.md` (gaps). Nothing is restated here.

## 11. Risk-ranked issue list

| # | Sev | Issue | ID | File |
|---|---|---|---|---|
| 1 | 🔴 | Monthly and yearly anchored periods never reset — every user has two by default | `CQ-1` | `domain/service/RoutineResetService.cs:36-76` |
| 2 | 🔴 | Routine item can be attached to another user's time period; the unauthenticated sweep then counts it against their streak | `SEC-1` | `application/dto/request/CreateRoutineTodoListRequest.cs:9`, both to-do validators |
| 3 | 🟠 | Activity merge throws when two routines collide in one period, failing the whole six-slice merge | `CQ-2` / `DOC-3` | `application/seam/RoutineTodoListActivityReferenceSource.cs:35-45` |
| 4 | 🟠 | `PUT /routine-todo-list` flips `IsDone` without the cross-slice event — Planning silently desyncs | `SEC-2` | `application/dto/request/UpdateRoutineTodoListRequest.cs:16` |
| 5 | 🟠 | Grace warning marked sent when delivery failed; streak then breaks unwarned | `CQ-6` | `infrastructure/jobs/RoutinePeriodNudgeJobHandler.cs:71-76` |
| 6 | 🟠 | Concurrent reset sites race on unique `(TimePeriodId, PeriodStart)`; one collision aborts the night's sweep | `SEC-3` | `GetAllGroupedRoutineTodoListEndpoint.cs:63-67`, `RoutineTodoListResetJobHandler.cs:67` |
| 7 | 🟠 | Read endpoints serve `NextResetAt = 0001-01-01` and zeroed freeze fields, breaking invariant 5 | `CQ-5` / `DOC-4` | `application/dto/response/RoutineTimePeriodResponse.cs:57-78` |
| 8 | 🟠 | Item streak inflates on untick/retick within one cycle | `CQ-7` | `domain/service/RoutineResetService.cs:258-267` |
| 9 | 🟠 | Updating an item deletes and recreates all its steps, churning ids | `CQ-4` | `application/endpoint/routineTodoList/command/UpdateRoutineTodoListEndpoint.cs:20-27` |
| 10 | 🟠 | Dormant periods catch up one cycle per run, dripping retroactive miss notifications | `CQ-8` | `domain/service/RoutineResetService.cs:236` |
| 11 | 🟠 | Validator misses `HistoryDepth` and the `LengthInDays` ceiling; DB catches them instead | `CQ-3` | `application/validator/RoutineTimePeriodValidator.cs:14-15` |
| 12 | 🟠 | Grouped read fetches unbounded completion history and re-interprets the projection per item | `PERF-1`, `PERF-2` | `GetAllGroupedRoutineTodoListEndpoint.cs:76-102` |
| 13 | 🟠 | Both jobs materialise every user's periods, items and steps in one batch | `PERF-3` | `infrastructure/jobs/*.cs` |
| 14 | 🟡 | Raw Postgres message returned on check violations | `SEC-4` | `RoutineTimePeriodConfiguration.cs:25-72` |
| 15 | 🟡 | `RoutinePeriodCompletionConfiguration` hand-rolls `HasKey`, skipping `BaseEntityConfigure()` | `CQ-9` | `infrastructure/persistence/configuration/RoutinePeriodCompletionConfiguration.cs:10` |
| 16 | 🟡 | Two dead filter DTOs exposing a settable `UserId` | `CQ-10` | `application/dto/filter/*.cs` |
| 17 | 🟡 | Validators open a DI scope per validation; bucket rule duplicated | `CQ-11` | both to-do validators |
| 18 | 🟡 | Three different user-scoping idioms, only two of them commented | `CQ-12` | across endpoints |
| 19 | 🟡 | Doc folder paths stale throughout the navigation index | `DOC-1` | `docs/domain-map.md` |
| 20 | 🟡 | Endpoint count 21 vs 23 between the two docs | `DOC-2` | `docs/summary.md:20` |
| 21 | 🟡 | Nudge sweep's `!IsHidden` scan unindexed | `PERF-4` | `RoutinePeriodNudgeJobHandler.cs:40-43` |

**Suggested fix order.** `CQ-1` + `TEST-1` first and alone — it is the largest behavioural change here
and wants its own release (see `MIG-2`). Then `SEC-1` + `TEST-3`, which is a small, contained patch.
Then `CQ-2`, `SEC-2`, `CQ-6`, `CQ-7` as a correctness batch. `CQ-5`, `CQ-3`, `CQ-4` are independent and
cheap. The `PERF-*` items are not urgent at current scale and should follow, not precede, the
correctness work — `PERF-1`'s fix touches the same method as `SEC-3`'s.

## 10. Migration / rollout risks

| Risk | Likelihood | Mitigation | ID |
|---|---|---|---|
| **`CQ-1`'s fix triggers a mass catch-up on first deploy.** Every monthly and yearly period in the database has a null or ancient `LastResetAt`; the corrected `ComputeNextReset` makes them all immediately due. The first 02:00 sweep after deploy resets them, writes completion rows and — via `CQ-8` — starts a per-run drip of backdated "period ended" notifications to every user who has ever had a monthly routine. | **High** — it is the direct consequence of the fix | Fix `CQ-8` in the same change so catch-up collapses into one cycle, and suppress the end-of-period notification for cycles older than one period length. Consider a one-off data migration that stamps `LastResetAt = <most recent boundary before now>` on affected periods so the first sweep has nothing retroactive to do. Deploy behind a quiet window and watch the notification volume. | `MIG-1` |
| **`CQ-1`'s fix changes streak semantics for live rows.** Periods that have sat un-evaluated for months will be scored on whatever their items' current `IsDone` state happens to be — likely all false — breaking streaks that display as intact today. | **High** | Those streaks are already fiction (nothing has advanced them either), but users will read the change as data loss. Either seed the catch-up as `StreakOutcome.NotEvaluated` for pre-deploy cycles, or accept and announce it. This is a product decision, not a code one. | `MIG-2` |
| **`SEC-1`'s fix may reject existing rows.** If any `routine_todo_list` row already points at a period owned by a different user, adding the check makes future updates of that row fail validation while leaving the row in place. | **Low** (requires the hole to have been exercised) | Audit first: `SELECT count(*) FROM routines.routine_todo_list r JOIN routines.routine_time_period p ON p.id = r.time_period_id WHERE p.user_id <> r.user_id;` — schema-qualified, because hand-written SQL is not qualified for you (CLAUDE.md). Repoint or delete any hits before shipping the check. | `MIG-3` |
| **`CQ-2`'s collision rule deletes rows.** Whichever rule is chosen, one of the two colliding routines loses its `Streak` / `BestStreak`, which the seam's own remark calls the thing "no amount of re-creating the activity brings back". | **Medium** | Make the rule explicit and one-way (keep the survivor's), log the discarded streak values at info with entity ids only, and cover it with `TEST-2` before shipping. | `MIG-4` |
| **`CQ-9`'s `BaseEntityConfigure()` swap may not be schema-neutral.** If the helper adds a row_version or timestamp columns, this becomes a real migration on a table that grows one row per period per cycle. | **Medium** | Generate the migration and read the diff before assuming it is empty — `summary.md:137-141` sets exactly this expectation for this module. If it is non-empty, it is an `ALTER TABLE ADD COLUMN` with a default, which locks briefly; size the table first. | `MIG-5` |
| **`CQ-3`'s new validation rules narrow an existing contract.** Any client currently sending `HistoryDepth` outside 1–100 gets a differently-shaped 400 (field-keyed rather than a Postgres message). | **Low** | No data migration needed — the DB already rejects these, so no such rows exist. Purely an error-shape change; note it in the API changelog. | `MIG-6` |
| **Drift risk: the docs are the map, and they are already stale.** `DOC-1` shows five path groups in `domain-map.md` that no longer match the tree after the slice extraction flattened directories. The navigation index is the thing agents and humans open first. | **Certain** (already true) | Fix in the same pass as any of the above — the `Doc impact:` line on each finding names the section. | `MIG-7` |
| **Drift risk: `framework/` is a submodule.** None of these findings require a framework change, but `SEC-3`'s fix is tempted toward `RetryDbConcurrencyHelper`. If it goes there, it is a two-repo operation — commit and push the submodule first, then the gitlink here, or the change is invisible to the parent's diff. | **Low** | Keep the unique-violation catch local to this slice's endpoints and job. | `MIG-8` |

## Audit-log gaps

Auditing is **not wired up in this host at all** — CLAUDE.md states it plainly and nothing in this
module contradicts it. So the items below are latent: they describe what will be missing the day the
interceptor is switched on, not data being lost today.

| Gap | Detail | ID |
|---|---|---|
| No `ExecuteUpdateAsync` / `ExecuteDeleteAsync` anywhere in the module | Verified across all 63 source files. Every write goes through the ChangeTracker, so this slice is well positioned for the interceptor. Worth keeping that way — `PERF-3`'s fix in particular could be tempted toward a bulk update. | `AUDIT-1` |
| `RoutineTodoListSeeder.TruncateTable` bypasses the ChangeTracker | `infrastructure/persistence/seeder/dev/RoutineTodoListSeeder.cs:21-24` calls `TruncateTableCascadeAsync<RoutineTodoList>()` — raw SQL, no interceptor, and it truncates **every user's** routine items despite being a per-user dev seeder. This is the documented contract for dev seeders ("only dev seeders truncate"), so it is correct — but it must never be reachable from a production seeding path. | `AUDIT-2` |
| Nothing marked `[AuditIgnore]` / `[NoAudit]`, and nothing here needs to be | `RoutineTimePeriod.Text` is user-authored free text and would be captured verbatim once auditing is on. That is ordinary entity data rather than PII by construction, but it is the one field in this module a privacy review would ask about. Logging already handles it correctly — `RoutinePeriodNotificationService.cs:66-67` explicitly logs the period **id** and keeps `Text` out, with a comment saying why. | `AUDIT-3` |
| The two jobs write without an authenticated user | Both handlers mutate tracked rows and insert `RoutinePeriodCompletion` (a plain `BaseEntity`, no `UserId`) in a bare background scope. Any future audit rows they generate will have no actor. `summary.md:106-112` already flags the adjacent hazard for `IEntityWithUser` inserts; the audit-actor question is the same seam. | `AUDIT-4` |

# AdhdTimeOrganizer.Planning — Review 03: Risks & Rollout

> Synthesis of [`02-findings.md`](02-findings.md) and [`01-testing.md`](01-testing.md). Ids link back;
> nothing is restated here. Point-in-time, 2026-08-31.

## 11. Risk-ranked issue list

| # | Sev | Issue (one line) | ID | File |
|---|---|---|---|---|
| 1 | 🟠 | ✅ **Fixed 2026-09-08** — `MergeOverwrite` carving an overnight task deletes it and puts nothing back, silently, with a 201. Fixed together with the root cause `CQ-14` and the doc line `DOC-1`; **`MIG-3` below is still open** | `CQ-7` | `application/helper/PlannerTemplateApplier.cs:150` |
| 2 | ✅ | ~~The Routines fan-out handler never re-syncs reminders — a finished task keeps nudging~~ — fixed 2026-09-08 | `CQ-2` | `application/eventHandler/RoutineTodoListIsDoneChangedEventHandler.cs:40` |
| 3 | ✅ | ~~FK ids (`ActivityId`, `ImportanceId`, `TodolistId`, `DefaultApplyTemplateId`) accepted without an ownership check; another user's activity name and role colour come back on the attacker's own day~~ — fixed 2026-09-08 via `PlannerReferencedIdOwnershipExtensions` on the five write paths, and **the residual closed the same day**: the recurrence and template-line writes now go through the same helper, along with `TemplatePlannerTaskRequest.TemplateId`, an id the finding never named and the only one of the family that is a cross-user *write* | `SEC-1` | `application/dto/request/taskPlanner/PlannerTaskRequest.cs:61` |
| 4 | ✅ | ~~Concurrent `GET /planner/settings` can create two settings rows; no unique index backs the 1:1~~ — fixed 2026-09-08, and the premise was half wrong: the index **is** unique (by EF convention, undocumented), so two rows were never creatable and `MIG-1` is moot. What was real is that the loser of the race took a unique violation and answered 500 on a page load; both endpoints now recover | `SEC-3` | `application/endpoint/plannerSettings/GetPlannerSettingsEndpoint.cs:26` |
| 5 | ✅ | ~~The to-do fan-out query has no `UserId` predicate; safe only while `ScopeUserId` is non-null~~ — fixed 2026-09-08; `TodoListItemIsDoneChangedEvent` gained the `UserId` it was assumed to already carry | `SEC-2` | `application/eventHandler/TodoListItemIsDoneChangedEventHandler.cs:36` |
| 6 | ✅ | ~~The Routines fan-out handler sets `Status` directly, leaving stale actual times the trend then reads~~ — fixed 2026-09-08 | `CQ-1` | `application/eventHandler/RoutineTodoListIsDoneChangedEventHandler.cs:41` |
| 7 | ✅ | ~~Same handler has no try/catch — a Planning concurrency fault 500s a Routines endpoint that already committed~~ — fixed 2026-09-08 | `CQ-3` | `application/eventHandler/RoutineTodoListIsDoneChangedEventHandler.cs:25` |
| 8 | ✅ | ~~Same handler overwrites `Cancelled` tasks that its sibling deliberately leaves alone~~ — fixed 2026-09-08 | `CQ-4` | `application/eventHandler/RoutineTodoListIsDoneChangedEventHandler.cs:40` |
| 9 | ✅ | ~~"Today" is UTC in three write paths, the user's own zone in both read paths~~ — fixed 2026-09-08; all five now go through the one `TaskPlannerHelper.TodayIn` | `CQ-5` | `application/endpoint/plannerTask/command/PatchPlannerTaskStatusEndpoint.cs:73` |
| 10 | ✅ | ~~Carved segments inherit `Status` and `TodolistItemId` but not the actual times — one done-toggle fans across every segment~~ — fixed 2026-09-08: a carved segment is a new block (`NotStarted`, no to-do link), the same ruling `CopyToDaysPlannerTaskEndpoint.CopyOnto` already made | `CQ-6` | `application/helper/PlannerTemplateApplier.cs:181` |
| 11 | ✅ | ~~The two `RepeatingPlannerTask` CRUD endpoints never wire their validator; 80 lines of recurrence rules are dead or untested and the two are indistinguishable~~ — resolved 2026-09-08: `TEST-11` measured it, the rules **were** running (FastEndpoints binds by convention), so this was 🟡 not 🟠. Both endpoints now wire it explicitly anyway | `CQ-8` | `application/endpoint/repeatingPlannerTask/command/CreateRepeatingPlannerTaskEndpoint.cs:3` |
| 12 | ✅ | ~~`ApplyTemplatePlannerTaskEndpoint` serves 400 for concurrency conflicts and for 500s alike, discarding the status code it computed~~ — fixed 2026-09-08; the summary declares 409 too, so the OpenAPI contract stopped documenting the bug | `CQ-9` | `application/endpoint/plannerTask/command/ApplyTemplatePlannerTaskEndpoint.cs:92` |
| 13 | 🟠 | ✅ **Fixed 2026-09-08** — `domain-map.md` asserted every planner-task validator rejects `EndTime <= StartTime`; three of six did not. All six do now, and the row states the remaining truth: no CHECK constraint, so older rows can still violate it | `DOC-1` | `docs/domain-map.md:91` |
| 14 | ✅ | ~~`DayPlanResponse.HasPlan`'s doc still says calendar rows are never created lazily~~ — fixed 2026-09-08 | `DOC-4` | `application/dto/response/taskPlanner/DayPlanResponse.cs:52` |
| 15 | 🟠 | ✅ **Fixed 2026-09-08** — and it was worse than 🟡: `GetSuggestionsTaskPlannerDayTemplateEndpoint` answered **500 to every call**, stale row or not, because its keyless view entity derived from `BaseEntityWithUser` and EF selected `id` / `created_timestamp` from an aggregate that has neither. The endpoint had no test at all, exactly as `docs/testing.md` warned. Stale-principal rows are now filtered in all three queries | `CQ-13` | `application/endpoint/taskPlannerDayTemplate/query/GetSuggestionsTaskPlannerDayTemplateEndpoint.cs:58` |
| 16 | ✅ | ~~Six custom endpoints omit `Roles()`; **not** an authz hole today — the host's configurator supplies the identical gate~~ — fixed 2026-09-08; all six now declare it, so no Planning route's gate is decided outside the slice | `SEC-4` | `application/endpoint/plannerSettings/GetPlannerSettingsEndpoint.cs:11` |
| 17 | 🟡 | Free-text `Tags` comma-joined into one column; a comma in a tag splits it, and no validator constrains the field | `CQ-11` | `infrastructure/persistence/configuration/activityPlanning/TaskPlannerDayTemplateConfiguration.cs:24` |
| 18 | ✅ | ~~Eighteen lines of midnight-wrap window logic duplicated verbatim across two filter endpoints, neither tested~~ — fixed 2026-09-08: one `TaskPlannerHelper.InTimeWindow`, and `PlannerTaskFilterWindowTests` (17) runs one table against both endpoints | `CQ-12` | `application/endpoint/plannerTask/query/GetFilteredSortedPlannerTask.cs:14` |
| 19 | ✅ | ~~`.Update()` on an already-tracked entity rewrites every column on every status patch~~ — fixed 2026-09-08 | `CQ-10` | `application/endpoint/plannerTask/command/PatchPlannerTaskStatusEndpoint.cs:64` |
| 20 | ✅ | ~~`testing.md` credits `CompletionFanOutTests` with a handler it never exercises~~ — fixed 2026-09-08 | `DOC-5` | `docs/testing.md:94` |
| 21 | 🟡 | Stale source comment names a `Holiday` day type the enum does not have | `DOC-2` | `domain/model/entity/Calendar.cs:12` |
| 22 | 🟡 | Two delete-behaviour test names claim a doc contradiction that was resolved | `DOC-3` | `AdhdTimeOrganizer.IntegrationTests/Endpoints/PlannerDeleteBehaviorTests.cs` |
| 23 | 🟠 | ✅ **Found and fixed 2026-09-08, not from the original pass** — `UpdateCalendarEndpoint` answered 500 where it meant 400: `ThrowError` from `AfterMapping` is swallowed by `BaseUpdateEndpoint`'s blanket catch. Three cases of `CalendarDayTypeRuleTests` had been red, which is how it surfaced | `CQ-15` | `application/endpoint/calendar/UpdateCalendarEndpoint.cs:18` |
| — | perf | ✅ `PERF-1` … `PERF-6` all fixed 2026-09-08 — see the table in file 02 § 9 | | |

**The shape of the risk.** Eleven of the twenty-two entries were 🟠 and **four of them were in one
45-line file** — `RoutineTodoListIsDoneChangedEventHandler`, the only cross-slice handler in the slice
with zero tests (`TEST-21`). Its sibling, which had four tests, had none of the four defects. That was
this review's headline recommendation and it is **done as of 2026-09-08**: the handler now matches its
sibling, `RoutineCompletionFanOutTests` gives it six tests, and rows 2, 6, 7, 8 and 20 above are closed.
Rows 1 and 13 (`CQ-7` and `DOC-1`, with their shared root cause `CQ-14`) closed on the same day, as did the
two security rows 3 and 5 (`SEC-1`, `SEC-2` — one hole in two halves: the ownership check that stopped an
attacker aiming a task at a victim's row, and the fan-out predicate that stopped the write landing).

**Every row above is now closed, and so are `PERF-1`…`PERF-6` and `MIG-3`/`MIG-4`/`MIG-5`** (one migration,
`20260908164241_PlannerSpanNormalisationTagsJsonbAndDeadIndexes`, carrying all three). `MIG-1` turned out to be
moot and `MIG-2` is the one rollout risk still standing — it is a dev/staging seeder blast radius, not a
production one, and `TEST-23` has to measure it before anything changes.

Nothing here was 🔴. The one data-loss path — `MergeOverwrite` carving an overnight task away — closed on
2026-09-08 along with the rows that could still carry the shape; there is no cross-user read reachable without
an attacker crafting a request by hand, and no unauthenticated surface.

**Two things this closing pass found that the review had not.** Both were only visible because the fixes came
with tests: `GetSuggestionsTaskPlannerDayTemplateEndpoint` answered 500 to *every* call, not just to a stale
row (`CQ-13`), and `UpdateCalendarEndpoint`'s day-type rejection was a 500 rather than a 400 (`CQ-15`, with a
red test already in the repo). Both are the shape this slice's `docs/summary.md` warns about — a failure that
compiles, returns a response, and logs nothing that names it.

## 10. Migration / rollout risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| **`MIG-1` — ✅ MOOT, closed 2026-09-08 with `SEC-3`.** The premise was that `user_planner_settings.user_id` carries no unique index and duplicates therefore already exist | — | **No migration was needed or written.** `20260819234301_Initial` created `ix_user_planner_settings_user_id` with `unique: true` — EF's foreign-key index convention marks the helper's non-unique `HasIndex` unique because the relationship is 1:1 — so duplicate rows were never creatable and there is nothing to collapse. `SEC-3`'s fix declares the uniqueness explicitly in `UserPlannerSettingsConfiguration` (same index, no schema change) and makes both endpoints survive losing the insert race, which is the failure that was actually reachable. If a survey is wanted for peace of mind: `SELECT user_id, count(*) FROM planning.user_planner_settings GROUP BY user_id HAVING count(*) > 1` — it cannot return a row |
| **`MIG-2`** — `TaskPlannerDayTemplateSeeder.TruncateTable()` issues `TRUNCATE … RESTART IDENTITY CASCADE` (`framework/…/seeder/SeederExtensions.cs:76`). Postgres `TRUNCATE CASCADE` **truncates every referencing table outright** and does not honour `ON DELETE SET NULL`. `user_planner_settings.default_apply_template_id` references `task_planner_day_template`, so re-running the dev seeders silently wipes **every user's planner settings**, not just the templates | High in any dev/staging reset; zero in production (dev seeders do not run there) | Confirm the blast radius with `TEST-23` before changing anything. Then either exclude `UserPlannerSettings` by nulling the FK first (`UPDATE … SET default_apply_template_id = NULL`) and truncating without `CASCADE`, or accept it and re-run `UserPlannerSettingsSeeder` after. Note `UserPlannerSettingsSeeder` is a **per-user default** seeder (order 420) and does not truncate, so today nothing restores what this removes |
| **`MIG-3` — ✅ CLOSED 2026-09-08**, in `20260908164241_PlannerSpanNormalisationTagsJsonbAndDeadIndexes` together with `MIG-4` and `MIG-5`. The clamp is the one prescribed below, per table, each `RAISE NOTICE`-ing its row count so a non-zero one leaves a trace; `Down` deliberately does not reverse it, since the original times are overwritten in place. Verified by applying the generated script to a scratch Postgres seeded with `23:00→01:00`, `23:59→23:00` and `10:00→10:00` plus a well-formed control: three clamped, control untouched. There is still **no CHECK constraint**, so `TasksOverlap`'s reading and `PlannerTemplateApplier.IsCarvable` stay load-bearing for a row arriving from `psql` or an old backup. *(Original entry below.)* **The code half landed 2026-09-08 without it, deliberately.** The validator rule is in (`CQ-14`: `PlannerTaskValidator`, `RepeatingPlannerTaskValidator`, `TemplatePlannerTaskValidator`), so *new* bad rows are impossible — but there is **no CHECK constraint** and nothing has normalised the rows that predate it. Such a row now round-trips through **no** write path at all: create, update and patch-span all refuse it, so it can only be deleted | Medium — unchanged. Rows are only reachable via a hand-crafted request; the UI never offered an overnight task | **Deferred by the owner to a single pass after the rest of the findings are fixed.** Survey first: `SELECT count(*) FROM planning.planner_task WHERE end_time <= start_time`, and the same on `repeating_planner_task` and `template_planner_task`. Then clamp in one data migration — `SET end_time = TIME '23:59', start_time = LEAST(start_time, TIME '23:58') WHERE end_time <= start_time` handles the 23:59-start edge too — logging the ids; `Down` cannot restore the originals, so say so in the migration. Nothing depends on this landing first: `PlannerTemplateApplier.IsCarvable` (`CQ-7`) leaves an un-normalised row whole instead of silently deleting it, and `docs/domain-map.md`'s invariant row states the gap rather than claiming enforcement (`DOC-1`) |
| **`MIG-4` — ✅ CLOSED 2026-09-08**, same migration. Moving `Tags` from comma-joined `text` to `jsonb` (`CQ-11`); tags a comma had *already* split stay lost, as accepted below | Low — cosmetic column, small table | Not the plain `ALTER … TYPE jsonb` EF scaffolds, which Postgres refuses (`42804`): `USING to_jsonb(CASE WHEN tags IS NULL OR tags = '' THEN ARRAY[]::text[] ELSE string_to_array(tags, ',') END)`, preceded by `ALTER COLUMN tags DROP DEFAULT` (the default is cast first and would fail before the `USING`; this schema has none). `Down` goes through a temporary column, because unpacking jsonb needs a set-returning function and a transform expression may not contain a subquery. Both directions run against a scratch Postgres |
| **`MIG-5` — ✅ CLOSED 2026-09-08**, same migration: dropping the three dead indexes (`PERF-5`) | Low | Three plain `DROP INDEX` statements as EF scaffolded them, inside the migration's transaction — **not** `CONCURRENTLY`, which cannot run in one. The tables are small and the indexes are provably unused, so the lock is momentary; if that is ever not true for a given deployment, `pg_stat_user_indexes.idx_scan` is still the check worth running first |
| **Drift risk: the slice registers with the host in four places, none of which break the build** | Standing, and already documented | `PlanningRouteSmokeTests.PlanningRoutes_AreRegistered` covers the FastEndpoints `o.Assemblies` half and `PlannerTaskToTodoListItem_ForeignKey_SurvivesTheSliceSplit` covers the cross-slice FK. `ModuleWiringTests` / `ModuleSchemaTests` / `SeamWiringTests` cover the rest. **No new drift found this pass** — the four registration points are all live |
| ~~**Drift risk: `SEC-4`'s six endpoints depend on `Program.cs:575-576`**~~ | ✅ Closed 2026-09-08 | The cheaper branch was taken: two lines × six files. The dependency is gone rather than monitored |
| **No sandbox or duplicate copy of this module exists** | — | Verified: `AdhdTimeOrganizer.Planning` appears once in the solution; `AdhdTimeOrganizer/reference/mojaCore/` contains no planner code |

## Audit-log gaps

**`AUDIT-1` — nothing in this slice is audited, and that is a host-level fact, not a slice defect.**
CLAUDE.md states it plainly: *"Auditing exists in Framework but is NOT wired up here. Nothing is written
today."* So there is no interceptor for anything to bypass, and no `[AuditIgnore]` / `[NoAudit]`
attribute is missing in the sense of causing a leak today.

What this review *can* record is what will be true the day it is switched on:

- **No `ExecuteUpdateAsync` / `ExecuteDeleteAsync` anywhere in the slice.** Every write goes through
  the ChangeTracker — `PlannerTemplateApplier` uses `RemoveRange`/`AddRange`, the seam's `RepointAsync`
  loads rows and assigns, `CalendarProvisioner` adds one entity. That is the good news, and it is worth
  not regressing: a future bulk day-type update tempted by `ExecuteUpdateAsync`
  (`BatchUpdateDayTypeCalendarEndpoint` is the obvious candidate, currently one save per day) would be
  the slice's first interceptor bypass.
- **`PlannerTemplateApplier` will produce a delete + insert pair per carved segment, not an update.**
  `MergeOverwrite` removes an existing task and re-adds segments as new rows with new ids. An audit
  reader reconstructing a task's history will see it end and two unrelated tasks begin. That is inherent
  to the carving design, not a bug, but it should be written down before anyone tries to build a
  per-task audit timeline. `CQ-6` is the related correctness question.
- **`Reminder.Title` and `Reminder.Note` are free-text, user-authored, and would land in an audit log
  verbatim.** They routinely contain names and phone numbers ("call the dentist", "Mum's birthday").
  `PiiRedactor` only matches structured PII and is not wired into the Serilog pipeline here anyway, so
  free-text will not be scrubbed. When auditing is enabled, `Reminder` needs `[AuditIgnore]` on both
  columns — or `[NoAudit]` on the entity. Same reasoning applies to `PlannerTask.Notes`,
  `PlannerTask.SkipReason`, `Calendar.Notes` and `Calendar.Label`.
- **No PII reaches the logs today.** Every log statement in the slice was checked:
  `CalendarProvisioner` logs `{UserId}` and `{Date}`; `ReminderRegistrationService` logs `{ReminderId}`
  and `{UserId}` and never the title; the three batch endpoints log `{CalendarId}` / `{TemplateId}`;
  the seeders log counts and `{UserId}`. This slice complies with the no-PII-at-the-call-site rule
  cleanly — worth stating, since it is the kind of thing that decays without a check.

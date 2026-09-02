# History — 03 · Risks & rollout

Point-in-time snapshot, 2026-09-01. Synthesis over `02-findings.md` and `01-testing.md`; IDs link back
to the detail there and are never restated here.

## 11. Risk-ranked issue list

| # | Sev | Issue | ID | File |
|---|---|---|---|---|
| 0 | 🔴 | **The test harness enables `Npgsql.EnableLegacyTimestampBehavior` and the host does not**, so no round-trip test in the repo can fail on a parameter-kind bug. Running the suite with it off fails 6 tests, 5 of them production code: rows 1 and 2 below, plus a 500 on the desktop details panel and three on the notification quiet-hours path | `INFRA-1` | `framework/Sydowwe.Framework.Testing/PostgresContainerFixture.cs:26` |
| 1 | 🔴 | `baseline: "alltime"` passes an `Unspecified`-kind `DateTime` to a `timestamptz` column → unhandled 500, in both summary-cards endpoints | `CQ-4` | `…/dashboard/{summary,detail}/History*SummaryCardsEndpoint.cs:142` |
| 2 | 🟠 | The tracking attribution sink bounds its day with an `Unspecified` kind, a closed `MaxValue` range and UTC midnights — on a live heartbeat path with zero tests | `CQ-5` | `application/seam/ActivityHistoryTimeAttributionSink.cs:53-67` |
| 3 | 🟠 | A timer alarm accepts any `activityId` and the job handler resolves the name unscoped → another user's activity name in the push body | `SEC-1` | `application/validator/ScheduleTimerAlarmsValidator.cs:35`, `application/job/TimerBoundaryAlarmJobHandler.cs:52` |
| 4 | 🟠 | No ownership check on `ActivityId` / `TodoListItemId` / `RoutineTodoListId` on write | `SEC-4` | `application/validator/ActivityHistoryValidator.cs:11-28` |
| 5 | 🟠 | The grid ignores `page` / `itemsPerPage` / `sortBy` and returns the whole ledger with three joins | `CQ-1`, `PERF-1` | `…/query/GetFilteredTableActivityHistoryEndpoint.cs:37-57` |
| 6 | 🟠 | The calendar dashboard and the grid fold days in **UTC**, contradicting the Timezone invariant | `DOC-2` | `application/endpoint/CalendarActivityEndpoint.cs:23-35`, `…gird:49` |
| 7 | 🟠 | The grid returns raw `ex.Message` to the client and swallows cancellation into a 500 | `SEC-2` | `…gird:61-65` |
| 8 | 🟠 | `detail/stacked-bars` emits 24 hourly windows regardless of the picked range, with UTC-instant arithmetic across DST | `CQ-3` | `…/detail/HistoryDetailStackedBarsEndpoint.cs:60-72` |
| 9 | 🟠 | ~235 lines duplicated between the detail and summary dashboards — every fix above lands in two files or ships half-done | `CQ-2` | `…/dashboard/{detail,summary}/*` |
| 10 | 🟠 | `filter` bounds on `EndTimestamp` while every other read bounds on `StartTimestamp` | `CQ-11` | `…/query/FilterActivityHistoryEndpoint.cs:32` |
| 11 | 🟠 | The domain map's endpoint table names `/all-options`; the shipped route is `/form-select-options` | `DOC-1` | `docs/domain-map.md:87` |
| 12 | 🟠 | Five dashboards materialize whole entity graphs and aggregate in memory; no `AsNoTracking()` anywhere | `PERF-2`, `PERF-3`, `CQ-8` | `…/dashboard/**` |
| 13 | 🟡 | The calendar dashboard has no validator: unbounded range, unbounded `TopN` | `SEC-3`, `PERF-4` | `application/endpoint/CalendarActivityEndpoint.cs` |
| 14 | 🟡 | `Baseline` is a magic string with a silent `default` arm meaning `last7days` | `CQ-7` | `…/request/dashboard/summary/HistorySummarySummaryCardsRequest.cs:5` |
| 15 | 🟡 | `ReplaceSessionAsync` is two transactions with an interleaving window | `CQ-12` | `application/service/TimerBoundaryAlarmService.cs:50-77` |
| 16 | 🟡 | Dead computation (`BaselineTo`, unused `baselineFrom`, unread `Records`) ×2 files | `CQ-6` | both summary-cards endpoints |
| 17 | 🟡 | `ApplyUserScoping` overridden to resolve a timezone and scope nothing | `CQ-10` | `…/query/FilterActivityHistoryEndpoint.cs:21-26` |
| 18 | 🟡 | Aggregate endpoint's summary describes a name-keyed pie chart that no longer exists | `DOC-3` | `…/query/AggregateByActivityActivityHistoryEndpoint.cs:10-14` |
| 19 | 🟡 | Sync `.ToList()` in the async dev seeder | `CQ-9` | `…/seeder/dev/ActivityHistorySeeder.cs:25` |
| 20 | 🟡 | `summary.md` doesn't state the alarm endpoints' role gate | `DOC-4` | `docs/summary.md:79-80` |

Test gaps are ranked separately in `01-testing.md` § 12; `TEST-4` and `TEST-2` are the two that would
have caught rows 1–2 of this table before a user did — **with the caveat `INFRA-1` adds**: as the harness
stands, neither of them written as an ordinary round trip would have failed on the kind bug in either
row. Row 0 is ranked above row 1 because it is the reason rows 1 and 2 reached a review rather than a
red build, and because two of the defects it is hiding are not in this table at all (they are outside
History's slice).

## 10. Migration / rollout risks

| Risk | Likelihood | Mitigation | ID |
|---|---|---|---|
| **Fixing `CQ-4`/`CQ-5` needs no schema change at all** — both are parameter-kind bugs. Deploy as ordinary code; no backfill, no lock, no migration | — | Ship them first and independently; they are the only two items on this list that are pure wins with no contract change | `MIG-1` |
| **`CQ-1` (grid paging) is a wire-contract change.** Whatever shape wins — paging the date groups, or dropping the paging fields — the SPA's history grid must ship in the same release. The route also carries the shipped `gird` typo, so a "while we're in here" rename would break the SPA a second way | High if attempted casually | Change the response shape and the SPA together in one PR; do **not** rename `gird`; add `TEST-1` first so the new paging is asserted on rows | `MIG-2` |
| **`DOC-2` (UTC → user-zone day fold) silently moves data between days.** Any client-side cache, screenshot-based expectation or user-facing "you logged 3h on Tuesday" changes value for every non-UTC user on the deploy. Nothing is stored per-day, so there is no backfill — but there is no way to make it gradual either | High, benign but visible | Land it with the `TEST-3` cases and mention it in release notes; the calendar's join against `ICalendarDayLookup` days must be converted in the same commit, or the two sides disagree in a new way | `MIG-3` |
| **`SEC-4` (ownership validation) may reject data that already exists.** If any production row points at a foreign `ActivityId` / `TodoListItemId`, a new *write* check won't fix it and a new *DB constraint* would fail to apply. There is no unique/composite constraint to add here — ownership isn't expressible as an FK — so this stays app-enforced | Low | Ship the write-side check alone. Before considering anything stronger, run the read-only audit query in `ActivityHistoryWriteRules.md` to see whether such rows exist | `MIG-4` |
| **`CQ-2` (de-duplicating the four dashboards) is the highest-churn change in this list and touches the four endpoints most covered by value-asserting tests.** That is a feature, not a risk — but only if the extraction is done *before* the behaviour fixes (`CQ-3`, `CQ-4`, `CQ-7`), not after | Medium | Order: extract shared builders → run the existing suites unchanged → then fix behaviour once, in one place | `MIG-5` |
| **`SEC-3` (calendar range cap) can 400 a request the SPA sends today.** Unknown what range the calendar view actually asks for | Medium | Read the SPA's calendar request before picking the cap; reuse `DateRangeDto.MaxCustomRangeDays` rather than inventing a second limit | `MIG-6` |
| **Removing the `INFRA-1` switch is a submodule change with a solution-wide blast radius, but a measured one.** Deleting it turns 0 failures into 6; one is a test-side seeding fix and the other five are production defects that must be fixed first anyway. The whole test project holds only 5 kind-less `DateTime` literals across 2 files, so there is no long tail | Low, once the five are fixed | Fix the three production defects, then delete the switch and the one seeding bug in the same submodule-first commit pair. Do **not** delete it before the fixes — it converts three latent 500s into a red build with no product change | `MIG-9` |
| **`INFRA-1`'s notification defect is the one with real user exposure.** `NotificationService.NotifyAsync` throws for any recipient outside UTC whose delivery is deferred by quiet hours. It cannot be reproduced by the UTC fixture user, and it lives in `Sydowwe.Notifications`, so any host that takes that module has it | High for non-UTC users with quiet hours set | Fix in the submodule (`.ToUniversalTime()` at the persistence boundary, not at the policy — the offset is meaningful up to that point), push there first, then the gitlink. Check whether any rows were written under legacy semantics in a dev database before assuming the column is clean | `MIG-10` |
| **Drift risk: there is no sandbox copy of this slice to keep in sync**, and `framework/` is a submodule. The one cross-repo hazard in scope is `CQ-5`'s companion edit to `IActivityTimeAttributionSink`'s XML — that interface lives in `AdhdTimeOrganizer.Core`, *not* in the submodule, so no two-repo dance is needed. If a fix here ever does reach `Sydowwe.Framework*`, commit and push the submodule **first**, then the gitlink | Low | Nothing to do today; noted so nobody assumes a framework edit travels with a parent push | `MIG-7` |
| **Five silent registration sites, plus the sixth this slice added.** `Program.cs` FastEndpoints `o.Assemblies`, `ModuleAssemblies`, `ApplyHostConfigurations`, the `.sln`, and `AddHostedService<TimerBoundaryAlarmRegistrar>()`. All are assembly-anchored and currently correct; the registrar is the one nothing scans for, and dropping it loses every alarm across a restart with no error | Low, catastrophic-quiet | Unchanged from `summary.md` — but note that `TEST` coverage for the registrar is still zero (previously known), so the only thing standing between a bad refactor and silent alarm loss is the doc | `MIG-8` |

## Audit-log gaps

**Auditing is not wired up in this host.** `CLAUDE.md` is explicit: `PiiRedactor` and the audit
interceptor exist in `Sydowwe.Framework` but nothing writes audit rows today. So the findings below are
about *staying ready*, not about repairing a live trail.

| Observation | ID |
|---|---|
| **No `ExecuteUpdateAsync` / `ExecuteDeleteAsync` anywhere in the slice.** Every mutation goes through the ChangeTracker: `ActivityHistoryActivityReferenceSource.RepointAsync` loads and assigns (with a comment saying why a set-based update would be wrong inside the merge transaction), `TimerBoundaryAlarmService.CancelAsync` uses `RemoveRange`, the job handler uses `Remove`. When the interceptor is switched on, this slice needs no changes — which is worth recording, because it is not true of every slice | `AUDIT-1` |
| **The dev seeder's `TruncateTableCascadeAsync<ActivityHistory>()` bypasses the ChangeTracker by design** (raw `TRUNCATE … RESTART IDENTITY CASCADE`). Verified schema-safe: `SeederExtensions.TruncateAsync` resolves the table name *and schema* from the model, which is exactly what `CLAUDE.md`'s hand-written-SQL rule demands. Dev-only, and truncation is not an auditable user action. No change needed | `AUDIT-2` |
| **No `[AuditIgnore]` / `[NoAudit]` attributes on either entity — and none are needed.** `ActivityHistory` carries no PII: activity ids, timestamps, a duration and two nullable task links. `TimerBoundaryAlarm` carries a session guid, an instant, a phase and a relative app route. Neither holds free text a user typed. When auditing is enabled, both are safe to capture whole | `AUDIT-3` |
| **Logging is PII-clean.** Every log statement in the slice logs ids and job keys only — `{AlarmId}`, `{JobKey}`, `{UserId}`, `{Live}`/`{Stale}` counts. `TimerBoundaryAlarmJobHandler` deliberately re-reads the activity name at fire time and never logs it; the job payload is the row id alone, with a comment explaining that the payload is persisted verbatim and logged on failure. This is the standard the rest of the solution should be held to | `AUDIT-4` |
| **The one gap worth naming:** `SEC-2`'s `AddError($"…{ex.Message}")` puts server-side exception text into a *client* response and, because it is not logged, leaves **no** server-side record of the failure at all. That is an observability gap as much as a disclosure one — a grid failing repeatedly for one user produces nothing an operator can find | `AUDIT-5` |

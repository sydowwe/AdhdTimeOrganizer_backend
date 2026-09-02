# History — 01 · Testing

Point-in-time snapshot, 2026-09-01. Consumes `AdhdTimeOrganizer.History/docs/{summary,domain-map,testing}.md`
as the spec. Finding IDs minted here (`TEST-n`) are referenced from `02-findings.md` and
`03-risks-rollout.md`.

## Test infrastructure summary

History has **no test project of its own** — everything lives in `AdhdTimeOrganizer.IntegrationTests`,
which is correct, because nearly every rule the slice owns (the `DbContext` alias, the global
`IEntityWithUser` query filter, the FastEndpoints assembly scan, the membership seam's fan-in, the
host-side cross-slice FKs) is a property of the *composed* app. Infrastructure comes from
`Sydowwe.Framework.Testing`: `PostgresTestBase` over `AppDbContextFixture` (a real Postgres container,
`[Collection("Postgres")]`), `UserRoleTestAuthHandler` (`CreateUserRoleClient()` /
`CreateAdminRoleClient()` / `CreateClient()`), and `FakeLoggedUserService.TestUserId` as the signed-in
id. Two slice-specific setup facts: `AppDbContextFixture.OnSchemaCreatedAsync` installs the three
`sqlScripts/*.sql` materialized views (any save touching `ActivityHistory` fires the host's
`SuggestionPatternRefreshInterceptor`, which `REFRESH`es them — missing → a wave of `42P01`), and the
fixture user's `Timezone` is **UTC**, so any test that means to prove a user-zone fold must seed its
own non-UTC user.

Nothing bespoke or fragile in the harness itself. The one structural fragility is *ownership of
coverage*: eight history suites all seed their own users/activities through near-identical private
helpers (`HistoryCrudAuthMatrixTests`'s `file static class Seed`, `HistoryRouteSmokeTests`'s
`SeedActivityAsync`, `TimerBoundaryAlarmTests`'s own), so a change to the seeding shape has eight
copies to chase. Not worth consolidating today — each is short — but note it before adding a ninth.

## 7. Test quality assessment

**Strengths.**

- **Assertions are on values, not status codes, exactly where that matters.**
  `HistoryTimeOfDayDashboardTests.TotalAcrossHours_MatchesThePieChartTotalForTheSameRange` picks an
  equality between two independent surfaces that can only hold if the arithmetic is right — the single
  best-shaped test in the slice. `HistoryDashboardGroupIdentityTests` (7 tests) does the same for group
  identity across a rename, which is otherwise a well-formed response with a wrong number.
  `HistorySummaryCustomRangeTests.CustomRange_OfExactlyTheMaximumSpan_IsAnswered_AndOneDayMoreIsNot`
  asserts *at* the boundary rather than near it.
- **The route-smoke suite is aware of its own failure mode.**
  `HistoryRouteSmokeTests`'s header explains why `GET /activity-history/{id}` is deliberately *not*
  smoke-tested (a 404 for a missing row is indistinguishable from a 404 for a missing route) and why the
  POST routes assert `NotBe(404) && NotBe(405)` rather than `Be(200)`. That is the right instinct, and
  it is documented in the file rather than in a review.
- **`Grid_MembershipFilter_NarrowsThroughTheSeam` asserts on rows, not routes** — the seam is resolved
  by string key and fails silently, and this test partitions two seeded activities to catch it. Keep it.
- **The CRUD/auth matrix leans on Framework base test classes** (`BaseGetByIdEndpointTests`,
  `BaseCreateEndpointTests`, `BaseUpdateEndpointTests`, `BaseDeleteEndpointTests`,
  `BaseFilterEndpointTests`) and only hand-writes the two shapes the bases don't cover. Cross-user rows
  are asserted to 404, with `UnauthorizedStatus` overridden per class to say so explicitly.
- **AAA and isolation are clean.** Every suite creates its own users with a `Guid.NewGuid():N` email and
  its own activities with unique names, so nothing collides across the shared container; DbContexts are
  `await using`; `TestContext.Current.CancellationToken` is threaded through.

**Weaknesses.**

- **The grid's contract is asserted at exactly one bit of resolution.**
  `ActivityHistoryGetFilteredTableTests.GetFilteredTable_ExcludesOtherUsersHistory` posts
  `itemsPerPage: 100, page: 1, sortBy: []` and asserts the result contains one id. Both paging fields are
  `required` on `BaseFilterSortPaginateRequest` and **neither is read by the endpoint** (see `CQ-1`), and
  no test would notice: 1 row fits in any page. Passing a request field that the server ignores is the
  cheapest possible silent failure, and it is uncovered. → `TEST-1`
- **Route-registration theatre in one place.** `HistoryDashboardRoutes_AreRegistered` posts a shared
  `DashboardBody()` to all seven dashboards and asserts only "not 404, not 405". That is the correct
  assertion *for the question it asks*, but it means five of those seven routes have no other test that
  drives them with a non-default `baseline` / `windowMinutes` / `topN`. The suite is honest about being
  a smoke test; the gap is that nothing sits above it for those parameters. → `TEST-2`, `TEST-7`
- **`CalendarActivityEndpoint` has one test and it is about `roleId`.**
  `HistoryDashboardGroupIdentityTests.Calendar_TopRolesCarryTheRoleId` is the only coverage of the one
  dashboard that reads another slice's entity, bounds its own range, and — unlike every sibling — does so
  in UTC (`DOC-2`). Nothing asserts its day fold, its range bounds or `TopN`. → `TEST-3`
- **The attribution sink has zero tests and sits on a live path.**
  `ActivityHistoryTimeAttributionSink` implements Core's `IActivityTimeAttributionSink` and is called by
  `Tracking.DesktopActivityHeartbeatEndpoint` (lines 90 and 119). The contiguous-window extension rule —
  which `summary.md` calls a property of the ledger's shape and the reason the logic lives here — is
  asserted nowhere, and neither is `TotalSecondsOnDayAsync`, which `CQ-5` argues throws. `SeamWiringTests`
  proves the seam *resolves*; nothing proves it *computes*. → `TEST-4`
- **The timer-alarm suite covers the lifecycle but not the input.** Nine good tests on schedule / cancel /
  fire / push envelope / quiet hours, and none on `ScheduleTimerAlarmsValidator` (the 64-boundary cap, the
  1-day horizon, the relative-`Url` rule that exists to stop a push becoming an off-site redirect) or on a
  foreign `ActivityId` (`SEC-1`). → `TEST-5`
- **Create/update edge cases are untested.** The mutual exclusion of `TodoListItemId` /
  `RoutineTodoListId` is a written rule in `ActivityHistoryValidator` with an explanatory comment and no
  test; the `(UserId, ActivityId, StartTimestamp)` unique index has no test either, so nobody knows
  whether a duplicate create answers 400/409 or a raw 500. → `TEST-6`
- **`ActivityHistoryRequest.UpdateEntity`'s deliberate omission is unpinned.** Not writing the two item
  links on update is load-bearing (writing them would unlink the row from its task on every ordinary
  edit) and is defended by a comment in three places. A future "map all the fields" cleanup deletes it
  with no test failing. → `TEST-8`

**Flakiness risks.** Low. `Seed.HistoryAsync` anchors at `DateTime.UtcNow.Date.AddHours(10)` and the
filter suite queries today's date in UTC — correct while the fixture user is UTC, but a test that runs
across a UTC midnight boundary would seed one day and query another. Not observed; noting it because
`HistorySummaryCustomRangeTests` and `HistoryTimeOfDayDashboardTests` avoid it by pinning explicit dates.

## 8. Coverage matrix

### Endpoints

| Endpoint | Happy | Edge | Auth / IDOR | Test file |
|---|---|---|---|---|
| `POST /activity-history` | ✅ 1 | ❌ `TEST-6` | ✅ 3 | `HistoryCrudAuthMatrixTests` (`ActivityHistoryCreateTests`) |
| `PUT /activity-history/{id}` | ✅ 1 | ❌ `TEST-8` | ✅ 4 (404 on foreign) | `HistoryCrudAuthMatrixTests` |
| `DELETE /activity-history/{id}` | ✅ 1 | — | ✅ 4 | `HistoryCrudAuthMatrixTests` |
| `GET /activity-history/{id}` | ✅ 1 | — | ✅ 4 | `HistoryCrudAuthMatrixTests` |
| `GET /activity-history/form-select-options` | ✅ 1 (200 only) | ❌ no value assertion; `?includeArchived` untested | ⚠️ implicit | `HistoryRouteSmokeTests` |
| `POST /activity-history/filter` | ✅ 2 | ❌ over-midnight / `EndTimestamp <= to` (`CQ-11`) | ✅ 2 | `HistoryCrudAuthMatrixTests` (`ActivityHistoryFilterTests`) |
| `POST /activity-history/gird` | ✅ 2 | ❌ paging + sorting + UTC date grouping `TEST-1` | ✅ 1 | `HistoryRouteSmokeTests`, `HistoryCrudAuthMatrixTests` |
| `POST /activity-history/aggregate-by-activity` | ✅ 1 (values, omits-empty, scoping) | ❌ 500-id cap, empty list | ✅ 1 | `HistoryRouteSmokeTests` |
| `POST …/dashboard/detail/pie-chart` | ✅ 2 | ✅ zone | ⚠️ smoke | `HistoryDashboardGroupIdentityTests`, `…UserZoneTests` |
| `POST …/dashboard/detail/stacked-bars` | ✅ 8 | ✅ window count vs picked range, over-midnight, DST, summary parity (`TEST-7`) | ⚠️ smoke | `HistoryDashboardUserZoneTests` |
| `POST …/dashboard/detail/summary-cards` | ⚠️ smoke only | ❌ baselines `TEST-2` | ⚠️ smoke | `HistoryRouteSmokeTests` |
| `POST …/dashboard/summary/pie-chart` | ✅ 4 | ✅ range + zone | ⚠️ smoke | `…GroupIdentityTests`, `…CustomRangeTests` |
| `POST …/dashboard/summary/stacked-bars` | ✅ 3 | ✅ zone + DST gap | ⚠️ smoke | `HistoryDashboardUserZoneTests` |
| `POST …/dashboard/summary/summary-cards` | ✅ 2 | ❌ baselines `TEST-2` | ⚠️ smoke | `…GroupIdentityTests`, `…CustomRangeTests` |
| `POST …/dashboard/summary/time-of-day` | ✅ 5 | ✅ 24-bucket / zone / split / totals equality | ⚠️ smoke | `HistoryTimeOfDayDashboardTests` |
| `POST …/dashboard/calendar` | ⚠️ 1 (`roleId` only) | ❌ zone, range bounds, `TopN` `TEST-3` | ❌ no cross-user test | `HistoryDashboardGroupIdentityTests` |
| `POST …/timer-alarm/schedule` | ✅ 2 | ❌ validator + foreign `ActivityId` `TEST-5` | ⚠️ implicit (`User.GetId()`) | `TimerBoundaryAlarmTests` |
| `POST …/timer-alarm/cancel` | ✅ 2 | ✅ unknown session = no-op | ❌ no cross-user cancel test `TEST-5` | `TimerBoundaryAlarmTests` |

⚠️ *smoke* = the route is proven registered and answers, but no test drives it with a second user or a
non-default parameter.

### Services / handlers / jobs / seeders / seams

| Unit | Tests | Notes |
|---|---|---|
| `TimerBoundaryAlarmService` | 5 (via endpoints) | `ReplaceSessionAsync` idempotency and past-boundary drop are pinned; `RegisterTriggerAsync`'s swallow-and-log path is not |
| `TimerBoundaryAlarmJobHandler` | 3 | fires → self-drains; no-op on a cancelled row; push envelope shape |
| `TimerBoundaryAlarmRegistrar` | **0** | previously known standing caveat (`docs/testing.md` → Known gaps); needs a host restart inside a fixture |
| `ActivityHistoryTimeAttributionSink` | **0** | `SeamWiringTests` proves resolution only → `TEST-4` |
| `TodoListItemLoggedTimeSource` | 2 | `TodoListItemDailyRecapTests` — keys on item id, and `SET NULL` keeps the recording |
| `ActivityHistoryActivityReferenceSource` | ✅ | `ActivityMergeTests.Merge_RepointsEveryReferencingSlice` (spans seven slices) + `ActivityForeignKeyInventoryTests` |
| `HistoryGrouping` | 7 (indirect) | every group-shaped response asserts `groupId` |
| `ActivityHistoryValidator` | 0 direct | `TEST-6` |
| `HistorySummaryDateRangeValidators` | 4 | `EveryFourthSummaryEndpoint_RejectsACustomRangeItWillNotAnswer` — the "fifth endpoint needs a fifth line" guard |
| `ScheduleTimerAlarmsValidator` / `ActivityHistoryAggregateByActivityValidator` | 0 | `TEST-5` |
| `ActivityHistorySeeder` | **0** | previously known (dev-only fixture; the chain is pinned by `PerUserDefaultSeederTests`) |
| Model-level guards | ✅ | `ModuleSchemaTests` (both tables in `history`), `UserDataExportAndDeletionTests`, `NamedQueryFilterTests`, `SuggestionPatternRefreshTests` |

### You can ship this: **~70%**

Read-and-authorization surface is genuinely well covered — the CRUD auth matrix plus the global query
filter mean the classic IDOR shapes are closed and *proven* closed, and the four dashboards that carry
the hard arithmetic (`summary/*`) are tested on values rather than status codes, which is the standard
this slice's docs set. The 30% that is missing is not evenly spread: it is concentrated in the three
places where a failure is a plausible number rather than an error — **the grid's ignored paging
contract**, **the summary-cards baselines** (including one, `alltime`, that inspection says throws), and
**the calendar dashboard's UTC fold**. Add to that the attribution sink, which has zero tests despite
sitting on the desktop-tracking write path. Ranked by expected damage: `TEST-4` (silent wrong totals on
a live ingest path, plus a suspected crash) > `TEST-2` (a 500 on a shipped `baseline` value) >
`TEST-1` (whole ledger returned per grid call) > `TEST-3` > `TEST-7` > `TEST-5` > `TEST-6` > `TEST-8`.

## 12. Missing tests — backlog

Prompts are self-contained and paste-ready for a context-less agent; all live under
`review/history/testingPrompts/`. Read `_SHARED-CONTEXT.md` first — every prompt assumes it.

| ID | Gap | Sev | Prompt |
|---|---|---|---|
| `TEST-1` | The grid ignores `page` / `itemsPerPage` / `sortBy`; nothing notices | 🟠 | [`GridPaginationAndSorting.md`](testingPrompts/GridPaginationAndSorting.md) |
| `TEST-2` | `summary-cards` `baseline` values (`alltime` suspected 500, `sameWeekday`, `last30days`) never driven | 🔴 | [`SummaryCardsBaselines.md`](testingPrompts/SummaryCardsBaselines.md) |
| `TEST-3` | Calendar dashboard: day fold, range bounds, `TopN`, cross-user | 🟠 | [`CalendarDashboardBounds.md`](testingPrompts/CalendarDashboardBounds.md) |
| `TEST-4` | `ActivityHistoryTimeAttributionSink` — contiguous-window extension and day totals, both untested on a live path | 🔴 | [`TimeAttributionSink.md`](testingPrompts/TimeAttributionSink.md) |
| `TEST-5` | `ScheduleTimerAlarmsValidator` caps + foreign `ActivityId` + cross-user cancel | 🟠 | [`TimerAlarmInputValidation.md`](testingPrompts/TimerAlarmInputValidation.md) |
| `TEST-6` | `ActivityHistoryValidator` mutual exclusion; duplicate `(UserId, ActivityId, StartTimestamp)`; foreign `ActivityId` on create | 🟠 | [`ActivityHistoryWriteRules.md`](testingPrompts/ActivityHistoryWriteRules.md) |
| `TEST-7` ✅ **written 2026-09-02** | `detail/stacked-bars` emits 24 windows regardless of the picked range | 🟠 | [`DetailStackedBarsWindows.md`](testingPrompts/DetailStackedBarsWindows.md) — landed in `HistoryDashboardUserZoneTests` alongside the `CQ-3` fix |
| `TEST-8` | `UpdateEntity` must not write the two item links | 🟡 | [`UpdateKeepsItemLinks.md`](testingPrompts/UpdateKeepsItemLinks.md) |

**Previously known — do not re-flag** (`docs/testing.md` → Known gaps): the registrar's boot
re-registration, `ActivityHistorySeeder`, and the accepted residual cancel race (a tab killed between
Stop and the cancel landing). No `[Trait("Status","KnownGap")]` tests are carried for this slice, so
anything new that cannot be fixed immediately should be pinned with that trait rather than left silent.

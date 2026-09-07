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

~~Nothing bespoke or fragile in the harness itself.~~ **Superseded by `INFRA-1` below**, added 2026-09-02
while fixing `CQ-4`: the harness sets one `AppContext` switch that the host does not, and it is currently
hiding three separate production defects. The rest of this paragraph stands. The one *other* structural
fragility is *ownership of coverage*: eight history suites all seed their own users/activities through
near-identical private helpers (`HistoryCrudAuthMatrixTests`'s `file static class Seed`,
`HistoryRouteSmokeTests`'s `SeedActivityAsync`, `TimerBoundaryAlarmTests`'s own), so a change to the
seeding shape has eight copies to chase. Not worth consolidating today — each is short — but note it
before adding a ninth.

## Test-harness integrity

`INFRA-n` rather than `TEST-n` on purpose. A `TEST-n` is a gap — a rule nothing asserts, closed by
writing a test. The finding below is the opposite failure: tests that *do* run, *do* pass, and prove
less than they appear to. Closing it means changing the harness, and until it is closed, adding
`TEST-n` coverage in the affected area buys less than the matrix in § 8 implies.

### `INFRA-1` 🔴 · The test harness enables `Npgsql.EnableLegacyTimestampBehavior`; the host does not

`framework/Sydowwe.Framework.Testing/PostgresContainerFixture.cs:26` (submodule)

```csharp
static PostgresContainerFixture()
{
    AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
```

This is the **only** occurrence of that switch in the solution. `Program.cs` does not set it, and no
`appsettings*.json` or launch profile does either — so every integration test in this repo runs under
`DateTime`/`DateTimeOffset` semantics that production does not have. Legacy mode accepts an
`Unspecified`- or `Local`-kind `DateTime`, and a `DateTimeOffset` with a non-zero offset, at a
`timestamptz` parameter; strict mode — what the deployed app runs — throws `ArgumentException` on all
three. The switch is process-wide and set in a static constructor, so it applies to every suite in the
assembly regardless of which fixture a test uses.

**The consequence is not "some tests are weak".** It is that *no round-trip test in this repo can fail
on a parameter-kind bug*, which is a defect class this codebase produces steadily: it is timezone-heavy
(`WallClockZone`, per-user `TimeZoneInfo`, quiet hours, DST cases) and every dashboard, ledger and
notification column is `timestamptz`. `CQ-4` is one instance; the review flagged it and `CQ-5` as
**PLAUSIBLE** precisely because it could not tell whether the rule was in force, and the preamble to
`02-findings.md` reasoned "nothing in the solution sets `EnableLegacyTimestampBehavior`" — true of the
host, false of the harness that would have had to catch them.

**Measured, not argued.** Flipping the switch to `false` and running the full suite unchanged:

| | Switch on (today) | Switch off |
|---|---|---|
| Result | 1626 total, **0 failed**, 144 skipped | 1626 total, **6 failed**, 144 skipped |

Five of those six are **production** code throwing, not test code:

| Failing test | Throws in | Kind |
|---|---|---|
| `ExtensionActivityTrackingTests.Heartbeat_MappedEntry_AttributesTimeAndCompletesPlannerTask` ✅ fixed | `History.ActivityHistoryTimeAttributionSink.TotalSecondsOnDayAsync:60`, via `Tracking.DesktopActivityHeartbeatEndpoint:117` → **500 on the desktop heartbeat ingest path** | `Unspecified` |
| `TrackingDashboardDateRangeTests.ProcessDetails_ApplyTheDailyWindowToTheEnvelopeItIsOpenedWith` ✅ fixed | `Tracking.DesktopProcessDetailsEndpoint.HandleAsync:32` → **500 on `POST /api/activity-tracking/desktop/pie-chart`'s details panel** | `Local` |
| `QuietHoursUserZoneAndLateDeliveryTests.QuietHours_AreEvaluatedInEachRecipientsOwnZone_NotOneDeploymentZone` | `Sydowwe.Notifications.NotificationService.NotifyAsync:129` (submodule) | `DateTimeOffset` `+12:00` |
| `…DeferredUntil_IsTheWindowEndInTheRecipientsOwnZone` | same | `DateTimeOffset` `+12:00` |
| `…QuietHoursDeferral_CrossesSpringForward_AndFlushesWebPushAndEmail` | same | `DateTimeOffset` `+02:00` |
| `RepeatingPlannerTaskSuggestionsTests.Tier2Suppresses_Tier3` | the test's own `SeedActivityHistoryOnWeekdayAsync:198` — the one genuine test-side bug | `Unspecified` |

So the switch is hiding **three distinct production defects**, only one of which the review had found:

1. **`CQ-5`, now confirmed rather than plausible.** The attribution sink threw a real 500 on the desktop
   heartbeat — a live ingest path. (Fixed in the working tree on 2026-09-02; this run predates the fix
   and is the independent confirmation `CQ-5` asked for.)
2. **`DesktopProcessDetailsEndpoint`** — new and unreported when this was written; **fixed 2026-09-02**.
   `DailyWindowMaskRequest.From`/`To` are `[QueryParam] DateTime`, bound straight from the query string
   into a `timestamptz` comparison. FastEndpoints yields `Utc` only for a `Z`-suffixed value; an
   offset-bearing string gives `Local` and a bare one gives `Unspecified`. **Two of the three shapes a
   client can legitimately send 500'd this endpoint**, and both details endpoints share the base.
   Outside History's slice — belongs to Tracking's review, raised here because this is where the
   evidence surfaced.

   **Fixed:** `From`/`To` normalize on binding via a private `AsUtcInstant` (`Local` →
   `ToUniversalTime()`, otherwise `SpecifyKind(Utc)`) — the same close every other client-supplied
   instant here gets (`ScheduleSpec.RunAtUtc`, `AndroidSyncEndpoint`). Applied to the shared base, so
   `domain-details` is covered by the same change. `Unspecified` reads as UTC because these two are an
   instant envelope that `DailyWindowSet.FromEnvelope` already consumes through `WallClockZone.FromUtc`.
   Pinned by `DailyWindowMaskRequestKindTests` — asserting on the DTO, not through a request, for the
   reason this whole finding exists; verified by re-running
   `TrackingDashboardDateRangeTests` with the switch off, where it now passes.
3. **`Sydowwe.Notifications.NotificationService.NotifyAsync` persists a zoned `DateTimeOffset`.** The
   quiet-hours deferral computes `deferredUntil` on the *recipient's* clock and saves it with that
   offset attached. It only carries a non-zero offset when the recipient is **not** in UTC — i.e. this
   throws for exactly the users the feature exists for, and never for the UTC fixture user. Three tests
   written specifically to prove per-recipient zone handling all die on the write. In the submodule, so
   fixing it is a two-repo operation.

**Fix, in order.** The three defects first — they are live regardless of what the harness does. Two are
done (`CQ-5`'s sink and the details endpoint); **the notification one is the outstanding blocker**, and
it is the one with real user exposure. Then the harness: delete the switch. Only one test genuinely depends on it
(`RepeatingPlannerTaskSuggestionsTests`, a one-line seeding fix), and the whole test project contains
just **5 kind-less `new DateTime(...)` literals across 2 files**, so the cleanup is small and bounded.
Deleting it is what makes every future round-trip test load-bearing for this defect class. Until then,
any assertion about a `timestamptz` parameter's kind must be made **directly on the value** rather than
through a request — see `HistorySummaryCardsBaselineTests.ResolvedBaseline_HasNoLowerBoundThatIsNotUtc`,
which is the shape to copy, and note it is the *only* assertion in that file that fails when `CQ-4`'s
sentinel is put back.

Two smaller harness notes found alongside, neither worth its own ID: the fixture pins the host port with
`WithPortBinding(5432, 5432)`, so one leaked container makes every subsequent run fail at
`InitializeAsync` with `port is already allocated` rather than anything diagnostic; and it also sets
`FIELD_ENCRYPTION_KEY` in the same static constructor, which is fine but means the constructor is now
doing two unrelated global things and deserves a comment saying so.

`Doc impact:` `docs/testing.md` should state that the harness diverges from the host here, for as long
as it does. **`02-findings.md`'s preamble has been corrected** — it asserted the switch was set nowhere.

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
- **The membership-seam test asserts on rows, not routes** — the seam is resolved by string key and
  fails silently, and the test partitions two seeded activities to catch it. Keep it. *(2026-09-02: it
  ran through the `gird` grid as `Grid_MembershipFilter_NarrowsThroughTheSeam`; that endpoint was
  deleted with `CQ-1`, so it now lives on the source itself as
  `SeamWiringTests.TodoListMembershipSource_NarrowsToActivitiesOnAList`.)*
- **The CRUD/auth matrix leans on Framework base test classes** (`BaseGetByIdEndpointTests`,
  `BaseCreateEndpointTests`, `BaseUpdateEndpointTests`, `BaseDeleteEndpointTests`,
  `BaseFilterEndpointTests`) and only hand-writes the two shapes the bases don't cover. Cross-user rows
  are asserted to 404, with `UnauthorizedStatus` overridden per class to say so explicitly.
- **AAA and isolation are clean.** Every suite creates its own users with a `Guid.NewGuid():N` email and
  its own activities with unique names, so nothing collides across the shared container; DbContexts are
  `await using`; `TestContext.Current.CancellationToken` is threaded through.

**Weaknesses.**

- ~~**The grid's contract is asserted at exactly one bit of resolution.**~~ ✅ **MOOT 2026-09-02.**
  `ActivityHistoryGetFilteredTableTests.GetFilteredTable_ExcludesOtherUsersHistory` posted
  `itemsPerPage: 100, page: 1, sortBy: []` and asserted the result contained one id; neither paging field
  was read by the endpoint. The endpoint was deleted with `CQ-1` — no client ever called it — so `TEST-1`
  is withdrawn rather than written, and `/filter` (which the SPA does call) keeps its own cross-user
  assertion.
- **Route-registration theatre in one place.** `HistoryDashboardRoutes_AreRegistered` posts a shared
  `DashboardBody()` to all seven dashboards and asserts only "not 404, not 405". That is the correct
  assertion *for the question it asks*, but it means five of those seven routes have no other test that
  drives them with a non-default `baseline` / `windowMinutes` / `topN`. The suite is honest about being
  a smoke test; the gap is that nothing sits above it for those parameters. → `TEST-2`, `TEST-7`
- ~~**`CalendarActivityEndpoint` has one test and it is about `roleId`.**~~ ✅ **CLOSED 2026-09-07** by
  `HistoryCalendarDashboardTests`; the `DOC-2` fold it describes was fixed in the same change.
  `HistoryDashboardGroupIdentityTests.Calendar_TopRolesCarryTheRoleId` is the only coverage of the one
  dashboard that reads another slice's entity, bounds its own range, and — unlike every sibling — does so
  in UTC (`DOC-2`). Nothing asserts its day fold, its range bounds or `TopN`. → `TEST-3`
- ~~**The attribution sink has zero tests and sits on a live path.**~~ ✅ **closed 2026-09-07** by
  `ActivityTimeAttributionSinkTests`, which also turned up three `KnownGap`s the prompt did not
  anticipate — see the `TEST-4` note under § 12. The original text follows.
  `ActivityHistoryTimeAttributionSink` implements Core's `IActivityTimeAttributionSink` and is called by
  `Tracking.DesktopActivityHeartbeatEndpoint` (lines 90 and 119). The contiguous-window extension rule —
  which `summary.md` calls a property of the ledger's shape and the reason the logic lives here — is
  asserted nowhere, and neither is `TotalSecondsOnDayAsync`, which `CQ-5` argues throws. `SeamWiringTests`
  proves the seam *resolves*; nothing proves it *computes*. → `TEST-4`
- **The timer-alarm suite covers the lifecycle but not the input.** Nine good tests on schedule / cancel /
  fire / push envelope / quiet hours, and none on `ScheduleTimerAlarmsValidator` (the 64-boundary cap, the
  1-day horizon, the relative-`Url` rule that exists to stop a push becoming an off-site redirect) or on a
  foreign `ActivityId` (`SEC-1`). → `TEST-5`
- ~~**Create/update edge cases are untested.**~~ ✅ **CLOSED 2026-09-07** by
  `ActivityHistoryWriteRuleTests`. The mutual exclusion of `TodoListItemId` / `RoutineTodoListId` is now
  driven on all four combinations and asserts the *error key* (the rule's `.WithName` is what lets the
  SPA put the message beside a field); the duplicate `(UserId, ActivityId, StartTimestamp)` answers
  **409**, not the 500 this bullet feared, because `DbUtils.HandleException` maps the unique violation.
  The pass found one defect inspection had missed → `CQ-13`. → `TEST-6`
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
| `POST /activity-history` | ✅ 1 | ✅ 10 (`TEST-6`: mutual exclusion + keys, length rules, duplicate → 409) | ✅ 3 + 4 (`SEC-4`) | `HistoryCrudAuthMatrixTests` (`ActivityHistoryCreateTests`), `ActivityHistoryWriteRuleTests`, `ActivityHistoryReferencedIdOwnershipTests` |
| `PUT /activity-history/{id}` | ✅ 1 | ⚠️ `EndTimestamp` recompute pinned (`TEST-6`); item links still `TEST-8` | ✅ 4 (404 on foreign) | `HistoryCrudAuthMatrixTests`, `ActivityHistoryWriteRuleTests` |
| `DELETE /activity-history/{id}` | ✅ 1 | — | ✅ 4 | `HistoryCrudAuthMatrixTests` |
| `GET /activity-history/{id}` | ✅ 1 | — | ✅ 4 | `HistoryCrudAuthMatrixTests` |
| `GET /activity-history/form-select-options` | ✅ 1 (200 only) | ❌ no value assertion; `?includeArchived` untested | ⚠️ implicit | `HistoryRouteSmokeTests` |
| `POST /activity-history/filter` | ✅ 2 | ❌ over-midnight / `EndTimestamp <= to` (`CQ-11`) | ✅ 2 | `HistoryCrudAuthMatrixTests` (`ActivityHistoryFilterTests`) |
| `POST /activity-history/aggregate-by-activity` | ✅ 1 (values, omits-empty, scoping) | ❌ 500-id cap, empty list | ✅ 1 | `HistoryRouteSmokeTests` |
| `POST …/dashboard/detail/pie-chart` | ✅ 2 | ✅ zone | ⚠️ smoke | `HistoryDashboardGroupIdentityTests`, `…UserZoneTests` |
| `POST …/dashboard/detail/stacked-bars` | ✅ 8 | ✅ window count vs picked range, over-midnight, DST, summary parity (`TEST-7`) | ⚠️ smoke | `HistoryDashboardUserZoneTests` |
| `POST …/dashboard/detail/summary-cards` | ⚠️ smoke only | ❌ baselines `TEST-2` | ⚠️ smoke | `HistoryRouteSmokeTests` |
| `POST …/dashboard/summary/pie-chart` | ✅ 4 | ✅ range + zone | ⚠️ smoke | `…GroupIdentityTests`, `…CustomRangeTests` |
| `POST …/dashboard/summary/stacked-bars` | ✅ 3 | ✅ zone + DST gap | ⚠️ smoke | `HistoryDashboardUserZoneTests` |
| `POST …/dashboard/summary/summary-cards` | ✅ 2 | ❌ baselines `TEST-2` | ⚠️ smoke | `…GroupIdentityTests`, `…CustomRangeTests` |
| `POST …/dashboard/summary/time-of-day` | ✅ 5 | ✅ 24-bucket / zone / split / totals equality | ⚠️ smoke | `HistoryTimeOfDayDashboardTests` |
| `POST …/dashboard/calendar` | ✅ 18 | ✅ user-zone fold both directions, range edges, empty days, `TopN` + its bound, span cap at the boundary (`TEST-3`) | ✅ 1 (days *and* seconds) | `HistoryCalendarDashboardTests`, `HistoryDashboardGroupIdentityTests` |
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
| `ActivityHistoryTimeAttributionSink` | 12 | `ActivityTimeAttributionSinkTests` — `TEST-4` ✅ **written 2026-09-07**. Both rules pinned (no-`SaveChanges`, contiguous-window extension), plus `CQ-5` as a regression. Surfaced three new `KnownGap`s: `IntTime` truncates sub-minute seconds to zero, which also makes the extension rule unreachable for real heartbeats, and `AttributeAsync` does not guard a negative `activeSeconds` |
| `TodoListItemLoggedTimeSource` | 2 | `TodoListItemDailyRecapTests` — keys on item id, and `SET NULL` keeps the recording |
| `ActivityHistoryActivityReferenceSource` | ✅ | `ActivityMergeTests.Merge_RepointsEveryReferencingSlice` (spans seven slices) + `ActivityForeignKeyInventoryTests` |
| `HistoryGrouping` | 7 (indirect) | every group-shaped response asserts `groupId` |
| `ActivityHistoryValidator` | 7 (through the two endpoints) | `ActivityHistoryWriteRuleTests` — every rule, asserted on the error key; `CQ-13` came out of it |
| `HistorySummaryDateRangeValidators` | 4 | `EveryFourthSummaryEndpoint_RejectsACustomRangeItWillNotAnswer` — the "fifth endpoint needs a fifth line" guard |
| `ScheduleTimerAlarmsValidator` / `ActivityHistoryAggregateByActivityValidator` | 0 | `TEST-5` |
| `ActivityHistorySeeder` | **0** | previously known (dev-only fixture; the chain is pinned by `PerUserDefaultSeederTests`) |
| Model-level guards | ✅ | `ModuleSchemaTests` (both tables in `history`), `UserDataExportAndDeletionTests`, `NamedQueryFilterTests`, `SuggestionPatternRefreshTests` |

### You can ship this: **~70%**

Read-and-authorization surface is genuinely well covered — the CRUD auth matrix plus the global query
filter mean the classic IDOR shapes are closed and *proven* closed, and the four dashboards that carry
the hard arithmetic (`summary/*`) are tested on values rather than status codes, which is the standard
this slice's docs set. The 30% that is missing is not evenly spread: it is concentrated in the three
places where a failure is a plausible number rather than an error — ~~the grid's ignored paging
contract~~ (endpoint deleted, `CQ-1`), **the summary-cards baselines** (including one, `alltime`, that inspection says throws), and
**the calendar dashboard's UTC fold**. Add to that the attribution sink, which has zero tests despite
sitting on the desktop-tracking write path. Ranked by expected damage: `TEST-4` (silent wrong totals on
a live ingest path, plus a suspected crash) > `TEST-2` (a 500 on a shipped `baseline` value) >
`TEST-3` > `TEST-7` > `TEST-5` > `TEST-6` > `TEST-8`. (`TEST-1` — the whole ledger returned per grid
call — is withdrawn: the endpoint is gone.)

## 12. Missing tests — backlog

Prompts are self-contained and paste-ready for a context-less agent; all live under
`review/history/testingPrompts/`. Read `_SHARED-CONTEXT.md` first — every prompt assumes it.

| ID | Gap | Sev | Prompt |
|---|---|---|---|
| ~~`TEST-1`~~ | ~~The grid ignores `page` / `itemsPerPage` / `sortBy`; nothing notices~~ — **withdrawn 2026-09-02**: endpoint deleted with `CQ-1`, no client ever called it | — | ~~`GridPaginationAndSorting.md`~~ |
| `TEST-2` | `summary-cards` `baseline` values (`alltime` suspected 500, `sameWeekday`, `last30days`) never driven | 🔴 | [`SummaryCardsBaselines.md`](testingPrompts/SummaryCardsBaselines.md) |
| `TEST-3` ✅ **written 2026-09-07** | Calendar dashboard: day fold, range bounds, `TopN`, cross-user | 🟠 | [`CalendarDashboardBounds.md`](testingPrompts/CalendarDashboardBounds.md) — landed as `HistoryCalendarDashboardTests` (17 tests) alongside the `DOC-2` fix |
| `TEST-4` ✅ **written 2026-09-07** | `ActivityHistoryTimeAttributionSink` — contiguous-window extension and day totals, both untested on a live path | 🔴 | [`TimeAttributionSink.md`](testingPrompts/TimeAttributionSink.md) — landed as `ActivityTimeAttributionSinkTests` (12 tests); see the note below |
| `TEST-5` | `ScheduleTimerAlarmsValidator` caps + foreign `ActivityId` + cross-user cancel | 🟠 | [`TimerAlarmInputValidation.md`](testingPrompts/TimerAlarmInputValidation.md) |
| `TEST-6` ✅ **written 2026-09-07** | `ActivityHistoryValidator` mutual exclusion; duplicate `(UserId, ActivityId, StartTimestamp)`; foreign `ActivityId` on create | 🟠 | [`ActivityHistoryWriteRules.md`](testingPrompts/ActivityHistoryWriteRules.md) — landed as `ActivityHistoryWriteRuleTests` (12 tests); found `CQ-13` |
| `TEST-7` ✅ **written 2026-09-02** | `detail/stacked-bars` emits 24 windows regardless of the picked range | 🟠 | [`DetailStackedBarsWindows.md`](testingPrompts/DetailStackedBarsWindows.md) — landed in `HistoryDashboardUserZoneTests` alongside the `CQ-3` fix |
| `TEST-8` | `UpdateEntity` must not write the two item links | 🟡 | [`UpdateKeepsItemLinks.md`](testingPrompts/UpdateKeepsItemLinks.md) |

`TEST-4` ✅ **written 2026-09-07** — `ActivityTimeAttributionSinkTests`, 12 tests, all passing. Notes for
whoever reads the matrix above:

- **`CQ-5` is closed and now genuinely guarded.** Both items were already fixed in the source
  (commit `e624797`); the two tests that would have caught them are kept as regressions. They are
  load-bearing again only because the submodule has since dropped the harness's
  `Npgsql.EnableLegacyTimestampBehavior` switch (`INFRA-1`, commit `ab0547d`) — under that switch a
  round trip could not have failed on the `Unspecified` bound, which is exactly why the prompt's
  scenario 5 expected a throw that no longer happens.
- **Three new findings, pinned rather than fixed**, all rooted in `IntTime` keeping only hours and
  minutes. The headline one: a sub-minute attribution is recorded with a **zero** `Length` while its
  `EndTimestamp` is written honestly, so it is a wrong number rather than an error — this slice's
  stated worst failure mode, on the desktop ingest path. Since the heartbeat validator caps a window at
  60 active seconds across all entries, any window split over two applications loses **both**
  attributions; and because `EndTimestamp` then never lands on the minute boundary the next window
  starts at, the contiguous-window extension rule — the reason this class lives in History at all — can
  only ever fire when a single activity holds all 60 seconds. The fix is in `Sydowwe.Framework`'s value
  object (the column is already `int` seconds), so it is a submodule change with solution-wide reach
  and was left out of this test-only change. Details in `AdhdTimeOrganizer.History/docs/testing.md`
  § *Known gaps*.

`TEST-2` ✅ **written 2026-09-02** — `HistorySummaryCardsBaselineTests` drives all four baselines on both
summary-cards routes, landed with the `CQ-4` fix. Left in the table above rather than struck through,
because it is the one entry whose severity was *understated*: the prompt assumed a round trip would
catch the `alltime` 500, and `INFRA-1` shows it cannot. The suite it produced is still worth having —
it pins what the baselines *mean* — but the kind assertion had to be made on the resolver instead.

**Previously known — do not re-flag** (`docs/testing.md` → Known gaps): the registrar's boot
re-registration, `ActivityHistorySeeder`, and the accepted residual cancel race (a tab killed between
Stop and the cancel landing). No `[Trait("Status","KnownGap")]` tests are carried for this slice, so
anything new that cannot be fixed immediately should be pinned with that trait rather than left silent.

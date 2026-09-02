# AdhdTimeOrganizer.History — Agent Summary

**Purpose:** The activity-history slice. Owns the `ActivityHistory` entity, its CRUD/read endpoints,
the seven dashboard endpoints (three `HistoryDetail*`, four `HistorySummary*`), and
the timer-boundary alarms.

**Third project of the portal split**, after `AdhdTimeOrganizer.Core` and
`AdhdTimeOrganizer.TodoLists`. Plan: `review/portal/slicePrompts/00-README.md`.

## Bounded context

Owns: `ActivityHistory` + its EF configuration, 13 endpoints, the history request/response/filter
DTOs, `ActivityHistoryValidator`, the four `summary/` range validators
(`HistorySummaryDateRangeValidators.cs` — one concrete class per request type, because FastEndpoints
resolves validators by exact type and skips validation silently without one), and the dev
`ActivityHistorySeeder` (Order 300, inside its band —
see `AdhdTimeOrganizer.Core/infrastructure/persistence/seeder/SeederOrderBands.md`).

Does **not** own:

- **The `Calendar` entity.** `CalendarActivityEndpoint` (`/activity-history/dashboard/calendar`) *is*
  here now — it moved in when Planning was extracted, because the response is a history summary — but
  it reaches the calendar through Core's **`ICalendarDayLookup`** seam rather than by naming
  Planning's entity. Do not replace that lookup with a direct query; it is the only reason this route
  can live beside the rest of the dashboards.
- **The suggestion-pattern machinery** — `SuggestionPatternRefreshInterceptor`,
  `SuggestionPatternRefreshQueue`, `SuggestionPatternView`, `SuggestionPatternViewInstaller`,
  `SuggestionPatternRefreshJob`, `PlannerSuggestionFromActivityHistory` + its configuration (both
  since moved to Planning), and the three
  `sqlScripts/*.sql` materialized views. These span History + Planning + Calendar and are wired into
  `Program.cs`. The interceptor fires on any save touching `ActivityHistory`, so the host now
  references this slice — that is host → slice, which is the correct direction.
- **The three tracking configurations** that used to sit in the host's
  `infrastructure/persistence/configuration/activityHistory/` folder (`DesktopActivityEntry`,
  `WebExtensionActivityEntry`, `AndroidSessionData`). The folder name lied; they belonged to Tracking
  and moved to `AdhdTimeOrganizer.Tracking` with their entities. The folder is gone.

## What History gained in the Tracking extraction

`application/seam/ActivityHistoryTimeAttributionSink.cs` implements Core's
**`IActivityTimeAttributionSink`**: "record N seconds against this activity". Tracking's desktop
heartbeat used to write `ActivityHistory` rows inline, which was a `Tracking → History` project
reference; the write now comes through this seam, and History is the sole implementer.

Two rules, both load-bearing:

- **It mutates the `DbContext` handed in and must not call `SaveChanges`.** The caller's own save is
  the transaction, which is what keeps a tracked entry atomic with its attribution.
- The contiguous-window extension rule (extend an existing row whose `EndTimestamp` equals the new
  window's start, rather than inserting) is a property of the **ledger's shape**, which is why it
  belongs here and not in the ingest endpoint it moved out of.

Unlike `IActivityMembershipSource` this is resolved as a **single** service, deliberately: a missing
registration throws at endpoint activation rather than silently dropping every attribution write.

## Timer-boundary alarms

`TimerBoundaryAlarm` + `TimerBoundaryAlarmService` + `TimerBoundaryAlarmJobHandler` +
`TimerBoundaryAlarmRegistrar`, and two endpoints under `/activity-history/timer-alarm/`.

**The problem:** `runningTimerStore` fired the end-of-timer alarm from a `setTimeout` in the tab, and a
`setTimeout` cannot fire in a tab that has been closed — so closing the lid on a 25-minute focus phase meant
nothing rang. The client now posts the whole cycle's boundaries when the user presses **Start** (a 4-period,
2-cycle pomodoro is 15 of them, batched: rescheduling per phase would need the live tab whose absence is the
bug), and the server rings them.

**This is not timer state and must not become it.** The countdown stays client-side, the server is never
authoritative for `endsAt`, there is no heartbeat and no polling. Nothing reads these rows to reconstruct a
session — the on-screen countdown has to be client-side anyway, and the store's reconstruction is exact
precisely because it compares one device's clock against itself.

Things that are load-bearing and not obvious:

- **Scheduler, not Reminders — deliberately.** Reminders has no per-reminder trigger by design; everything
  fires from one sweep whose cadence (`ReminderScanOptions.CadenceMinutes`, default **5**) is documented as
  the firing-precision floor. Ample for "probation ends in 30 days", useless for a pomodoro.
  `ScheduleSpec.RunOnceAt` is a real trigger on the exact instant. The cost is one live trigger per
  boundary — the thing Reminders refuses to do — which is fine here and would not be at thousands of
  concurrent timers. Reminders' register endpoint is Admin/Root-only diagnostic surface besides; users get
  this slice's own endpoint, the same split `Planning.ReminderRegistrationService` makes. Neither
  timer-alarm endpoint narrows `AllowedRoles()` — **User + Admin + Root by default, deliberately**: a timer
  is ordinary user surface, and that is the whole contrast with Reminders' Admin/Root-only register route.
- **`TimerBoundaryAlarmRegistrar` is not optional.** The Quartz job store is in-memory (deliberate, see
  `SchedulerQuartzConfig`), so a restart loses *every* trigger and there is no generic reconciliation. Each
  owner re-pushes on boot. Drop this hosted service and nothing breaks at build time, nothing throws, and
  every alarm registered before a deploy silently never fires. Re-registration is safe unconditionally: the
  job key is derived from the row id (reschedules in place) and the run-log dedup, which survives the
  restart, recognises an already-fired instant.
- **`dueAt` is an absolute instant, scheduled verbatim.** It was computed from the client's own clock;
  re-deriving it from a duration against the server clock puts the two clocks' skew into the alarm. It must
  carry an offset on the wire (a trailing `Z` is the usual one) — the validator rejects an offset-less
  instant rather than reading it as server-local, which is what it silently did before.
- **`schedule` replaces, but not atomically.** `ReplaceSessionAsync` deletes the old rows in one save and
  writes the new ones in another, so two concurrent POSTs for one session can interleave. Left that way on
  purpose: the cost is a duplicate push under the same `Tag`, which the client's own tag replacement
  collapses, and a transaction would have to span a Scheduler call.
- **The table self-drains** — deleted on fire, on cancel, and by the registrar's stale sweep — which is why
  it has no retention job and no `FiredAt` column. Rows live minutes. That is also why `ActivityId` is a
  plain column with no FK: an orphan degrades to a name-less body and is gone within the hour.
- **`ActivityId` ownership is checked at the schedule endpoint, and nowhere else can check it.** The
  recipient is always the caller and needs no check, but `ActivityId` is a client-supplied id for someone
  else's table: no FK means the database does not check it, and the fire-time handler runs
  **unauthenticated**, where the global filter (`ScopeUserId == null || …`) passes *every* row. So the
  endpoint verifies `Activity.UserId == User.GetId()` before the rows are written — otherwise posting a
  stranger's sequential id returns their activity name in the push body. The handler's name lookup is
  additionally scoped to `alarm.UserId`, which costs nothing and makes the leak unreachable even from a bad
  row.
- **`payload.Tag` is not decoration.** The still-open tab shows its own notification under the same tag and
  the push replaces it in place instead of stacking a second one. It is derived from the boundary instant so
  the client rebuilds it with no round trip. Never set `renotify` — the replacement has to be silent.
- **Cancel is a no-op on an unknown session, never a 404.** A partially-registered set is the normal outcome
  of a tab dying mid-flight. The residual failure — a tab killed between Stop and the cancel landing, firing
  an alarm for a session already over — is **accepted**, not mitigated; suppressing it would need the session
  mirrored into IndexedDB for the service worker to read, a second source of truth for a rare, mild,
  self-explaining notification.

`NotificationType.TimerBoundary` and `TimerBoundaryPayload` live in `Sydowwe.Framework.Contracts` (the
renderer that reads them is in the Notification module, which cannot see a slice), and the kind is listed in
`QuietHoursExemptTypes`.

## Dependency seams

- **References:** `AdhdTimeOrganizer.Core`, `Sydowwe.Framework`, `Sydowwe.Framework.Contracts`.
  **Nothing else** — in particular *not* `AdhdTimeOrganizer.TodoLists` and not the host.
- **Referenced by:** `AdhdTimeOrganizer` (the host) and the integration test project.

### ⚠ The membership seam — no consumer today, still not to undo

The deleted `gird` grid filtered on to-do / routine membership (`IsFromTodoList`, `TaskPriorityId`,
`IsFromRoutineTodoList`, `RoutineTimePeriodId`). It used to do that by querying
`dbContext.TodoListItems` / `dbContext.RoutineTodoLists` directly, which made History depend on
TodoLists *and* Routines and forced a fixed extraction order across three slices.

That was rewritten onto **`IActivityMembershipSource`** (`AdhdTimeOrganizer.Core/application/seam/`),
and then the grid itself was deleted (`CQ-1`) because no client ever called it. **So this slice
consumes the seam nowhere today** — the interface, the two sources and the guards over them are kept
for the next consumer, and the pattern below is still the one to copy:

- Core declares the interface and the key constants (`ActivityMembershipSourceKeys`).
- The owning slice implements it — `TodoListActivityMembershipSource` in
  `AdhdTimeOrganizer.TodoLists`, `RoutineTodoListActivityMembershipSource` in
  `AdhdTimeOrganizer.Routines` (it moved there with the entity; nothing on this side changed, which
  is the seam working as intended).
- The consumer resolves `IEnumerable<IActivityMembershipSource>` and matches on `Key`. History was
  that consumer until the grid went; nothing consumes it now.

Each source returns a **composable** `IQueryable<long>` of activity ids, so `ids.Contains(...)`
renders as `IN (SELECT …)` — the same plan the hand-written `EXISTS` subqueries produced. Do not
materialize it, and do not "simplify" the seam back into a direct
`dbContext.Set<TodoListItem>()` query: that re-creates exactly the coupling this project exists
without.

A source resolved by string key fails **silently** when it is missing or misregistered — no build
error, no exception, the filter just stops narrowing. `SeamWiringTests` is what catches that — the
key-coverage guards plus `TodoListMembershipSource_NarrowsToActivitiesOnAList`, which asserts on rows
rather than on registration. Keep them; they are the only thing holding the seam correct while it has
no consumer to notice.

### The ownership seam — the same trick, on the write path

`ActivityHistoryRequest` carries three ids the client chooses and the server does not:
`ActivityId`, `TodoListItemId`, `RoutineTodoListId`. **Nothing but the endpoint can check them** —
the row's `UserId` is stamped server-side, so a request naming a stranger's id inserts a perfectly
valid row *of the caller's own* that points at somebody else's, and the caller's own list reads then
throw on it (the global filter empties the `Activity` include and the projection materializes a
non-nullable `Id` off the null).

`Create`/`UpdateActivityHistoryEndpoint` check all three in `BeforeMapping` through Core's
`IReferencedIdOwnershipService`: `Activity` directly, the two item ids through
`IUserOwnedReferenceVerifier`, which TodoLists and Routines implement so this slice still names
neither. A failure is a **404 on the field** — missing and foreign must stay indistinguishable.
Archived activities pass; archival is not ownership. Guarded by
`ActivityHistoryReferencedIdOwnershipTests`.

## Gotchas

- **History takes a plain `DbContext`, never `AppDbContext`** — `ModuleServiceExtensions` aliases it.
  So `dbContext.Set<ActivityHistory>()`, never `dbContext.ActivityHistories`.
- **User scoping comes from the DbContext, not the endpoints.** `AppDbContext.OnModelCreating`
  applies a global query filter to every `IEntityWithUser`, and `ActivityHistory` is one. The base
  endpoints' `ApplyUserScoping` is a no-op virtual — do not rely on it. Keep `ActivityHistory`
  `IEntityWithUser` with its FKs and cascades intact.
- **`ActivityHistory` carries two nullable item links, and they are the source of recap accuracy.**
  `TodoListItemId` / `RoutineTodoListId` record *which task* a recording was saved from, stamped when
  the user accepts the save-to-history prompt on completing an item (a step's prompt sends the parent
  item's id). Both are navigation-free and declared **host-side** in
  `AppDbContext.ConfigureCrossSliceRelationships` — this slice can see neither TodoLists nor Routines
  — with pinned constraint names and `SetNull`, because `ActivityHistory` is the source of truth for
  recorded time and deleting a task must not delete it. Most rows are null on both: everything the
  tracking heartbeat attributes arrives keyed by activity alone.
  `ActivityHistoryRequest.UpdateEntity` deliberately does **not** write them — the edit form does not
  carry them, so assigning them would unlink the row on every ordinary edit.
- **`TodoListItemLoggedTimeSource` (`application/seam/`) must key on `TodoListItemId` only.** It
  serves TodoLists' daily recap through Core's `ITodoListItemLoggedTimeSource`. Widening it to match
  on the activity looks strictly more generous and is wrong: two to-do items may share one activity,
  and the same seconds would be credited to both.
- **A save touching `ActivityHistory` needs the three materialized views to exist**, or Postgres
  fails with 42P01. The app installs them via `SuggestionPatternViewInstaller` (embedded resources);
  the test fixture applies `sqlScripts/*.sql` in `AppDbContextFixture.OnSchemaCreatedAsync`. If
  history tests start failing with 42P01, that plumbing is what broke — not this slice.
- **There is no grid endpoint.** `/activity-history/gird` (`GetFilteredTableActivityHistoryEndpoint`)
  was deleted with `CQ-1`: it ignored the `ItemsPerPage`/`Page`/`SortBy` its own request declared
  `required`, returned the caller's entire ledger on every call — and **no client ever called it**.
  The SPA's only list read is `POST /activity-history/filter`. The doc note that used to sit here,
  "the SPA calls it that way, do not fix the typo", was simply wrong: `gird` appears nowhere in the
  frontend repo. The to-do / routine membership filters went with it and exist on no endpoint today.
- **Four registration sites, none of which break the build if missed:** FastEndpoints `o.Assemblies`
  in `Program.cs` (miss → routes 404), `ModuleAssemblies` in `ModuleServiceExtensions` (miss → the
  seeder is registered by the AppDomain sweep instead; being in *both* registers it twice),
  `ApplyHostConfigurations` in `AppDbContext` (miss → the table drops out of the model), and the
  `.sln`. All four are assembly-anchored and already cover everything added here — but a **fifth** now
  applies to the timer alarms specifically: `services.AddHostedService<TimerBoundaryAlarmRegistrar>()` in
  `Program.cs`, which nothing scans for (miss → alarms stop surviving a restart, silently).
- **A new user-scoped entity in this slice must be classified in `UserDataExportAndDeletionTests.KnownGaps`
  or exported**, and its table added to `ModuleSchemaTests`' `history` list. Both fail loudly, which is the
  point — the export one is a GDPR Art. 15 decision the test refuses to let anyone skip.

## Deeper reference

- `domain-map.md` — the two entities, their indexes, the endpoint inventory, the navigation index for
  everything that is not an endpoint, and the invariants
- `testing.md` — test strategy and the guards that must not be deleted
- `README.md` — human-facing intro and the four setup facts (schema, migrations, materialized views,
  the registrar registration)

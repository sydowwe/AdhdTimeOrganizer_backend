# Testing

Full guide: `framework/Sydowwe.Framework.Testing/docs/testing.md` (**not** the root `docs/testing.md`,
which is a foreign copy). Quick reference:

- Tests run the real portal `Program` against a Postgres container (`Testcontainers.PostgreSql`), with
  auth and a couple of singletons swapped. xunit v3 + FluentAssertions + Moq + Respawn.
- Shared infrastructure lives in `Sydowwe.Framework.Testing`: one fixture
  (`PostgresContainerFixture<TProgram, TDbContext>`), one base class (`PostgresTestBase`), one auth
  handler (`RoleTestAuthHandler`, scheme `"Test"`), one factory (`TestWebApplicationFactory<TProgram>`).
  The handler and factory are role-parametrized — do not add per-role subclasses.
- This portal closes the fixture in
  `AdhdTimeOrganizer.IntegrationTests/Infrastructure/AppDbContextFixture.cs`; tests are tagged
  `[Collection("Postgres")]`. Its `OnSchemaCreatedAsync` applies
  `AdhdTimeOrganizer/infrastructure/persistence/sqlScripts/*.sql` (the three suggestion-pattern
  materialized views), copied next to the test binaries by a `Content` item in the test csproj. They
  are hand-written SQL, not migration output, so `EnsureCreated` skips them — and without them
  `SuggestionPatternRefreshInterceptor` fails with 42P01 on any save touching `PlannerTask`,
  `ActivityHistory` or `Calendar`. Add new scripts to that folder and they are picked up. The running
  app gets the same views from `SuggestionPatternViewInstaller` (called from `Program.cs` just before
  `SeedDatabase`), which reads them as **embedded resources** and creates only the ones `to_regclass`
  says are missing — the two paths read the same three files, so a new script is installed both places.
- Test bases get HTTP clients via `CreateClient()` (Admin+User), `CreateAdminRoleClient()`,
  `CreateUserRoleClient()`, `CreateRootRoleClient()`, `CreateUnauthenticatedClient()`. For different
  test users, `CreateFactory(roles, userId)` — caller disposes. `CreateDbContext()` for
  seeding/asserting outside HTTP; override `SeedAsync(db)`.
- For each FastEndpoints base in `endpoint/base/` there is a matching abstract test base in
  `framework/Sydowwe.Framework.Testing/baseTests/` (`BaseGetByIdEndpointTests`, `BaseGridEndpointTests`,
  `BaseCreateEndpointTests`, `BaseUpdateEndpointTests`, `BaseDeleteEndpointTests`,
  `BasePatchEndpointTests`, `BaseGetAllEndpointTests`, `BaseGetSelectOptionsEndpointTests`,
  `BaseFilterEndpointTests`, `BaseSortEndpointTests`, `BaseFilterSortEndpointTests`,
  `BaseBatchDeleteEndpointTests`, `BaseToggleIsHiddenEndpointTests`). Use them — they ship the auth
  matrix + 404 paths. Add endpoint-specific scenarios (validation, business rules, IDOR) in the
  concrete subclass.
- **An opt-in module ships its own test bases in a companion package**, not in
  `Sydowwe.Framework.Testing` — folding them in would drag the module into the test project of every
  host that ships without it. Today that is
  `framework/Sydowwe.Notifications.Testing/baseTests/BaseNotificationItemStateTests`, closed here by a
  ten-line `NotificationItemStateTests`. If you write a guard for behaviour the **module** owns rather
  than behaviour this host's composition owns, put it there so the other app on the submodule inherits
  it; if it is about this host's wiring, it belongs in `AdhdTimeOrganizer.IntegrationTests` directly.
  Both kinds still *run* only from here — a module is a library with no host to boot.

## Things that only a behavioural test catches

Every cross-slice mechanism in this solution fails **silently** — no build error, no exception, just a
filter that stops narrowing or a job that never fires. Assert on rows, not on routes. Existing guards
worth copying the shape of:

- `SeamWiringTests` — the seam registry (placement, key coverage, no unhandled events).
- `HistoryRouteSmokeTests.Grid_MembershipFilter_NarrowsThroughTheSeam`.
- `ActivityTimeAutomationTests` — the event-driven completion rule.
- `TrackingRouteSmokeTests.WebExtensionActivityEntry_KeepsItsCombinedQueryFilter` — seeds two users and
  an out-of-range row rather than inspecting metadata.
- `UserScopingQueryFilterTests` — asserts the generated SQL parameterizes the user id, and that two
  users hitting one endpoint in one process each see only their own rows.
- `PlanningRouteSmokeTests.PlannerTaskToTodoListItem_ForeignKey_SurvivesTheSliceSplit`.
- `ActivityProfilesRouteSmokeTests.Core_DoesNotReferenceActivityProfiles`.
- `AccountDeletionSummaryTests` — the deletion-warning counts. Seeds a *different* row count into every
  category, so a subquery aimed at the neighbouring table lands on the wrong number instead of
  coincidentally the right one, and pins that the web-extension count deliberately reads past the
  partition-window query filter.
- `ActivityMergeTests.Merge_RepointsEveryReferencingSlice` — one merge, one referencing row per slice,
  asserted on the surviving rows in six projects rather than on the endpoint's `repointedCount`. The
  `IActivityReferenceSource` seam resolves by string key, so a slice dropped from `ModuleAssemblies`
  leaves its rows on activities the same request then deletes — and every activity FK here is
  `Cascade`, so they are destroyed, not orphaned, while the response still looks right.
- `ActivityArchivingTests` — which endpoints exclude archived activities and, just as important, which
  must not. The rule is "only pickers exclude"; both halves fail as a valid 200 with a list of the
  wrong length, so every picker is asserted not to contain a seeded archived row and every
  record-reading surface is asserted to still resolve it. It also pins that the *absent* filter means
  active-only (the settings table's default view depends on it) and that `usageCount` sorts in SQL —
  a count applied after projection would sort every row on `0` and return an arbitrary page in
  convincing order.
- `HistorySummaryCustomRangeTests` — `rangeType: "CustomRange"` is `date`…`endDate` inclusive on all four
  `summary/` endpoints. The range resolver used to fall through to `(date, date)` and a `to <= from` guard
  widened that to two days, so a month-long request was answered for two days — a well-formed 200 the
  client cannot tell from the truth. Rows are seeded just inside and just outside each edge, inside the
  hour where the user's date and UTC's disagree, so a wrong-but-plausible range still fails. The 400 cases
  — no end, an inverted one, one past `MaxCustomRangeDays` — run per route because FastEndpoints resolves
  validators by exact request type: a request type without its own concrete validator is silently
  unvalidated, which a one-route test cannot see. Both edges of the span cap are asserted, since a cap
  that rejects its own boundary looks identical to a correct one from the server side.
- `TimerBoundaryAlarmTests` — the one-shot end-of-timer alarm, end to end. Three things here fail with no
  error at all: the handler is resolved by **string key** (so the test looks it up out of
  `IEnumerable<IScheduledJobHandler>` by `Key`, exactly as the dispatcher does), the trigger lives in
  Quartz's **RAM job store** (so `TimerBoundaryAlarmRegistrar` must re-push every pending row on boot — delete
  it and every alarm registered before a deploy silently never fires), and the push document's four keys are
  read **positionally** by `sw-push.js`. That last one is asserted on the parsed JSON that reaches
  `IWebPushSender`, via a capturing fake, because nesting `tag`/`url` under `data` does not throw — it
  delivers a push titled with the app name and an empty body, which looks like it worked. Also pins that
  `tag` is derived from the boundary instant (the client rebuilds it to suppress the double alarm), that an
  untagged kind gets **no** `tag` key rather than a null one, and that `TimerBoundary` is exempt from
  quiet-hours deferral while an ordinary kind in the same window is still deferred.
- `QuietHoursUserZoneAndLateDeliveryTests` — whose clock a quiet window is read in, and whether a delivery
  admits it is late. The zone bug was invisible twice over: the window was evaluated in `Application:Timezone`
  while the client rendered the same two ints with no zone at all, and a suite whose only user is on UTC
  agrees with either reading. So the test gives **two** users the **same** minutes in **different** zones at
  one instant (UTC+12 via `Etc/GMT-12`, fixed-offset so no DST transition can drift it) and asserts exactly
  one defers — under the old single-zone reading both outcomes are identical whichever way it falls, so the
  test is unpassable by coincidence. `DeferredUntil`'s **value** is pinned separately from the defer/don't
  decision: getting the first right and the second wrong withholds the push correctly and then releases it
  twelve hours off, which no `!= null` assertion catches. The IANA normalization is pinned because
  `TimeZoneInfo.Id` is `"Central Europe Standard Time"` on the Windows box this is developed on and only the
  IANA form is a string a browser accepts — an un-normalized response passes every server-side check and
  breaks in the client, on one OS. The late marker is asserted in **both** directions every time (a threshold
  fails asymmetrically: marking everything is noise the user learns to ignore, marking nothing is the original
  bug) and on the parsed push JSON, since `originallyDueAt` lives in the nested `data` object precisely so the
  service worker keeps rendering `title`/`body` verbatim.
- `NotificationSubjectProjectionTests` — `NotificationDto.Subject`, the `{kind, id}` reference that lets a
  click reach the entity a notification is about instead of the list its type lives in (backend ask B2). The
  field is optional by design, so a resolver that stops resolving still returns a valid 200 bell list that has
  merely gone back to being a dead end — absence is therefore asserted only where absence is the answer (a
  digest is N occurrences; `DeadlineApproaching` carries a composed title and no id), presence everywhere
  else, and always in pairs. Two things beyond that fail invisibly. The subject must be read off
  `Notification.PayloadJson` and **not** the `INotificationPayloadEnricher`-enriched copy the same handler
  builds to render title/body: reading the overlay renders identically and quietly carries display names to
  the network boundary, voiding the payload PII contract — so a fake enricher rewrites an id as well as adding
  a name, and the test asks which document won. And the SignalR/bell and Web Push deliveries of one event are
  rendered by different code paths that used to disagree about click-through by construction, so the push
  document is parsed from a capturing `IWebPushSender` to pin that `subject` sits in the nested `data` object
  and that no top-level `url` appeared — the top-level `url` is a route the *producer* supplied (only
  `TimerBoundary` does), never one the notifications module invented. The kind vocabulary's wire spelling is
  pinned too: it is append-only, and a rename unlinks every already-persisted notification of that kind in
  every consuming app with no error anywhere.
- `BaseNotificationItemStateTests` (in `Sydowwe.Notifications.Testing`, closed here by
  `NotificationItemStateTests`, which supplies only a second user) — which states a notification can be in and
  which transitions a user may drive (backend ask B3). These are **module** rules, so they live with the
  module and every host inheriting it gets the guard. Three silent failures. **Unread is a pair of writes** — the flag and the stamp — and
  clearing only the flag returns the same 204 while leaving a row that reads as unread everywhere a client can
  see, carrying a `ReadAt` no reader distinguishes; both columns are asserted on the stored row. **Un-reading
  moves the row from the purge job's 90-day read window to its 365-day outer one**, because the job keys on
  `IsRead` and never on `ReadAt` — that was a decision, and it only stays one if a test says so, since a
  future "tidy up: purge by `ReadAt`" reads as equivalent and silently deletes rows the user marked
  unfinished. So three same-aged rows (read, never-read, read-then-un-read) go through the **real** handler
  out of `IEnumerable<IScheduledJobHandler>` and exactly one is collected. And the batch dismiss **ignores**
  ids that are not the caller's rather than 404-ing the batch: the gap between "ignored" and "deleted someone
  else's row" is one `Where` clause and both answer 200 with a plausible count, so the stranger's row is
  asserted to survive rather than merely to be reported as skipped. The per-request cap is asserted at **both**
  edges — a cap that rejects its own boundary is indistinguishable from a correct one otherwise — and it needs
  its own test at all because FastEndpoints resolves validators by exact request type, so a request type
  without a concrete validator is silently unvalidated and the ceiling is a comment.
- `ModuleWiringTests` — the whole composition root. Includes `IUserTimeZoneReader`, which is generic over the
  host's user type and so invisible to every marker scan: forget the host's closing registration and quiet
  hours fall back to UTC for everyone, throwing nothing and logging nothing.
- `PerUserDefaultMatcherTests` — no DB needed.
- `ActivityRoleSystemKeyTests` — the three app-referenced activity roles resolve by `SystemKey` after the
  user renames them, and stay undeletable. The lookup they replaced was by display name, so a rename
  404'd it and quick-create died in four dialogs with nothing thrown and nothing logged.

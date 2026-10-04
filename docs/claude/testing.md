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
  `SeedDatabase`), which reads them as **embedded resources**, creates the missing ones and re-creates
  any whose `COMMENT` stamp (a hash of the script) no longer matches — the two paths read the same
  three files, so a new or edited script is installed both places.
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
- `TrackingRouteSmokeTests.WebExtensionActivityEntry_KeepsBothOfItsQueryFilters` — seeds two users and
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
- `NotificationCatalogTests` — the settings screen's row set. `NotificationType` is a framework contract
  shared with another product, so the preference matrix has to be filtered to what *this* host raises
  (`NotificationCatalogOptions`, declared in `ModuleServiceExtensions`) — and every way that filtering can
  go wrong returns a perfectly valid 200: an unregistered catalog silently republishes all nineteen enum
  members, and a stale one drops a kind's row while the kind keeps dispatching. So the base class asserts
  set equality **both ways** against a second copy of the list, checks the registration actually happened,
  and pins that a write for an uncatalogued kind is refused *and leaves no row*. The rules live in
  `BaseNotificationCatalogTests` (the module's testing package); only the list is this host's.
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
- `GoogleCalendarConnectionTests.StateCookie_RoundTripsThroughCookieContainer` — the OAuth `state` cookie's
  **path** (backend ask B14). It was scoped to `/user/google-calendar` while FastEndpoints' `RoutePrefix`
  puts the reader at `/api/user/google-calendar/connect`, so no browser ever sent it and every real connect
  was the "state is missing" 400 — while the suite stayed green, because every other connect test writes the
  `Cookie` header by hand and never exercises the scope rule. This one drives an `https` client with
  `HandleCookies = true` (a `CookieContainer` applies the same prefix rule a browser does, and refuses a
  `Secure` cookie over plain http, so the base address matters), sets no header itself, and asserts a 204
  **and** that the replay is `CookieMissing` — proving the delete used the same derived path as the set.
  `AuthUrl_NotConfigured_Returns503_NotABrokenUrl` pins the other silent one: an empty client id used to
  produce a well-formed 200 whose URL Google bounces on its own error page.
- `ModuleWiringTests` — the whole composition root. Includes `IUserTimeZoneReader`, which is generic over the
  host's user type and so invisible to every marker scan: forget the host's closing registration and quiet
  hours fall back to UTC for everyone, throwing nothing and logging nothing. `IUserLocaleReader` is pinned the
  same way.
- `NotificationLocaleTests` — notification text in the recipient's locale (B15), asserted on the bytes of all
  three render points (bell, live push + mail, deferred flush). Every one of them still returns 200 and still
  delivers when the locale stops being passed; only the language is wrong, which is the state the ask was filed
  against.
- `NotificationVocabularyTests` — subclass of the module's `BaseNotificationVocabularyTests`: every kind in this
  portal's catalog is worded in every locale through the composed renderer. The portal's kinds are virtual
  members on the framework's text tables (the module carries no app's vocabulary), so forgetting one — or the
  `AddNotificationVocabulary` call — renders the generic "Nová notifikácia" with no error; this is the guard.
- `PerUserDefaultMatcherTests` — no DB needed.
- `ActivityRoleSystemKeyTests` — the three app-referenced activity roles resolve by `SystemKey` after the
  user renames them, and stay undeletable. The lookup they replaced was by display name, so a rename
  404'd it and quick-create died in four dialogs with nothing thrown and nothing logged.
- `SchedulerJobSignalsTests` — the scheduler dashboard's `isOrphaned` / `isOverdue`. Neither is reachable from
  the entity (one needs the DI handler set, the other a clock), so both are overlaid **after** the projection
  and both are non-nullable: a surface that skips the overlay does not omit the field, it answers `false` —
  a confident wrong answer, which is how the job-detail page came to report "On schedule" about a job five
  hours late that the `onlyOverdue` filter had picked out one request earlier. So the overdue flag is asserted
  on the same row the filter returns, and both margin edges are pinned (a row inside the 60s display margin
  must **not** flag, or every healthy job blinks overdue on each fire). The by-id surface is asserted
  separately from the grids because `BaseGetByIdEndpoint` has no projection hook, so its overlay lives
  somewhere else and can drift alone — and it is the one the detail page gates Trigger-now on, where a false
  offers an action whose 409 the server can already predict. Also pins the raw `runningSince` on both the grid
  and the by-id read — the trigger-now confirm warns of a likely veto off it, and a projection that drops it
  serializes `null`, which reads as "nothing running".
- `SchedulerAttentionDetailTests` — the reason string on the health lists. A missing `detail` deserializes to
  null and renders as nothing, and a *useless* one (restating the `nextRunAt` the client already draws) is
  indistinguishable from a useful one to any test that only checks the field is present. So each sentence is
  asserted on what it must actually carry: the error off the run log, the missing **handler** key (not the job
  key the row already shows), and — the load-bearing one — whether an overdue job has a run in flight.
  "Stuck" vs "merely slow" is the difference between paging someone and waiting, and the sentence is where that
  reading is made for the operator. Also pins the replay preconditions on
  the run-detail read against the 409 `ReplayJobRunEndpoint` documents.
- `TestSeedEndpointTests` + `TestSeedGateTests` + `TestSeedRouteRegistrationTests` — `POST /api/test/seed`,
  the e2e fixture reset (see [endpoints.md](endpoints.md#the-e2e-fixture-endpoint)). Split three ways because
  the failure modes are unrelated. The **gate** tests are pure decisions with no host, and the load-bearing
  one is `NoConfiguration_CanAdmitARemoteDatabase`: the database half of the gate is "is this host this
  machine", it deliberately takes no configuration, and that test fails the moment a key appears that could
  widen it. An environment-only gate would be one edited `ASPNETCORE_ENVIRONMENT` away from truncating
  production, and neither the port nor the database name could have substituted — this solution's dev and
  prod databases are the same name on the same remote host, differing only by port. The **registration**
  test pins that a closed gate leaves nothing to reach — asserted as "the same status as an undefined path",
  not as a literal 404, because `IdentityServiceExtensions` sets an authorization `FallbackPolicy` and
  ASP.NET applies that to unmatched requests too, so every unknown path here answers 401 to an anonymous
  caller. The **fixture** tests assert on rows, one per gap the fixture exists to close: an item with a
  non-zero `suggestedTime` whose activity also has history (five of them, or the header ratio line stays
  hidden), several overdue not-done items, a counter at 2 of 5, an archived activity that the archival query
  filter really hides, a planner day with tasks beside one without, and one profile of each leisure kind
  plus its memory anchor. Every one of those surfaces is fully built and simply never rendered before,
  which is precisely why a 200 from the endpoint proves nothing. Idempotency is asserted by snapshotting
  every id and value out of the database, seeding again, and comparing — not by counting rows.
  The `e2e-v2` additions are pinned the same way, and two of them carry a claim a row count cannot make.
  The tracking ledgers are asserted through the *rules of the ingest path they bypass*: minute-aligned
  window starts, an Android duration that agrees with its own two instants (the sync endpoint drops one
  that does not), one `ActivityHistory` row per contiguous mapped block rather than one per window, and
  **nothing at all** stored for the process the ignore rule matches — a fixture describing a state the
  pipeline could not produce is not a fixture. The notifications get a renderer assertion beside the row
  one, for the same reason the estimate needed a projection assertion: `NotificationTextRenderer` falls
  back to a generic body for a payload it cannot bind, so a drifted payload seeds rows that render as
  "Máte novú notifikáciu" and look like a renderer bug while every row assertion passes.
  The `e2e-v3` additions — the admin account and the reminders register — add a third shape of claim.
  `Seed_GivesTheAdminAccountTheAdminRole_AndNoOtherAccount` asserts the grant **and its absence**: the
  five `reminder-definition`/`reminder-dashboard` routes are gated on `GetAdminRole()`, no account carried
  `Admin` before, and `GET /api/user/data` returns no roles field at all — so a missing grant surfaces only
  as a 403 from a view the SPA has already rendered the chrome for. The absence half matters as much: if
  `primaryUser` ever picked up `Admin`, no spec could distinguish an admin gate that still works from one
  silently removed. The reminder rows are pinned per *dashboard number* rather than per table, because
  every number those views render is a count over a subset — upcoming strictly smaller than the total,
  a next-7-days count different from the upcoming one, and **`FailedNeedingAttention` smaller than the
  failed-row count**, which the fixture's reversed-failure pair is the only thing that can establish. And
  `Seed_KeysEveryDefinitionToARealPortalReminder` holds the seam the fixture writes by hand: the module's
  key is four strings with no FK behind them, so a definition naming a portal row that does not exist is
  the orphan `ReminderRegistrationService` logs about, and it renders in the register perfectly.
  The `e2e-v4` addition — routine period history — is pinned by a fourth shape:
  `Seed_LeavesEveryRoutinePeriodCurrent_SoTheGroupedReadDoesNotRewriteIt` runs the three lazy mutations
  the grouped read performs (`CheckGrace`, `RefreshFreezeBudget`, `TryResetCatchUp`) over the seeded periods
  and asserts each is a no-op. Without it a period whose `LastResetAt` merely *looks* plausible is rewritten
  by the first request — a completion row appended, the streak re-scored, every item un-ticked, on a 200 —
  while every row assertion passes against the state before that request. The ledger itself is asserted as
  the rules the reset job obeys (contiguous cycles, the newest closing on `LastResetAt`, the period streak
  equal to what `RecomputeStreak` walks to over the rows, `FrozenAt` present exactly on the frozen row), and
  the read-path guard drives `RoutineTimePeriodReadRefresher` from a host with **no ambient user** — every
  stock test host resolves a user id for the `IEntityWithUser` filter, so from any of them the fixture
  accounts' periods are invisible and the refresher renders nothing, which is a fact about the test host and
  not about the fixture.
- `ProfileGridSortKeyTests` + `SortKeyRejectionTests` — the sort keys the client actually sends, and what
  happens to one that resolves to nothing. The first pins the keys per grid (`activity.name` on all four
  profile grids, the anchor grid's composite `periodKey` expanding into year-then-month with one shared
  direction) and asserts on row **order**, never on a 200: a key that lands on the wrong member returns a
  valid page in the wrong order, which on a paginated grid hides rows. The second pins the general rule —
  an unresolvable key is a **400 naming the key**, never a 500 and never a silently unsorted page — across
  the three kinds of caller that do not share a base, because the rejection lives in `InvalidSortKeyMiddleware`
  and nothing else proves it is reached: a `BaseGridEndpoint` derivative, the non-grid
  `reminder-dashboard/my-upcoming`, and a tracking distinct-entry table whose blanket `catch (Exception)`
  turned the rejection back into a 500 until it learned to rethrow this one type. Each route is also given
  a **valid** key, since over-eager rejection is the same outage in the other direction — and the mapping
  grid's `activity.text` is in there because it reaches through a **conditionally** projected nested object
  (`Activity == null ? null : new …`), the shape `SortByMany` warns a provider may refuse to translate. The
  reminder cases pin the `nextOccurrence` → `nextOccurrenceAt` alias on all three reads over a definition,
  asserted on order rather than on a 200: an alias that resolved to the wrong member, or a normalizer that
  dropped the entry instead of renaming it, returns a page that looks exactly as healthy.
- `ErrorTypeContractTests` — the `errorType` field on the error body (see
  `docs/claude/endpoints.md#error-bodies--errortype-not-the-status-alone`). The type travels on
  `HttpContext.Items` and is only rendered by the `Errors.ResponseBuilder` the host installs in
  `Program.cs`; drop that one line and every endpoint still stamps its type, every status is still
  right, nothing throws and nothing logs — the field simply stops appearing, which a status-level test
  sails straight through. So these parse the **body**, and they pair the two 409s on one endpoint
  (`DeleteActivityRoleEndpoint` refuses a system role as a business `Conflict` and a role that still has
  activities as `DbForeignKeyError`) because a mechanism that cannot tell those apart is the bug the
  field exists to fix. The validation case asserts the field is **absent**, not null, on a
  FluentValidation 400.
- `SessionRevocationTests` (`Auth/`): a revoked session's access token 401s at its next request (B28).
  The check is one `OnTokenValidated` line in `IdentityServiceExtensions`; remove it and every endpoint
  still works, every revoke still returns 204, and the refresh still dies. Only the unexpired access
  token keeps authenticating, so the tests replay that token on a **raw** client after each of the seven
  revoke paths and assert the status. (Mutation-checked: without the line, every test in the class fails except the hub one, which doesn't depend on it.)
  Each test signs in a fresh account (`AuthTestBase.CreateRealUserAsync`) because revoking the shared
  fixture user's sessions reaches other tests, and each probes before the revoke so the revoke has to
  override a cached "live" verdict. `RevokedSession_StaysRejected_WhenTheCacheHasForgottenIt` evicts
  the cache entries and pins the database fallback, the one failure the design must not have (a
  revoked session passing silently). The hub test casts `Fixture.UnauthenticatedFactory` to
  `WebApplicationFactory<Program>` for its `Server`: the connection registry and the cache are per host,
  so a second factory would hold a connection no revoke in the test can reach. Two tests drive the
  client's real follow-up (the revoked devices each refresh once) and assert the revoker survives. That
  pins the split in `ValidateRefreshTokenAsync` between a deliberately revoked token (refused) and a
  rotated one replayed late (theft, revoke everything).
  `UserAccountDeletionTests.DeleteAccount_ThenReplayingTheOldSession_KillsRefreshAndTheLiveAccessToken`
  is the deletion half. It fails if `BaseDeleteUserAccountEndpoint` stops reporting the cascaded sessions,
  because the delete request itself cached the session as live.

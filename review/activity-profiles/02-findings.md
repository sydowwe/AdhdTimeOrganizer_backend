# AdhdTimeOrganizer.ActivityProfiles — 02 Findings

Point-in-time snapshot, 2026-09-01. Severity: 🔴 blocker · 🟠 important · 🟡 polish.
Every finding carries a `Doc impact:` line naming what the fixer must reconcile in the living docs.

First review of this slice; no prior `review/activity-profiles/` pass exists. The module's
`docs/summary.md`, `docs/domain-map.md` and `docs/testing.md` were read as the spec, and are
generally accurate and unusually good — the drift below is narrow and named in the `DOC-` section.

**Previously known / not re-flagged:**

- Auditing is not wired anywhere in this solution (`CLAUDE.md`). Nothing here writes an audit trail.
  The two slice-specific consequences are tracked as `AUDIT-1` / `AUDIT-2` in
  [03-risks-rollout.md](03-risks-rollout.md), not as findings.
- Custom endpoints in this slice call no `Roles(...)`. **Verified safe**: `Program.cs:573-577`
  installs a global `Endpoints.Configurator` that applies `Roles("User","Admin","Root")` to any
  endpoint whose `AllowedRoles` is empty. The three leisure endpoints call `Roles(this.GetUserRole())`
  explicitly; the other 54 inherit. Not a finding.
- The four pinned FK constraint names (`fk_activity_*_profile_activities_activity_id`,
  `fk_memory_anchor_activities_activity_id`) are load-bearing and correct as-is. Tracked as a rollout
  risk (`MIG-3`), not a finding.
- `docs/testing.md` → *Known gaps* already lists: the six entities absent from the user data export;
  `CreateMemoryAnchorValidator`'s two untested rules; the two unreachable check constraints; the
  untested `OpenMeteoWeatherProvider`; the untested `LeisureWeather:Enabled` switch. Not re-flagged —
  see [01-testing.md](01-testing.md).
- `LeisureSuggestionRecord` retention is a write-path sweep with no scheduled job, deliberately. The
  reasoning in `RecordSeenLeisureSuggestionEndpoint.cs:29-34` holds; only its `ExecuteDeleteAsync`
  mechanics are flagged (`AUDIT-1`).

---

## 5. Security & authorization

### SEC-1 🔴 ✅ FIXED 2026-09-03 · `GET /api/activity-backlog-profile` returns every user's backlog profiles

`AdhdTimeOrganizer.ActivityProfiles/application/endpoint/backlog/query/GetAllActivityBacklogProfileEndpoint.cs:8`
— the whole class is `: BaseGetAllEndpoint<ActivityBacklogProfile, ActivityBacklogProfileResponse>(dbContext)`
with an empty body.

`framework/Sydowwe.Framework/application/endpoint/base/read/BaseGetAllEndpoint.cs:53` runs
`dbContext.Set<TEntity>().AsNoTracking()` and `:74` defines `Filter` as the identity function. The
base has **no** `ApplyUserScoping` hook at all — unlike `BaseGridEndpoint:39`, which has one (and
which the three grids in this slice override, correctly).

**Why it matters.** `ActivityBacklogProfile` is a `BaseTableEntity`, **not** `IEntityWithUser`, so
`ApplyUserQueryFilters` never touches it. `AppDbContext.cs:299-306` gives the three profiles exactly
one filter — `AppQueryFilters.ActivityArchived` — and no user filter. So this route is a
`SELECT * FROM activity_profiles.activity_backlog_profile` for any authenticated user: every other
user's profile ids, activity ids, energy level, effort type, participant counts, duration,
`IsRepeatable`, and their lookup FK ids. Depending on how EF resolves the `e.Activity.Name` join
against `Activity`'s own user filter, the activity **name** comes back too or comes back null — the
row leaks either way.

This is not one of the module's declared gaps. `docs/testing.md:109-111` records the endpoint as
untested and explains its *projection* ("reports not done by design"); `docs/summary.md:60-69` lists
the hand-scoped surfaces and this route is not among them. Both read as though scoping were handled.
Nothing goes red — the endpoint answers 200 either way, which is precisely the silent-failure shape
`CLAUDE.md` warns about.

The pattern for fixing it structurally already exists **20 lines below the archived filters**:
`AppDbContext.cs:321-323` gives `RoutinePeriodCompletion` — a non-`IEntityWithUser` entity whose
owner is reachable only through a required navigation, i.e. the identical situation — its own named
user-scope filter mirrored through that navigation.

**Fix (do both).**
1. In-slice, now: override the base's hook on the endpoint —
   `protected override IQueryable<ActivityBacklogProfile> Filter(IQueryable<ActivityBacklogProfile> q) => q.Where(p => p.Activity.UserId == User.GetId());`
2. Structurally: mirror `RoutinePeriodCompletion` in `AppDbContext.OnModelCreating` for all three
   profiles —
   `.HasQueryFilter(AppQueryFilters.ActivityProfileUserScope, p => ScopeUserId == null || p.Activity.UserId == ScopeUserId)`
   — a **named** filter (the archived one is already named, so both coexist) with the
   `ScopeUserId == null ||` escape the seeders and the merge seam need. See `MIG-1`.

Then delete the "no global query filter backs this up" half of the comments on the three grids — it
would no longer be true, and a comment that overstates the danger is how the next person justifies
another unscoped read.

**Fixed 2026-09-03.** Both halves, as written above.

`GetAllActivityBacklogProfileEndpoint` overrides `Filter` through `p.Activity.UserId == User.GetId()`,
the same predicate the three grids hand-scope with in `ApplyUserScoping`.

`AppQueryFilters.ActivityProfileUserScope` is the floor under it: one named filter on all three
`Activity*Profile` entities in `AppDbContext.OnModelCreating`, mirrored through the required `Activity`
navigation. Copied from `RoutinePeriodCompletion` including the `scopingEnabled` gate, the
`ScopeUserId == null ||` escape (`MIG-1`) and reading `ScopeUserId` off the executing context rather than
a captured service. Named, so it coexists with `ActivityArchived` on those three instead of replacing it
(`MIG-2`); `NamedQueryFilterTests.ActivityProfiles_CarryBothTheMirroredUserScopeAndArchivedFilters`
asserts both are still declared.

Pinned on rows, not routes — the endpoint answered 200 either way:
`ActivityProfileGridTests.Backlog_GetAll_OnlyReturnsCallersOwnRows` for the endpoint half, and
`NamedQueryFilterTests.ProfileUserScopeFilter_HidesForeignProfiles_AndKeepsTheUnscopedEscape` for the
model half — a foreign profile hidden from a query that names no ownership predicate at all.

**Caveat on the escape's guard.** That the filter lets an *unscoped* context see every row is asserted on
the predicate, not behaviourally: `FakeLoggedUserService.IsAuthenticated` is hard-coded `true` and
`GetUserId` falls back to `TestUserId`, so no context reachable from the test host ever has a null
`ScopeUserId`. The seeder / merge-seam / export half of `MIG-1` therefore still rests on the existing
suites for those paths, which pass.

**Not in scope, still open:** `CQ-1` — this endpoint keeps its own scoping rather than moving to a base
that has the hook, because `BaseGetAllEndpoint` has no `ApplyUserScoping` at all and adding one is a
submodule change (`MIG-8`).

`Doc impact: summary.md → gotcha "The three Activity*Profile entities are not IEntityWithUser…" (add the GetAll surface, and the second filter once it exists) · domain-map.md → Endpoints table, backlog row (:97) · testing.md → guard table + Known gaps` — done.

### SEC-2 🟠 ✅ FIXED 2026-09-03 · Profile create and update accept another user's lookup ids

- `application/validator/CreateActivityBacklogProfileValidator.cs:15-44` — checks `MinParticipants`,
  `MaxParticipants`, `DurationMinutes` and the activity, and **nothing** about `LocationTypeId`,
  `WeatherDependencyId` or `ExpectedCostTierId`.
- `application/validator/UpdateActivityBacklogProfileValidator.cs:9-14` — three range rules, no DB
  work at all.
- Same shape for `CreateActivityBucketListProfileValidator.cs:15-42` /
  `UpdateActivityBucketListProfileValidator.cs:9-14` and `ExperienceTypeId`.
- The ids reach the column unfiltered through `ActivityBacklogProfileRequest.ToEntity:20-32` and
  `UpdateEntity:34-45`.

**Why it matters.** The global `IEntityWithUser` filter scopes *reads*; this is a write of a raw
`long` into an FK column whose target row genuinely exists. Three concrete consequences, all landing
on someone who did nothing:

1. **The victim can no longer delete their own lookup row.** All three backlog lookup FKs are
   `OnDelete(DeleteBehavior.Restrict)` (`ActivityBacklogProfileConfiguration.cs:26-39`). A profile
   belonging to user A pinning user B's "Expensive" tier means B's delete fails with a foreign-key
   error naming a table B cannot see — an unexplainable 400 for the rest of time.
2. **The attacker's own row breaks on the way out.** `DrawLeisureSuggestionEndpoint.cs:178` projects
   `ContextLabel = p.LocationType.Text` through a required navigation that carries the user filter,
   so the label comes back null (or the whole row drops out of the join). The grid returns a
   `locationTypeId` the caller's own lookup list cannot resolve.
3. It is a cheap existence oracle for id ranges in a table the caller has no read access to.

The house already knows this class of bug: `review/todo-lists/02-findings.md` → `SEC-1`/`SEC-2` is
the same defect on `TodoListId` / `TaskPriorityId`.

**Fix.** The create validators already open a scope and a `DbContext` for their `CustomAsync` block —
add an `AnyAsync` per lookup id through `db.Set<T>().Where(l => l.UserId == userId)` in the same
round trip (a single projection can carry all three `bool`s, as the existing one carries
`HasBacklogProfile`). The update validators do no DB work today and need the same block; that is the
reason they are separate classes from the create ones, so the shape is already there.

**Fixed 2026-09-03.** As prescribed, on all four validators.

The two **create** validators fold the ownership checks into the `Activity` round-trip they were already
making — three extra `bool`s on the backlog projection beside `HasBacklogProfile`, one on the bucket-list
one — each an `Any` over `db.Set<TLookup>()` with an explicit `l.UserId == userId`, not a reliance on the
global filter (the same choice the existing `a.UserId == userId` on the root already makes).

The two **update** validators gained the block they had no DB work for at all. They cannot copy the
create root: `UpdateEntity` deliberately does **not** rewrite `ActivityId`, so the request's `ActivityId`
is not what the row will point at and using it as the query root would be checking against the wrong
thing. They root at the caller instead — still one round-trip.

`LookupOwnership` holds only the failure half (field names + message), because EF cannot translate a
helper method inside a projection and each call site's query belongs to the round-trip it joins. The
message is deliberately identical for "does not exist" and "is not yours" — the anti-oracle rule Tracking
`SEC-2` already follows.

`ActivityProjectProfile` has no lookup FKs, so its two validators are unchanged.

Pinned on rows, not status codes, in `ActivityProfileGridTests`:
`Backlog_Create_WithAnotherUsersLookupIds_IsRejectedAndWritesNothing`,
`Backlog_Update_WithAnotherUsersLookupIds_IsRejectedAndRowUnchanged` (which also asserts the *own* ids in
the same rejected payload did not land — a rejected update writes nothing at all),
`BucketList_Create_WithAnotherUsersExperienceType_IsRejectedAndWritesNothing`,
`BucketList_Update_WithAnotherUsersExperienceType_IsRejectedAndRowUnchanged`, plus
`Backlog_Create_WithOwnLookupIds_Succeeds` and `BucketList_Update_WithOwnExperienceType_Succeeds` so the
guard cannot degrade into a blanket rejection.

**Not in scope, still open:** `SEC-3` (the `MemoryAnchor` activity repoint) is the same class of defect on
a different column and is fixed separately; `CQ-1` (the eighteen longhand endpoints) is untouched — these
are validators, and moving the endpoints to bases would not have changed a line of this fix.

`Doc impact: domain-map.md → Validators section (:129-138 — the create validators enforce more than "one profile per activity"; the update validators become DB-touching)` — done, plus a guard row in testing.md.

### SEC-3 🟠 ✅ FIXED 2026-09-04 · `PUT /api/memory-anchor/{id}` repoints the anchor onto any activity in the database

`application/dto/request/MemoryAnchorRequest.cs:18` (`e.ActivityId = ActivityId;` inside
`UpdateEntity`), reached by `UpdateMemoryAnchorEndpoint` →
`framework/…/BaseUpdateEndpoint.cs:135`. `UpdateMemoryAnchorValidator.cs:9-15` checks month, year,
rating and note length — nothing about the activity.

**Why it matters.** The row itself is loaded safely (`BaseUpdateEndpoint.LoadAsync:118` goes through
`FindAsync` on the filtered `DbSet`, and `MemoryAnchor` **is** `IEntityWithUser`), so the caller can
only edit their *own* anchor. But they can point it at **any** `activity.id`, including another
user's: the FK has no user-match constraint, so the write succeeds. That:

- bypasses both rules `CreateMemoryAnchorValidator.cs:20-55` exists to enforce — "the activity must
  have a backlog or bucket-list profile to be anchored" and "at most 2 anchors per activity per
  month" — neither of which has any DB backing (`MemoryAnchorConfiguration.cs:34` is a **non**-unique
  index, correctly);
- distinguishes a real foreign activity id (204) from a non-existent one (400 off the FK violation);
- leaves the victim's cascade owning a row they cannot see: deleting their activity silently deletes
  the attacker's anchor.

The three profile request DTOs deliberately do **not** rewrite `ActivityId` in `UpdateEntity`
(`ActivityBacklogProfileRequest.cs:34`, and the two siblings). `MemoryAnchorRequest` is the only one
that does, and it inherits `ActivityId` from Core's `ActivityIdRequest`, which is why the asymmetry is
easy to miss.

**Fix.** Preferred: drop `e.ActivityId = ActivityId;` from `MemoryAnchorRequest.UpdateEntity` — which
activity an anchor is about is identity, not an editable field, and the create path is the only place
the rules are checked. If it must stay editable, `UpdateMemoryAnchorValidator` needs the same
`CustomAsync` projection `CreateMemoryAnchorValidator` runs, including the `ExistingCount >= 2` rule
(which must then exclude the row being edited).

**Fixed 2026-09-04.** The preferred way: `e.ActivityId = ActivityId;` is gone from
`MemoryAnchorRequest.UpdateEntity`, which now matches the three profile request DTOs. The column is
written on create and never again.

The cross-user half was already closed ahead of this, by the `EnsureOwnsActivityAsync` guard
`UpdateMemoryAnchorEndpoint.BeforeMapping` gained with the `ActivityId`-ownership family (pinned by
`ActivityIdOwnershipTests.UpdateMemoryAnchor_WithForeignActivityId_IsRejected`). **The guard stays** even
though the field is now ignored: a body naming an activity that is not the caller's is refused outright
rather than silently accepted, and it answers the same 404 for a foreign id as for a non-existent one, so
scenario 2's oracle closes with it. Its doc comment was rewritten — it claimed `UpdateEntity` writes the
column.

Immutability alone does not finish the job: the two-per-month ceiling could still be crossed by editing
`anchorMonth` / `anchorYear` onto a period that is already full. That check is now
`UpdateMemoryAnchorEndpoint.AfterMapping` — **not** the validator, because the rule is about the row's
*stored* activity and a FastEndpoints validator sees neither the route id nor the loaded entity, while
`AfterMapping` has both. It counts siblings excluding the edited row, and only runs when the period
actually changed (compared against `OriginalValue`, not `IsModified` — nothing has run a `DetectChanges`
pass at that point), or a note-only edit of an anchor that is itself one of two in its month would be
refused by the ceiling it occupies.

The profile rule ("must have a backlog or bucket-list profile") is deliberately **not** mirrored onto the
update path: with `ActivityId` immutable it was already checked at create, and re-checking it would make
deleting a profile silently freeze the notes on its anchors. `MIG-6` holds — no DB constraint was
attempted.

`TEST-4` landed as `MemoryAnchorUpdateRulesTests`, asserting rows rather than status codes:
`UpdateMemoryAnchor_WithOwnOtherActivityId_DoesNotRepoint` (200, `ActivityId` unmoved, rest of the body
applied — scenario 3, in its ignored-not-refused shape),
`UpdateMemoryAnchor_WithUnknownActivityId_IsRefused` (scenario 2),
`UpdateMemoryAnchor_IntoAFullMonth_IsRefused` (scenario 4 on the year/month path, as the prompt's
parenthetical prescribes), plus `UpdateMemoryAnchor_IntoAMonthWithRoom_IsAllowed` and
`UpdateMemoryAnchor_NoteOnlyInAFullMonth_IsAllowed` so the ceiling cannot degrade into a blanket
rejection (scenario 5).

**Not in scope, still open:** the *create* half of the same two rules is still untested — `docs/testing.md`
keeps that bullet, now narrowed to create. `CQ-1` is untouched: this endpoint already is a
`BaseUpdateEndpoint`.

`Doc impact: domain-map.md → entity table, MemoryAnchor row (:20 — "max 2 per activity per month (validator, not DB)" holds only for create today) · testing.md → Known gaps (the untested-create-rules bullet gains an update half)` — done, plus a suite row in testing.md.

## 6. Code quality

### CQ-1 🟠 ✅ FIXED 2026-09-04 · Eighteen hand-written profile endpoints re-implement bases that already have the hook they need

`application/endpoint/{backlog,bucketList,project}/**` — six `Create`/`Update`/`Delete` pairs, three
`BatchDelete`, three `GetById`, three `all-options`, all written out longhand against
`Endpoint<T>` / `EndpointWithoutRequest`, each carrying its own copy of:

```csharp
var entity = await dbContext.Set<T>().Include(p => p.Activity).FirstOrDefaultAsync(p => p.Id == id, ct);
if (entity is null || entity.Activity.UserId != userId) { AddError("… not found."); await Send.ErrorsAsync(404, ct); return; }
```

Six identical copies of that block (`UpdateActivityBacklogProfileEndpoint.cs:34-43` and five
siblings), three more inside a per-id loop in the batch deletes.

`BaseUpdateEndpoint`, `BaseDeleteEndpoint` and `BaseBatchDeleteEndpoint` all expose
`protected virtual Task<bool> AuthorizeAsync(TEntity entity, …)` (`BaseUpdateEndpoint.cs:131`,
`BaseDeleteEndpoint.cs:97`, `BaseBatchDeleteEndpoint.cs:81`) for exactly this — a post-fetch IDOR
check. `CLAUDE.md` says to check the bases before writing a custom endpoint.

**Why it matters.** It is not only duplication: three concrete defects below (`CQ-2` 400-not-409,
`PERF-2` the N+1 batch delete, and `SEC-1` itself) exist *because* these are hand-rolled and each one
drifted independently. The `GetById` copies additionally load the whole `Activity` entity to read one
`long`.

**Fix.** Migrate the family to the bases with an `AuthorizeAsync` override reaching through
`Activity.UserId`. Two deliberate behaviour changes to decide first: the bases answer **403** where
these answer 404 (`BaseGetByIdEndpoint.NotFoundWhenUnauthorized` exists for that choice — the current
404 is the better one, so set it), and `BaseUpdateEndpoint.LoadAsync` uses `FindAsync`, which does not
load the `Activity` navigation, so the ownership check needs its own scalar read. Worth doing as one
change with `ActivityProfileGridTests` green throughout — it already pins every ownership path except
the batch deletes (`TEST-2`, write those first).

**Fixed.** All 18 now derive from the bases, plus `PatchActivityProjectProfileStatusEndpoint` (19), which
re-routes off `BasePatchEndpoint`'s `/{id}` onto `/{id}/status` in its `Configure`. Ownership is one
`AuthorizeAsync` override per endpoint calling `IReferencedIdOwnershipService.OwnsActivityAsync` on the
loaded row's `ActivityId` — the service rather than a hand-written join, because it lifts
`AppQueryFilters.ActivityArchived`, so an archived activity's profile stays editable. Belt and braces over
the host's `ActivityProfileUserScope` filter, which already 404s a foreign row before the hook is reached.
The three `GetById`s set `NotFoundWhenUnauthorized` to keep the 404 and moved their anchor overlay to
`PostProcessAsync`; the three `all-options` are `BaseGetSelectOptionsEndpoint` with `Filter` / `Sort` /
`Map` overrides. This closed `CQ-2` (the bases map the error type to a status code) and `PERF-2` (the batch
deletes are one read again).

Two behaviour changes, both `MIG-7`: `PUT` answers **200 with the id** instead of 204, matching
`UpdateMemoryAnchorEndpoint` and every other update in the solution —
`BucketList_Update_WithOwnExperienceType_Succeeds` asserts it now, and a client checking for 204 has to be
adjusted. And a foreign id on a `batch-delete` would answer **403** rather than 404 in a host without the
global profile filter; in this host the count mismatch still makes it a 404. `TEST-2` landed first, as four
tests in `ActivityProfileGridTests`.

`Doc impact: domain-map.md → Endpoints section (:89-106 — the "hand-written rather than base-derived" note would apply only to the leisure trio afterwards)` — done, plus the ownership inventory in summary.md and a suite row and two guard rows in testing.md.

### CQ-2 🟠 ✅ FIXED 2026-09-04 · Every custom profile endpoint maps a concurrency conflict to 400

`UpdateActivityBacklogProfileEndpoint.cs:50-55` (and the same six-line `catch` in all 12 custom
command endpoints):

```csharp
catch (Exception ex) { var result = DbUtils.HandleException(ex, nameof(HandleAsync)); AddError(result.ErrorMessage!); await Send.ErrorsAsync(400, ct); }
```

The status code is **hard-coded 400**. Every framework base does
`Send.ErrorsAsync(EndpointHelper.ToStatusCode(result.ErrorType), ct)`
(`BaseUpdateEndpoint.cs:106`), which is what turns a `DbUpdateConcurrencyException` into a 409 and an
FK violation into its own code.

**Why it matters.** `BaseEntityConfigure()` puts a `row_version` token on all three profiles, and
`BaseUpdateEndpoint`'s class comment documents the 409 contract. These endpoints have the token and
throw the exception, then flatten it to "Bad request" — a client that would retry a 409 sees a
validation error instead and gives up. The same flattening hides FK and unique-violation codes.

**Fix.** Replace the literal `400` with `EndpointHelper.ToStatusCode(result.ErrorType)` in all 12,
or delete the endpoints in favour of the bases (`CQ-1`). `TEST-7` pins the behaviour.

**Fixed** by the second route: the 12 hand-written `catch` blocks are gone with the endpoints (`CQ-1`), so
every profile command now answers through `EndpointHelper.ToStatusCode`. `TEST-7` is still unwritten — the
409 is inherited, not pinned here.

`Doc impact: none`

### CQ-3 🟠 ✅ FIXED 2026-09-04 · Three dev seeders gate on a global row count, so only the first user is ever seeded

- `infrastructure/persistence/seeder/dev/ActivityBacklogProfileSeeder.cs:26` —
  `if (await dbContext.Set<ActivityBacklogProfile>().AnyAsync()) return;`
- `ActivityProjectProfileSeeder.cs:25` — same
- `ActivityBucketListProfileSeeder.cs:25` — same

All three implement `IPerUserDevSeeder` and take a `userId`. `MemoryAnchorSeeder.cs:24` gets it
right: `AnyAsync(ma => ma.UserId == userId)`.

**Why it matters.** A dev database with two users has profiles for whichever user was seeded first
and none for the rest — silently, since the guard returns before the `LogWarning` path. That is
exactly the shape of database in which a cross-user scoping bug (`SEC-1`) looks like it works.

**Fix.** `AnyAsync(p => p.Activity.UserId == userId)` — the entities are not `IEntityWithUser`, so
the traversal is needed here too.

**Fixed 2026-09-04.** As prescribed, on all three seeders. `MemoryAnchorSeeder` was already correct.
Pinned by `Seeding/DevProfileSeederTests` (`TEST-8`), which fails on the old guard — see the note there
about why it has to build a host with **no** ambient user: under `ActivityProfileUserScope` an ordinary
test context hides user A's rows from the guard and the bug disappears.

`Doc impact: none`

### CQ-4 🟠 ✅ FIXED 2026-09-04 · The dev profile seeders re-declare the lookup defaults, and seed weather rows with no `Code`

`ActivityBacklogProfileSeeder.cs:39-44` hard-codes `Indoor/Outdoor/Any`, `None/Sunny/Dry/Snow`,
`Free/Cheap/Moderate/Expensive`; `ActivityBucketListProfileSeeder.cs:38-39` hard-codes the five
experience types. `EnsureLookups<T>` (`:92-113`) inserts any that are missing — with
`entity.Text`/`SortOrder`/`UserId` only, since it is generic over `BaseLookupWithUser`.

Those are the same rows the four `userDefault/` seeders own (orders 20-23, which run *before* these
at 50-52), so the fallback normally does nothing. When it does fire, the `Sunny` / `Dry` / `Snow`
rows it creates carry **no `Code`** — the one column `ActivityWeatherDependencySeeder.cs:24-30`
exists to set, and whose own XML doc explains that a code-less row falling back to
`WeatherDependencyCodes.Infer` "is exactly the fragility the column exists to remove".

Secondary: `EnsureLookups` calls `SaveChangesAsync` **inside** the loop (`:108`), one round trip per
row.

**Fix.** Have the dev seeders read the lookups the default seeders wrote and log-and-skip if they are
missing (the ordering guarantees they are not), rather than re-creating them. If the fallback must
stay, make `ActivityWeatherDependency` a special case that sets `Code`, and hoist the `SaveChanges`
out of the loop.

**Fixed 2026-09-04.** The first way: `EnsureLookups<T>` is gone from both seeders, replaced by a
read-only `LoadLookups<T>(userId, texts)` that returns `null` and logs which texts were missing, on
which the caller returns before seeding. The insert path is what carried the code-less
`ActivityWeatherDependency` rows and the per-row `SaveChangesAsync`, so both went with it; the
hard-coded `SortOrder`s went too, since nothing writes them any more.
`DevProfileSeederTests.WithoutTheDefaultLookups_TheSeederWritesNothingAtAll` pins it from the missing-row
side: no profiles, and no invented lookups of any of the four kinds.

`Doc impact: none`

### CQ-5 🟠 ✅ FIXED 2026-09-04 · A forecast whose `daily` block is present but empty becomes a "dry, cold, sunless day" — a wrong opinion, not no opinion

`infrastructure/extService/weather/OpenMeteoWeatherProvider.cs:132`:
`private static double First(double[]? values) => values is { Length: > 0 } ? values[0] : 0;`
— reached from `:112-117`, after the `daily is null` check at `:108-110`.

**Why it matters.** `(0, 0, 0, 0)` runs straight into `WeatherFitRule.MatchingCodes`
(`domain/service/WeatherFitRule.cs:41-44`), where `PrecipitationMm <= 1.0` is **true** — so the day
matches `Dry` and the endpoint paints a "good for today" badge on every dry-weather activity, from a
response that told us nothing. The whole feature's contract, stated three times in the code
(`IDailyWeatherProvider.cs:6-9`, `GetLeisureWeatherFitEndpoint.cs:23-27`, `summary.md:102-108`), is
that every failure shape is "**no** weather opinion". This one is a confident wrong one. The
provider's own comment at `:131` describes the behaviour accurately and then treats it as acceptable;
it is not, because `Dry` is the default-true code.

**Fix.** Return `null` from `ForecastAsync` when any of the four arrays is missing or empty — a
partial response is a failed lookup. Do not cache that null (the existing code only caches a
materialised `DailyWeather`, so this is a one-line guard before the `new DailyWeather(...)`).
`TEST-9` pins it.

**Fixed as described.** `First` became `TryFirst(double[]?, out double)`; `ForecastAsync` returns
`null` unless all four arrays yield a value, before the `new DailyWeather(...)`, so nothing partial is
cached. Pinned by the new `Services/OpenMeteoWeatherProviderTests` (13 tests — the provider's first
coverage, which also closes the *previously known* `LeisureWeather:Enabled` gap), plus one endpoint
test in `LeisureWeatherFitTests` recording *why* it has to be the provider that refuses: a real
all-zero day genuinely is `Dry` to the rule, so nothing downstream can tell the two apart.

`Doc impact: none` (the contract is already documented correctly; the code is what disagrees)

### CQ-6 🟡 ✅ FIXED 2026-09-04 · `RecordSeen…` reads the existing records by the unowned id list

`application/endpoint/leisureSuggestion/RecordSeenLeisureSuggestionEndpoint.cs:80-82` filters on
`activityIds` — every id the caller sent, including the ones the ownership read at `:70-73` just
rejected — instead of `ownedIds`. `SaveAsync:133-135` repeats it.

Safe **only** because both queries also carry `r.UserId == userId`. It reads as though the ownership
filter above it were decorative. Use `ownedIds` (`owned.Select(t => t.ActivityId)`) in both places.

**Fixed.** Both queries read `ownedIds` now, and `SaveAsync`'s parameter is named for what it carries. No
behaviour change — `r.UserId == userId` was already doing the work; the point is that the next reader can
see the ownership check has a consumer.

`Doc impact: none`

### CQ-7 🟡 ✅ FIXED 2026-09-04 · Every `DbUpdateException` is treated as the unique-index race

`RecordSeenLeisureSuggestionEndpoint.cs:126` catches `DbUpdateException` bare and assumes the row it
tried to insert lost a race. A genuine failure (FK, check constraint, a connection fault mid-save)
detaches the pending inserts, re-reads, finds no winner, and throws the
`InvalidOperationException` at `:141-142` — a 500 whose message says "lost the race … but the winning
row is not there", which is not what happened.

Narrow the catch to the constraint that is actually raced:
`catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: "23505" })`.

**Fixed** with the solution's existing spelling of that filter — `PostgresErrorCodes.UniqueViolation`, as
in `IngestBatchSaveExtensions:42` and `ScheduledJobDispatcher:308` — so anything that is not the raced
constraint now propagates as itself instead of being re-described as a race that did not happen.

`Doc impact: none`

### CQ-8 🟡 ✅ FIXED 2026-09-04 · A shared static response instance

`GetLeisureWeatherFitEndpoint.cs:38` —
`private static readonly LeisureWeatherFitResponse NoSignal = new() { MatchingWeatherDependencyIds = [] };`
returned to every caller of the two empty paths. The record's member is `IReadOnlyList<long>` and
nothing mutates it today, so this is a smell rather than a bug — but a shared cross-request DTO
instance is one `PostProcess` hook away from being one. A `static` factory method returning a fresh
empty response costs nothing on a path that already made zero DB calls.

**Fixed:** `NoSignal` is a static factory method, so each of the two empty paths answers with its own
instance.

`Doc impact: none`

### CQ-9 🟡 ✅ FIXED 2026-09-04 · Dead clamp in the staleness signal

`domain/service/LeisureDrawRanker.cs:230` —
`Math.Min(Math.Max(StalenessFloor + days, StalenessFloor), StalenessCeiling)`. `days` is
`Math.Max(0, …)` on the line above, so the inner `Math.Max` can never bind. Harmless; it makes the
reader look for the negative-days case that the line above already removed.

**Fixed:** the inner `Math.Max` is gone — `Math.Min(StalenessFloor + days, StalenessCeiling)` — with a
comment saying why the floor needs no clamp. No behaviour change; `LeisureDrawRankerTests` is unchanged
and green.

`Doc impact: none`

### CQ-10 🟡 ✅ FIXED 2026-09-04 · `WeatherDependencyCodes.Infer` matches very short substrings

`domain/model/WeatherDependencyCodes.cs:47-54` — the needle lists include `"such"` (Slovak *suchý*),
`"nice"`, `"warm"`, `"zim"`, `"sun"`. `Contains(…, OrdinalIgnoreCase)` over free user text means an
English label like *"Anything such as a walk"* infers `Dry`, and *"Sunday only"* infers `Sunny`.

Low blast radius by design — the signal only ever ranks up and paints a badge, and the class comment
says so — but the guess is re-run on every read and shown as a badge on someone's own row. Prefer a
word-boundary match for the needles shorter than five characters, or drop `"such"`/`"zim"` and accept
"no opinion" for those labels, which is what the doc says an unrecognised row gets anyway.

**Fixed with the word-boundary rule.** `ContainsAny` now matches only where a word starts, and a needle
shorter than five characters must *be* the word — so `sun`, `any`, `dry`, `such` no longer fire from
inside *Sunday*, *Company*, *Laundry* or *such as*, while the five-plus Slovak stems (`slneč-`,
`žiadn-`, `such-é`) still reach an inflected ending. The forms that used to be reached by substring are
listed explicitly instead (`anything`, `sunny`, `sunshine`, `snowy`, `sneží`, `zima`/`zimný`,
`sucho`/`suchý`). Five new cases in `WeatherFitRuleTests` pin both halves.

`Doc impact: none`

### CQ-11 🟡 ✅ FIXED 2026-09-04 · A dev seeder pinned to a wall-clock date

`infrastructure/persistence/seeder/dev/MemoryAnchorSeeder.cs:57` — `var today = new DateTime(2026, 5, 19);`,
from which every anchor's month is counted back. A dev database seeded next year gets anchors that
are all 12+ months old, which quietly changes what the "n of m experienced" readouts and the
anchored-completion surfaces look like in manual testing. `DateTime.UtcNow.Date` with the existing
`Random(42)` keeps the run reproducible within a day, which is all the fixed seed was buying.

**Fixed 2026-09-04.** `DateTime.UtcNow.Date`, as prescribed; `Random(42)` stays.

`Doc impact: none`

### CQ-12 🟡 ✅ FIXED 2026-09-04 · A seeder still carries the entity's old name

`ActivityProjectProfileSeeder.cs:15` — `public string SeederName => "ActivityDiyProfile";`, plus
"DIY profiles" in the two log lines (`:34`, `:105`). `SeederName` is the registry key operators see
and filter on; the entity has been `ActivityProjectProfile` since the extraction. Rename it and the
log text together.

**Fixed 2026-09-04.** `SeederName` is `"ActivityProjectProfile"` and both log lines say "project
profiles". Nothing outside the class read the old key — the only other mention was the ⚠ note in
`testingPrompts/DevSeederSecondUser.md`, updated with it.

`Doc impact: none`

### CQ-13 🟡 ✅ FIXED 2026-09-04 · `ComfortZoneStep` is validated 1–10 and scored as if it were 1–5

`CreateActivityBucketListProfileValidator.cs:15` / `UpdateActivityBucketListProfileValidator.cs:11` —
`InclusiveBetween(1, 10)`. Everything downstream assumes 1–5:
`LeisureDrawRanker.Score:246-247` awards `(6 - step) * 0.5`, which goes **negative** from step 7,
and `ComfortStepAsEnergy:136-141` saturates at `High` above 3. The DTO
(`LeisureSuggestionDrawResponse.cs:52-53`) and `domain/service/LeisureCandidate.cs:29` both document
"1–5". Tighten the validator to `InclusiveBetween(1, 5)`; see `DOC-3` and `MIG-2` for the existing-row
question.

**Fixed 2026-09-04.** `InclusiveBetween(1, 5)` on both bucket-list validators, the create one carrying the
comment that says *why* the ceiling is 5 (the `(6 - step)` term and `ComfortStepAsEnergy`'s saturation) so
the next reader does not widen it back.

**No DB check constraint**, per `MIG-4` — the range stays an application rule, and rows already stored in
6–10 keep loading and updating on every other field. The clamp-the-existing-rows data pass `MIG-4`
prescribes is still owed before deploy; nothing in this change touches stored values. The dev seeder was
already inside 1–5 (steps 1-5 across its five rows), and so is every other fixture except three test
payloads.

Pinned by `ActivityProfileGridTests.BucketList_ComfortZoneStepAboveFive_IsRejectedOnBothWritePaths` — a 6
refused on create *and* update, asserted on rows (nothing written, the stored 5 unmoved), because the column
is a plain `int` and an out-of-range row throws nowhere: it just scores negatively and sinks out of every
draw. `BucketList_Update_WithOwnExperienceType_Succeeds` (now step 4) is the positive side, so the rule
cannot degrade into a blanket rejection.

Three test payloads carried a 9 or a 7. The two that expect a rejection (`BucketList_UpdateAndDelete_
ForeignProfile_AreRefusedAndRowUnchanged`, `BucketList_Update_WithAnotherUsersExperienceType_
IsRejectedAndRowUnchanged`) were moved to 3 — they would still have passed, but on the range rule instead
of the ownership rule they exist to pin.

`Doc impact: domain-map.md → nothing to change once the code agrees with it` — the entity row now states the
range and that only the validators enforce it, since that is the half a reader cannot get from the schema.

## 9. Performance hotspots

| Where | Issue | Impact | Mitigation | ID |
|---|---|---|---|---|
| ✅ fixed 2026-09-04 · `DrawLeisureSuggestionEndpoint.HandleAsync:61-107` | Up to **9 sequential round trips** per draw: 3 `Count` (`:128-131`), a cost-tier lookup (`:161-164`), 3 source reads, history (`:302-305`), last-committed-effort (`:320-333`, itself 2 queries), then the activity read | Every "I'm bored" click; the three counts alone are three full table scans of the user's profiles | Both folds done, 9 → 6: `CountPoolAsync` is three `Count` subqueries rooted on the caller's `User` row (the create validators' shape), and `ReadLastCommittedEffortAsync` carries the backlog lookup as a correlated subquery on the same row. Both write their predicates out rather than calling `BacklogOf` and friends — EF cannot translate a helper method inside a projection, so those three have to be kept in step by hand | PERF-1 |
| ✅ fixed 2026-09-04 · `BatchDeleteActivityBacklogProfileEndpoint:31-45` + the two siblings | **One query per id**, each with `Include(p => p.Activity)` materialising the whole activity | A 50-id batch is 50 round trips and 50 activity rows | See `CQ-1` — the three now derive from `BaseBatchDeleteEndpoint`, so the rows come back in one `Where(p => ids.Contains(p.Id))` read. Ownership is still per row, but as a scalar `EXISTS` rather than a fetch | PERF-2 |
| `DrawLeisureSuggestionEndpoint.ReadHistoryAsync:300-311` | Reads the caller's **whole** `LeisureSuggestionRecord` table per draw | Bounded by the 90-day sweep, so a few hundred rows worst case | Acceptable as-is; the comment at `:296-299` states the trade-off correctly. Revisit only if the sweep is ever moved to a job | PERF-3 |
| ✅ fixed 2026-09-04 · `ActivityBacklogProfileResponse.ProjectionWithAnchors:73-101` (and the bucket-list twin) | Two correlated subqueries per row — `EXISTS` plus an ordered `TOP 1` — over the anchors | One grid page = 2 × page size subqueries; small today, quadratic-ish with anchor count | Shape kept, index added: `MemoryAnchorConfiguration` gains `(UserId, ActivityId)` and migration `20260904005748_MemoryAnchorUserActivityIndex` creates it. Plain `CreateIndex`, same as `TrackingLedgerReadIndexes` | PERF-4 |
| ✅ fixed 2026-09-04 · `ActivityBacklogProfileSeeder.EnsureLookups:99-110` | `SaveChangesAsync` inside the loop | Dev only | See `CQ-4` — the method is gone, and with it the loop; the replacement `LoadLookups<T>` writes nothing | PERF-5 |

### Indexes that should exist

- ✅ **added 2026-09-04** · **`memory_anchor (user_id, activity_id)`** — every anchored-completion read filters `user_id`
  first and then correlates on `activity_id`: `GridActivityBacklogProfileEndpoint.ScopedAnchors():21`
  and its bucket-list twin, both `ProjectionWithAnchors` subqueries, both `GetById` overlays
  (`GetByIdActivityBacklogProfileEndpoint.cs:44-50`), and the `IsAnchored` filter predicates. The
  only composite that exists is `(activity_id, anchor_year, anchor_month)`
  (`MemoryAnchorConfiguration.cs:34`), whose leading column is the wrong one for all of them; the
  `user_id` FK index alone leaves the correlation to a filter step.
- Nothing else. The three lookup FKs on the backlog profile, the unique `activity_id` on each profile
  and the unique `(user_id, source, activity_id)` on `LeisureSuggestionRecord` cover every other
  predicate in the slice, and the retention sweep's `(user_id, last_suggested_at)` rides the unique
  index's leading column.

## Code/doc drift

### DOC-1 🟠 ✅ FIXED 2026-09-04 · `testing.md` says there is no unique index behind "one profile per activity" — there is

`AdhdTimeOrganizer.ActivityProfiles/docs/testing.md:66`:
> `*_SecondProfileForSameActivity_IsRejected` (×3) | One profile per activity, enforced by the create
> validators' subqueries. **There is no unique index behind it**

There is: `builder.HasIndex(p => p.ActivityId).IsUnique();` in
`ActivityBacklogProfileConfiguration.cs:24`, `ActivityBucketListProfileConfiguration.cs:20` and
`ActivityProjectProfileConfiguration.cs:20`.

**Which side is wrong: the doc.** The index is load-bearing — `IActivityProfile`'s own remarks
(`domain/model/entity/IActivityProfile.cs:8-12`) and
`ActivityProfileActivityReferenceSource.RepointUniqueProfileAsync:79-106` both build the merge
collision rule *on* it. A reader who believes the doc will conclude that repointing two profiles onto
one survivor merely produces a duplicate rather than a constraint violation, and will "simplify" the
delete branch out of the seam.

**Fix.** Correct the guard row, and add the unique index to the `domain-map.md` entity table
(`:13-15`) where the three profiles are described as 1:1 without saying what enforces it.

**Fixed 2026-09-04.** The `testing.md` guard row now says the index is the floor and the create validators'
subqueries are the 400 in front of it, and names `RepointUniqueProfileAsync` as the second consumer — the
merge branch this finding says a reader would otherwise "simplify" away. `domain-map.md` gained a sentence
under the entity table saying the same thing (under the table, not in a Notes cell, because it is true of
all three rows and the merge-collision consequence does not fit one).

`Doc impact: testing.md → guard table (:66) · domain-map.md → entity table (:13-15)` — done.

### DOC-2 🟠 ✅ FIXED 2026-09-03 · Both docs describe the backlog `GetAll` without mentioning it is unscoped

`domain-map.md:97` describes it as "The only profile family with a `GetAll`, and it uses the **plain**
`Projection`, so its rows always say 'not done'". `summary.md:60-69` lists the hand-scoped surfaces
(the three grids, both leisure endpoints) and does not include it. `testing.md:109-111` records it as
untested and explains only the projection.

Three documents describe this endpoint's quirks and none of them mentions the one that matters. This
is the doc half of `SEC-1`: fix the code first, then say in `summary.md` which surfaces are scoped by
hand **and by what** — the list is now long enough (3 grids + 3 GetById + 3 Update + 3 Delete + 3
BatchDelete + 3 all-options + 2 leisure + 1 GetAll) that "the three profile grids" is no longer a
useful summary of it.

**Fixed 2026-09-03,** alongside `SEC-1` — the code first, then the docs.

`summary.md`'s `IEntityWithUser` gotcha is rewritten around the split this finding asks for: the global
half (`AppQueryFilters.ActivityProfileUserScope`, and the warning that the slice cannot see it) and the
hand-written half, now an actual **table of all 22 surfaces and the mechanism each one uses** — grids via
`ApplyUserScoping`, the `GetAll` via `Filter`, the 13 single-row routes via their own
`entity.Activity.UserId != userId` check, the three `all-options` projections, the three create
validators, and the two leisure endpoints. "The three profile grids" is gone. The bullet says outright
that the list is the only inventory of that scoping and that a new route is unscoped until it joins it.

`domain-map.md`'s backlog row now says the `GetAll` scopes by overriding `Filter` **because
`BaseGetAllEndpoint` has no `ApplyUserScoping` hook**, and the sentence under the table names all four
scoped read surfaces plus the filter beneath them. `testing.md`'s known gap is narrowed to what is
genuinely still untested — the plain projection's "not done" answer — with the scoping half pointing at
its new guard.

`Doc impact: summary.md → the IEntityWithUser gotcha · domain-map.md → Endpoints table (:97) · testing.md → Known gaps (:109-111)` — done.

### DOC-3 🟡 ✅ FIXED 2026-09-04 · `ComfortZoneStep`: 1–5 everywhere, 1–10 in the validators

See `CQ-13`. `domain-map.md` does not state the range itself, but `LeisureCandidate.cs:29` and
`LeisureSuggestionDrawResponse.cs:52-53` both say "1–5" and the ranking arithmetic assumes it.
**The code is wrong, not the doc** — the validator is the outlier.

**Closed by `CQ-13`:** the validators now say 1–5, so nothing disagrees. `domain-map.md` picked the range up
anyway, since it was the one doc that did not state it.

`Doc impact: none once CQ-13 lands`

### DOC-4 🟡 ✅ FIXED 2026-09-04 · The endpoint count disagrees with itself

`ActivityProfilesRouteSmokeTests.cs:25,27` says "**53** endpoints" twice; `summary.md:18` and
`domain-map.md:90` say **57**. Counting the tree: 8 backlog + 7 bucket list + 8 project + 7 anchor +
24 lookup + 3 leisure = **57**. The test comment is stale; it is the one a reader trusts most, since
it sits next to the assertion.

**Fixed:** both occurrences in the test comment now say 57, matching the two docs and the tree.

`Doc impact: none` (the drift is in a test comment — fix it there)

## 🟡 Nits appendix

- ✅ FIXED 2026-09-04 · `GetByIdActivityBacklogProfileEndpoint.cs:52` and its bucket-list twin build a
  queryable over `new[] { entity }.AsQueryable()` purely to reuse the static `Projection`, then
  `with`-copy two fields onto the result. It works and the comment explains why, but a plain mapping
  expression would be shorter than the explanation. — gone with `CQ-1`: `BaseGetByIdEndpoint` runs
  `TResponse.Projection` over the real queryable, so the row is projected in SQL. The two-field `with`
  overlay survives in `PostProcessAsync` and has to: the anchors are not reachable by navigation from a
  profile, which is the whole reason `Projection` cannot fill them.
- `LeisureSuggestionTests` (685 lines) carries two Planning tests (`:478`, `:513`) — see
  [01-testing.md](01-testing.md). *Not moved: both drive `/api/planner-task` off the backlog fixture that
  lives in this file, so relocating them means duplicating `SeedBacklogAsync`/`FindCalendarAsync` into a
  Planning file — a trade the testing pass should make, not this one. The discoverability half is closed in
  place: the section comment above them says outright that they are Planning coverage and will not be found
  by file name.*
- ✅ FIXED 2026-09-04 · `ActivityWeatherDependencyDevSeeder.cs:24-26` seeds "Rainy" and "Cold", neither
  of which maps to any of the four `WeatherDependencyCodes` nor infers one, so both rows are
  permanently invisible to the weather signal. Fine as a dev fixture — worth a one-line comment saying
  it is deliberate, since the next reader will file it as a bug. *Commented at `:21-26`: the two rows
  stand in for user-invented ones and exist to exercise the "no opinion" path; `Code` stays the
  `userDefault` seeder's alone.*
- ✅ FIXED 2026-09-04 · `LeisureDrawRanker.Draw:271-295` allocates `chosenKeys` before the relaxation pass
  needs it; the first loop's `chosenKeys.Add` result is discarded. Cosmetic. *The set is now built from
  `chosen` between the two loops (`chosen.Select(c => c.Key).ToHashSet(Ordinal)`), so the capped pass no
  longer maintains a collection it never reads and the `Add` that matters — the relaxation pass's
  dedupe — is the only one left.*

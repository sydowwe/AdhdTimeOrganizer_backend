# AdhdTimeOrganizer.ActivityProfiles — Agent Summary

**Purpose:** the *optional facets* of an `Activity`. An activity in Core is a bare name + role +
category; this project holds the three profiles that describe what kind of thing it is
(`ActivityBacklogProfile`, `ActivityProjectProfile`, `ActivityBucketListProfile`), the four per-user
lookups those profiles FK into (`ActivityLocationType`, `ActivityWeatherDependency`,
`ActivityExpectedCostTier`, `ActivityExperienceType`), and `MemoryAnchor` — the "this activity was
memorable in month X" note.

**This is the sixth and last slice out of Core**, and the only one extracted from
`AdhdTimeOrganizer.Core` rather than from the host. Named for what it holds, not "Leisure": the
backlog and bucket-list profiles are leisure-ish, but `ActivityProjectProfile` is DIY (difficulty,
materials, required tools, readiness status), and the four lookups serve backlog and bucket list
alike.

## Bounded context

Owns: the nine entities and their EF configurations, 56 endpoints (22 profile, 24 lookup, 7 memory
anchor, 3 leisure), their DTOs, 11 validators, 4 filter requests, 6 slice-only enums
(`EnergyLevel`, `EffortType`, `DifficultyLevel`, `ReadinessStatus`, `LeisureSuggestionSource`,
`LeisureSuggestionOutcome`), 4 per-user default seeders and 8 dev seeders.

It also owns **the leisure picker's ranking rule** — `domain/service/LeisureDrawRanker.cs` — which is
the only real logic in the slice, and **the leisure weather signal** (`WeatherFitRule` plus the
Open-Meteo provider under `infrastructure/extService/weather/`, the one outbound HTTP call any slice
makes). Everything else here is CRUD over the facets of an `Activity`; those files decide what the app
recommends when the user says "I'm bored".

Does **not** own: `Activity`, `ActivityRole`, `ActivityCategory`, `User` — all Core's. Nor
`AppDbContext`, `Program.cs`, the migrations or the DI wiring, all host-side.

## Dependency seams

- **References:** `AdhdTimeOrganizer.Core`, `Sydowwe.Framework`, `Sydowwe.Framework.Contracts`.
  **Zero outbound slice edges**, and it needed no seam to achieve that — unlike History
  (`IActivityMembershipSource`) or Tracking (`IActivityTimeAttributionSink` +
  `ActivityTimeRecordedEvent`). Nothing outside Core ever referenced these eight entities.
- **Referenced by:** the host and `AdhdTimeOrganizer.IntegrationTests`. **No other slice references
  it, and none should** — see the note in the csproj.

## Gotchas — things that will bite you

- **The four inbound edges were deleted, not inverted.** `Activity.BacklogProfile`,
  `Activity.ProjectProfile`, `Activity.BucketListProfile` and `Activity.MemoryAnchors` all existed
  before the extraction, plus `User.MemoryAnchors`. Each was only feeding a configuration helper a
  navigation expression, so all five went and the dependent side now configures the FK with the
  parameterless `IsManyWithOneActivity()` / `IsManyWithOneUser()` / `.WithOne()`. **Do not add any of
  them back** — it needs a project reference from Core, which inverts the direction every slice
  depends on, and it compiles fine.
  `ActivityProfilesRouteSmokeTests.Core_DoesNotReferenceActivityProfiles` is the guard.

- **Four FK constraint names are pinned, and must stay pinned.** EF derives an FK's name from the
  principal-end navigation, so deleting those four navigations silently renamed
  `fk_activity_backlog_profile_activities_activity_id` → `..._activity_activity_id`, and likewise for
  the other two profiles and `memory_anchor`. That is a DROP + ADD CONSTRAINT pair per table — an
  ACCESS EXCLUSIVE lock and a full revalidation — for zero schema benefit. All four carry an explicit
  `HasConstraintName(...)`, which is what makes the `ActivityProfilesSlice` migration empty. Same
  reasoning as `PlannerTaskConfiguration`'s pinned name.

- **The three `Activity*Profile` entities are not `IEntityWithUser` and get NO global query filter.**
  They are scoped by hand, through `p.Activity.UserId == userId`, in `ApplyUserScoping` on the three
  profile grids — deliberately in `ApplyUserScoping` and not `ApplyCustomFiltering`, because the base
  only calls the latter when the request actually carries a filter. That hand-scoping is the only
  thing keeping other users' profiles out, and `ApplyUserScoping` is a no-op virtual, so deleting the
  override is silent. `ActivityProfileGridTests` pins it. The four lookups and `MemoryAnchor` *are*
  `IEntityWithUser` and are covered by the global filter. **The two leisure endpoints scope the same
  way** — `DrawLeisureSuggestionEndpoint` reaches the owner through `p.Activity.UserId` on all three
  sources, and `RecordSeenLeisureSuggestionEndpoint` checks ownership of every activity id it was given
  before writing a row for it. Both are pinned by `LeisureSuggestionTests`.

- **"Done" on the bucket list and the one-time backlog is derived from `MemoryAnchor`, not stored.** A
  bucket-list entry is complete exactly when its activity has an anchor; a backlog entry is complete when
  it has an anchor **and** is not `IsRepeatable` (a repeatable entry is never finished, however many
  anchors its activity carries). Nothing in the schema records this, so `isAnchored` / `memoryAnchorId`
  drifting from that rule costs no migration, throws nothing and still answers 200 — the B1 tests in
  `ActivityProfileGridTests` are the only thing that notices. Keep the response projection, the
  `ApplyCustomFiltering` predicate and the GetById overlay saying the same thing; they are three
  separate copies of one rule.

- **Those two fields are computed in the projection, deliberately, because they must be sortable.**
  `BaseGridEndpoint` sorts the *projected* queryable, so a field overlaid by `PostProcessItems` would sort
  on `false` for every row — the page would come back in an arbitrary order with no error. The two grids
  therefore override the framework's `Projection` hook (added for exactly this) and call
  `ProjectionWithAnchors`, which takes the caller's anchor set because the static `Projection` has no
  DbContext and no navigation to reach it: `Activity.MemoryAnchors` was deleted in the extraction and must
  stay deleted. The plain `Projection` leaves both fields at their defaults, so any *new* caller of it
  reports "not done" — `GetById*ProfileEndpoint` overlays them from a second read for that reason.

- **The leisure draw is deterministic in `(request, data, history)`, and that is a contract, not an
  implementation detail.** The seed lives in the picker's URL, so reloading the page must show the same
  cards; the jitter therefore hashes the candidate key rather than walking a sequential RNG, and equal
  scores break the tie on the key. Anything that makes the order depend on row order — dropping the
  `ThenBy`, ranking in a `HashSet`, ordering by `Id` — breaks a promise no exception will report. The
  hash pair (FNV-1a + mulberry32) is also bit-compatible with the client rule this endpoint replaced,
  deliberately.

- **The draw's memory is `LeisureSuggestionRecord`, written only by `/leisure-suggestion/seen`.** A
  *rendered* draw records nothing: recording on render would demote the very cards on screen and stop a
  seeded URL from reproducing its own draw. Rows are swept after 90 days on the next write by the same
  user, so there is no scheduled job to forget to register.

- **The weather signal answers 200 with an empty list for every kind of failure, and that is the
  contract.** No location on the user, a place that does not geocode, a provider outage, a provider that
  breaks its own never-throw promise — all of them come back as "no weather opinion". The client cannot
  tell them apart and does not try: nothing ranks up, no badge renders, **nothing is ever excluded**. So
  the failure mode to watch for is not an error, it is the badge quietly never appearing — which is why
  `LeisureWeatherFitTests` asserts on the resolved id set rather than on a 200, and why the endpoint has
  its own `catch` around the provider on top of the provider's own.

- **`ActivityWeatherDependency.Code` is what the signal matches on; `Text` is the user's to rename.**
  The lookup is per-user and editable, so matching a row to a condition by its label would break on the
  first rename and never work in the second locale — that is also why the endpoint returns **ids** and
  the client only does `includes(row.id)`. The code is written by `ActivityWeatherDependencySeeder` and
  by nothing else: the CRUD DTOs do not carry it, so an update cannot clear it. A row with no code (one
  the user invented) falls back to `WeatherDependencyCodes.Infer(text)` at read time, and the guess is
  deliberately **not** stored — persisting it would turn a heuristic into a fact the user cannot correct.

- **The provider is registered by name in `Program.cs`, and must not carry a lifetime marker.** It is a
  typed `HttpClient`, which the marker scans cannot produce at all; adding `IScopedService` to "help"
  registers it twice, because this slice is in `ModuleAssemblies`. Pinned by
  `ActivityProfilesRouteSmokeTests.TheWeatherProvider_IsRegisteredExactlyOnce`.

- **The profile validators no longer traverse `Activity`.** `CreateActivityBacklogProfileValidator`
  and its two siblings, plus `CreateMemoryAnchorValidator`, enforce "one profile per activity" and
  "at most 2 anchors per activity per month". They used to read `a.BacklogProfile != null`; they now
  use a `db.Set<T>().Any(p => p.ActivityId == a.Id)` subquery inside the same projection. Still one
  round-trip, and still distinguishes "activity not found / not yours" (the whole projection comes
  back null) from the business failures.

- **Seeder `Order` interleaves with Core's, on purpose.** This slice does *not* get a contiguous band:
  the lookups hold 10–13 (before Core's `Activity` at 40) and the profiles/anchors hold 50–60 (after
  it). Read `AdhdTimeOrganizer.Core/infrastructure/persistence/seeder/SeederOrderBands.md` before
  touching any `Order` here — moving these into a band above Core would make truncation try to delete
  a lookup while a profile still referenced it.

- **Registering with the host is four places, none of which break the build.** FastEndpoints
  `o.Assemblies` in `Program.cs` (missing → all 55 routes 404);
  `ModuleServiceExtensions.ModuleAssemblies` (missing → the 12 seeders register *twice*, since the
  `AppDomain` sweep then also sees them: the dev ones truncate and reseed twice, the per-user defaults
  double-insert); `AppDbContext.ApplyHostConfigurations` (missing → all eight tables vanish from the
  model, and because nothing else FKs into them the model still builds and the next `migrations add`
  emits eight DROP TABLEs); and the solution file. `ActivityProfilesRouteSmokeTests` pins the first
  two.

## Navigation

`docs/domain-map.md` is the index: what lives where, and which invariants are load-bearing.

# AdhdTimeOrganizer.ActivityProfiles — Domain Map

Navigation index. Read `summary.md` first; open only what you need from here.

## Entities — `domain/model/entity/` (one flat namespace)

All nine share `AdhdTimeOrganizer.ActivityProfiles.domain.model.entity`. The folder split Core used
(`activity/profile`, `activity/lookup`, `activity/memoryAnchor`) collapsed on the way over — it added
three namespaces and bought nothing once they were alone in a project.

| Entity | Base | Owner | Notes |
|---|---|---|---|
| `ActivityBacklogProfile` | `BaseTableEntity` | `Activity` (1:1) | Energy/effort, participants, cost tier, duration, repeatable. FKs three lookups `Restrict`. |
| `ActivityProjectProfile` | `BaseTableEntity` | `Activity` (1:1) | Difficulty, project area, estimated hours, messy, `jsonb` materials + tools, readiness. No lookup FKs. Nullable `ShoppingTodoListItemId` (FO13) — the handoff's to-do item, FK declared host-side, `SetNull`. |
| `ActivityBucketListProfile` | `BaseTableEntity` | `Activity` (1:1) | Experience type, comfort-zone step (**1–5**, enforced by the two validators only — no check constraint), travel, financial goal, inspiration source. |
| `ActivityLocationType` | `BaseLookupWithUser` | user | Per-user lookup. Backlog only. |
| `ActivityWeatherDependency` | `BaseLookupWithUser` | user | Per-user lookup. Backlog only. **The one lookup with a second column**: nullable `Code` (`WeatherDependencyCodes`), written by the seeder and never by the CRUD endpoints, which is what ties a renameable row to a weather condition. |
| `ActivityExpectedCostTier` | `BaseLookupWithUser` | user | Per-user lookup. Backlog only. |
| `ActivityExperienceType` | `BaseLookupWithUser` | user | Per-user lookup. Bucket list only. |
| `MemoryAnchor` | `BaseEntityWithActivity` | user + `Activity` | Month/year/rating/note. Two check constraints, max 2 per activity per month (application code, not DB — `CreateMemoryAnchorValidator` on create, `UpdateMemoryAnchorEndpoint.AfterMapping` on a month/year edit). `ActivityId` is set on create and never rewritten: `MemoryAnchorRequest.UpdateEntity` leaves it alone. |
| `LeisureSuggestionRecord` | `BaseEntityWithActivity` | user + `Activity` | The leisure draw's memory: source + last-shown instant + outcome, unique on `(user_id, source, activity_id)`. Both FKs cascade, so a deleted activity takes its history with it. Swept after 90 days. |

⚠ The three profiles are **not** `IEntityWithUser`; the lookups and `MemoryAnchor` are. That single
line decides which reads are scoped for you and which are not — see `summary.md`.

The three 1:1s are each a **unique index on `ActivityId`** in the configuration, not a validator rule:
the create validators' "already has a profile" subquery is the friendly 400 in front of it, and
`ActivityProfileActivityReferenceSource.RepointUniqueProfileAsync` deletes rather than repoints a
colliding profile precisely because the index would otherwise refuse the merge.

## Enums — `domain/model/enum/`

`EnergyLevel`, `EffortType` (backlog), `DifficultyLevel`, `ReadinessStatus` (project),
`LeisureSuggestionSource` + `LeisureSuggestionOutcome` (the draw record). All six are referenced
**only** by this slice's entities, which is why the first four moved out of Core's shared enum
namespace. Persisted as strings via `EnumColumn()`.

⚠ Their **wire** spellings are not their member names. The leisure contract is camelCase throughout
(`"low"`, `"readyToStart"`, `"bucketList"`), and the host's bare `JsonStringEnumConverter` would emit
`"Low"` — so `application/dto/LeisureSuggestionTokens.cs` and `domain/model/LeisureSuggestionKey.cs`
map them explicitly and the leisure DTOs carry strings. That also means renaming one of these members
is not an API break, and a new member cannot reach the wire unspelled: the mappers throw.

## The rule — `domain/service/`

- `LeisureDrawRanker` — hard constraints, soft signals, seeded jitter, source caps. Pure static; no EF,
  no clock of its own. **Read its class comment before changing a weight** — each one is a product
  decision, written down where the diff will show it.
- `LeisureCandidate` — the facts the rule reads, flattened out of whichever profile the row came from.
- `LeisureDrawConstraints` / `LeisureRankingContext` — what the user has available, and the history +
  reference instant + seed the ranking reads.

`LeisureDrawRankerTests` (no database) is the guard on all of it.

The weather signal is a second, much smaller pure rule in the same folder:

- `WeatherFitRule` — which of the four `WeatherDependencyCodes` a day fits. Thresholds are deliberately
  generous: the signal only ever ranks **up** and paints a badge, so a false negative costs the feature
  while a false positive costs one odd card.
- `DailyWeather` — the four numbers the rule reads, and the whole of what a provider must supply.
- `IDailyWeatherProvider` — place name → today's numbers, **or null; never an exception**. The
  implementation lives in `infrastructure/extService/weather/` (Open-Meteo, no API key, two cached calls).

`WeatherFitRuleTests` (no database, no provider) guards the rule and the label-inference fallback.

## Outbound HTTP — `infrastructure/extService/weather/`

The only outbound call any slice in this solution makes. `OpenMeteoWeatherProvider` geocodes the user's
free-text `User.WeatherLocation` and reads one day's forecast, caching both in `IMemoryCache` (the
forecast entry capped at the location's next local midnight). Registered **by name** in `Program.cs` —
`services.AddHttpClient<IDailyWeatherProvider, OpenMeteoWeatherProvider>(…)` with a 5s timeout — because
a typed `HttpClient` cannot come from a marker scan, and a lifetime marker on it would double-register
it (this slice is in `ModuleAssemblies`). `ActivityProfilesRouteSmokeTests.TheWeatherProvider_IsRegisteredExactlyOnce`
is the guard. Settings: the `LeisureWeather` section (`LeisureWeatherOptions`), including an `Enabled`
switch that turns the outbound calls off without unregistering anything.

## Configurations — `infrastructure/persistence/configuration/`

Nine, one per entity, all in one namespace. The three profile configurations and
`MemoryAnchorConfiguration` are the load-bearing ones:

- each declares its `Activity` FK from the dependent side with a **parameterless** `.WithOne()` /
  `IsManyWithOneActivity()`, because the inverse navigations on `Activity` were deleted;
- each **pins its FK constraint name** — the four `HasConstraintName(...)` calls are what keep the
  `ActivityProfilesSlice` migration empty. Removing one is a silent lock-taking DDL migration.
- `MemoryAnchorConfiguration` also carries the two check constraints
  (`ck_memory_anchor_month`, `ck_memory_anchor_rating`) and the `(ActivityId, AnchorYear, AnchorMonth)`
  index.

Three of the four lookup configurations are one-liners over Framework's
`BaseLookupWithUserConfiguration<User, T>`. `ActivityWeatherDependencyConfiguration` **composes** that base
instead of inheriting it (its `Configure` is not virtual) so it can add the `Code` column.

## Endpoints — `application/endpoint/`

59 total, in six families. None of them declare an endpoint group; every route except the three
leisure ones and the anchor-eligibility picker now comes from a base's `EntityName.Kebaberize()`
convention rather than a literal string, so renaming one of those entities moves its route.

| Folder | Count | Routes |
|---|---|---|
| `backlog/` | 8 | `/api/activity-backlog-profile*` — derived. The only profile family with a `GetAll`; it uses the **plain** `Projection`, so its rows always say "not done", and it scopes by overriding `Filter` because `BaseGetAllEndpoint` has no `ApplyUserScoping` hook |
| `bucketList/` | 7 | `/api/activity-bucket-list-profile*` — derived |
| `project/` | 9 | `/api/activity-project-profile*` — derived, except two patches that re-route off `BasePatchEndpoint`'s `/{id}` in their `Configure`: the status-only `PATCH .../{id}/status`, and `PATCH .../{id}/shopping-item` (FO13), which stores the handoff's to-do item id after checking it through the `todoListItem` ownership verifier (404 when not the caller's) |
| `memoryAnchor/` | 8 | `/api/memory-anchor/*` — derived, plus the explicit `GET /api/memory-anchor/anchor-eligible-activities` (`GetAnchorEligibleActivitiesEndpoint`): the caller's un-archived activities with a backlog or bucket-list profile, as `SelectOptionResponse`s ordered by name. Already-anchored activities stay listed — the only create-time refusal is the two-per-month ceiling. It reads `Activity` directly, so it is not on any base and scopes by hand |
| `lookup/{expectedCostTier,experienceType,locationType,weatherDependency}/` | 24 | `/api/activity-*-type/*` etc. — derived |
| `leisureSuggestion/` | 3 | `POST /api/leisure-suggestion` (the ranked draw), `POST /api/leisure-suggestion/seen` (its outcome, 204) and `GET /api/leisure-weather-fit` (today's matching weather-dependency ids) — explicit routes, hand-written rather than base-derived |

Every profile endpoint is built on a Framework base and states its ownership through the base's own
hook: the three `Grid*ProfileEndpoint`s override `ApplyUserScoping`, `GetAll…` and the three
`GetSelectOptions…` override `Filter`, and the command endpoints plus the three `GetById`s override
`AuthorizeAsync` — the write side through `IReferencedIdOwnershipService.OwnsActivityAsync`, which
lifts `AppQueryFilters.ActivityArchived` so an archived activity's profile is still editable. All of
it sits over the host's `AppQueryFilters.ActivityProfileUserScope` global filter, which this slice
cannot see and deliberately does not depend on. The three `GetById`s set
`NotFoundWhenUnauthorized`, so a foreign id is a 404 rather than a 403 that would confirm it exists.
The two leisure endpoints scope by hand and are not built on any CRUD base — the draw is a read
expressed as a POST body, and `seen` is an upsert keyed on a composite the bases cannot express.

## Event handler — `application/eventHandler/`

`ProjectShoppingDoneEventHandler` (FO13), on Core's `TodoListItemIsDoneChangedEvent`: a done transition
on the item a project links through `ShoppingTodoListItemId` moves a `NeedsShopping` project to
`ReadyToStart`. Scoped by hand on the event's `UserId` (the handler's scope has no principal, so the
host's profile filter is a no-op there). `ProjectShoppingHandoffTests` asserts on the stored status.

## DTOs — `application/dto/`

`request/` (3 profile requests + `MemoryAnchorRequest`, which implements Core's `IActivityIdRequest`),
`response/` (4, all `IProjectionResponse`), `filter/` (4 `IFilterRequest`s used by the four grids).

⚠ `ActivityBucketListProfileResponse` and `ActivityBacklogProfileResponse` each carry a **second**
projection, `ProjectionWithAnchors(query, anchors)`, which is the only one that fills `IsAnchored` /
`MemoryAnchorId`. The two grids use it through `BaseGridEndpoint.Projection`; the plain `Projection`
answers "not done" for both fields. The matching `IsAnchored` filter field is tri-state. The backlog
filter offers `IsRepeatable` and **no `IsOneTime` alias** — the inverse briefly had its own filter field,
which put one question on the request under two spellings that a caller could contradict; a client wanting
"one-time" sends `isRepeatable: false`. See `summary.md` for why the fields cannot be overlaid after the
query.
The lookups reuse Framework's generic `LookupResponse<T>` / `LookupFilter` and need no DTOs of their
own.

The leisure pair sits alongside: `request/LeisureSuggestionDrawRequest` +
`request/RecordLeisureSuggestionSeenRequest`, `response/LeisureSuggestionDrawResponse` (envelope + item)
and `response/ActivityInfoResponse` — the four fields a suggestion card renders, deliberately not Core's
`ActivityResponse` with its whole role and category objects. None of them are `IProjectionResponse`:
the draw is ranked in memory, so there is no single queryable projection to hang off.
`dto/LeisureSuggestionTokens.cs` holds the wire spellings.

## Validators — `application/validator/`

11: create + update for each of the three profiles and for `MemoryAnchor`,
`PatchActivityProjectProfileStatusValidator` (an `IsInEnum` guard on the status-only patch), plus one
per leisure endpoint (`LeisureSuggestionDrawValidator` rejects an unknown energy token rather than defaulting it,
and `RecordLeisureSuggestionSeenValidator` rejects a malformed key — a *well-formed* key naming a
deleted activity is the ordinary case and the endpoint drops it silently). The four **create**
validators are the ones that were rewritten to use `db.Set<T>()` subqueries instead of `Activity`
navigations. The lookups have no validators here — they use Core's shared
`NameTextColorIconValidator`.

**Lookup ownership is enforced here and nowhere else.** A profile stores its lookups as raw `long` FK
columns, and the FK proves only that the row exists — not that it is the caller's. So the backlog
validators check `LocationTypeId` / `WeatherDependencyId` / `ExpectedCostTierId` and the bucket-list
ones check `ExperienceTypeId`, against `UserId`, on **both** create and update: `UpdateEntity` rewrites
every one of those columns, so update is as much of a write surface for them as create is. The create
validators fold the checks into the round-trip they were already making off the caller's `Activity`;
the update validators cannot use that root (`UpdateEntity` deliberately leaves `ActivityId` alone) and
root at the caller instead. `LookupOwnership` holds the failure half so the message — the same one for
"does not exist" and for "is not yours", or the route becomes an existence oracle — cannot drift between
the two. `ActivityProjectProfile` has no lookups and needs none of this.

## Seeders — `infrastructure/persistence/seeder/`

- `userDefault/` — 4 per-user default seeders, one per lookup, all subclassing
  `BasePerUserDefaultSeeder<T>`. `Collides` keys on `(user_id, text)`. Orders 20–23.
- `dev/` — 8 per-user dev seeders: the four lookups (10–13), the three profiles (50–52),
  `MemoryAnchor` (60).

⚠ These `Order` values sit **inside Core's 010–099 band by design** and interleave with Core's own
seeders around `Activity` at 40. See `SeederOrderBands.md` in Core before changing any of them.

## Tests

All of them live in `AdhdTimeOrganizer.IntegrationTests` — this project has no test project of its
own. `ActivityProfileGridTests` is the one that matters most: it is the only thing standing between a
deleted `ApplyUserScoping` override and every user's profiles leaking.

**`testing.md` is the map** — which file covers what, the bespoke helpers and doubles, the guard table
and the known gaps.

# AdhdTimeOrganizer.ActivityProfiles — Domain Map

Navigation index. Read `summary.md` first; open only what you need from here.

## Entities — `domain/model/entity/` (one flat namespace)

All nine share `AdhdTimeOrganizer.ActivityProfiles.domain.model.entity`. The folder split Core used
(`activity/profile`, `activity/lookup`, `activity/memoryAnchor`) collapsed on the way over — it added
three namespaces and bought nothing once they were alone in a project.

| Entity | Base | Owner | Notes |
|---|---|---|---|
| `ActivityBacklogProfile` | `BaseTableEntity` | `Activity` (1:1) | Energy/effort, participants, cost tier, duration, repeatable. FKs three lookups `Restrict`. |
| `ActivityProjectProfile` | `BaseTableEntity` | `Activity` (1:1) | Difficulty, project area, estimated hours, messy, `jsonb` materials + tools, readiness. No lookup FKs. |
| `ActivityBucketListProfile` | `BaseTableEntity` | `Activity` (1:1) | Experience type, comfort-zone step, travel, financial goal, inspiration source. |
| `ActivityLocationType` | `BaseLookupWithUser` | user | Per-user lookup. Backlog only. |
| `ActivityWeatherDependency` | `BaseLookupWithUser` | user | Per-user lookup. Backlog only. **The one lookup with a second column**: nullable `Code` (`WeatherDependencyCodes`), written by the seeder and never by the CRUD endpoints, which is what ties a renameable row to a weather condition. |
| `ActivityExpectedCostTier` | `BaseLookupWithUser` | user | Per-user lookup. Backlog only. |
| `ActivityExperienceType` | `BaseLookupWithUser` | user | Per-user lookup. Bucket list only. |
| `MemoryAnchor` | `BaseEntityWithActivity` | user + `Activity` | Month/year/rating/note. Two check constraints, max 2 per activity per month (validator, not DB). |
| `LeisureSuggestionRecord` | `BaseEntityWithActivity` | user + `Activity` | The leisure draw's memory: source + last-shown instant + outcome, unique on `(user_id, source, activity_id)`. Both FKs cascade, so a deleted activity takes its history with it. Swept after 90 days. |

⚠ The three profiles are **not** `IEntityWithUser`; the lookups and `MemoryAnchor` are. That single
line decides which reads are scoped for you and which are not — see `summary.md`.

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

56 total, in six families. None of them declare an endpoint group; the lookup and anchor routes come
from `BaseGridEndpoint`'s `EntityName.Kebaberize()` convention rather than a literal string, so
renaming one of those entities moves its route.

| Folder | Count | Routes |
|---|---|---|
| `backlog/` | 7 | `/api/activity-backlog-profile*` — explicit routes |
| `bucketList/` | 7 | `/api/activity-bucket-list-profile*` — explicit routes |
| `project/` | 8 | `/api/activity-project-profile*` — explicit routes, incl. the status-only `PATCH .../{id}/status` |
| `memoryAnchor/` | 7 | `/api/memory-anchor/*` — derived |
| `lookup/{expectedCostTier,experienceType,locationType,weatherDependency}/` | 24 | `/api/activity-*-type/*` etc. — derived |
| `leisureSuggestion/` | 3 | `POST /api/leisure-suggestion` (the ranked draw), `POST /api/leisure-suggestion/seen` (its outcome, 204) and `GET /api/leisure-weather-fit` (today's matching weather-dependency ids) — explicit routes, hand-written rather than base-derived |

The three `Grid*ProfileEndpoint`s are the ones carrying the hand-written `ApplyUserScoping`; the two
leisure endpoints scope by hand for the same reason and are not built on any CRUD base — the draw is a
read expressed as a POST body, and `seen` is an upsert keyed on a composite the bases cannot express.

## DTOs — `application/dto/`

`request/` (3 profile requests + `MemoryAnchorRequest`, which implements Core's `IActivityIdRequest`),
`response/` (4, all `IProjectionResponse`), `filter/` (4 `IFilterRequest`s used by the four grids).

⚠ `ActivityBucketListProfileResponse` and `ActivityBacklogProfileResponse` each carry a **second**
projection, `ProjectionWithAnchors(query, anchors)`, which is the only one that fills `IsAnchored` /
`MemoryAnchorId`. The two grids use it through `BaseGridEndpoint.Projection`; the plain `Projection`
answers "not done" for both fields. The matching `IsAnchored` filter field is tri-state, and the backlog
filter also accepts `IsOneTime` — the inverse of `IsRepeatable`, under the name the clients use. See
`summary.md` for why the fields cannot be overlaid after the query.
The lookups reuse Framework's generic `LookupResponse<T>` / `LookupFilter` and need no DTOs of their
own.

The leisure pair sits alongside: `request/LeisureSuggestionDrawRequest` +
`request/RecordLeisureSuggestionSeenRequest`, `response/LeisureSuggestionDrawResponse` (envelope + item)
and `response/ActivityInfoResponse` — the four fields a suggestion card renders, deliberately not Core's
`ActivityResponse` with its whole role and category objects. None of them are `IProjectionResponse`:
the draw is ranked in memory, so there is no single queryable projection to hang off.
`dto/LeisureSuggestionTokens.cs` holds the wire spellings.

## Validators — `application/validator/`

10: create + update for each of the three profiles and for `MemoryAnchor`, plus one per leisure
endpoint (`LeisureSuggestionDrawValidator` rejects an unknown energy token rather than defaulting it,
and `RecordLeisureSuggestionSeenValidator` rejects a malformed key — a *well-formed* key naming a
deleted activity is the ordinary case and the endpoint drops it silently). The four **create**
validators are the ones that were rewritten to use `db.Set<T>()` subqueries instead of `Activity`
navigations. The lookups have no validators here — they use Core's shared
`NameTextColorIconValidator`.

## Seeders — `infrastructure/persistence/seeder/`

- `userDefault/` — 4 per-user default seeders, one per lookup, all subclassing
  `BasePerUserDefaultSeeder<T>`. `Collides` keys on `(user_id, text)`. Orders 20–23.
- `dev/` — 8 per-user dev seeders: the four lookups (10–13), the three profiles (50–52),
  `MemoryAnchor` (60).

⚠ These `Order` values sit **inside Core's 010–099 band by design** and interleave with Core's own
seeders around `Activity` at 40. See `SeederOrderBands.md` in Core before changing any of them.

## Tests

- `AdhdTimeOrganizer.IntegrationTests/Endpoints/ActivityProfilesRouteSmokeTests.cs` — route
  registration per family, seeder single-registration, and the Core-must-not-reference-this assertion.
- `AdhdTimeOrganizer.IntegrationTests/Services/LeisureDrawRankerTests.cs` — the ranking rule, no
  database: what is excluded rather than penalised, that a seed reproduces a draw, that the draw does
  not depend on row order, and that the source caps never cost the user a card.
- `AdhdTimeOrganizer.IntegrationTests/Endpoints/LeisureSuggestionTests.cs` — the two routes end to end:
  card shape, the two empty-state counts, ownership on both, the upsert, the 90-day sweep, the cascade,
  and that rejecting a draw actually changes the next one at the same seed.
- `AdhdTimeOrganizer.IntegrationTests/Endpoints/ActivityProfileGridTests.cs` — the user-scoping guard
  on the three profile grids. This is the one that matters most: it is the only thing standing between
  a deleted `ApplyUserScoping` override and every user's profiles leaking.

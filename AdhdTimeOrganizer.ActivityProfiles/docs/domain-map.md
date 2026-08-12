# AdhdTimeOrganizer.ActivityProfiles — Domain Map

Navigation index. Read `summary.md` first; open only what you need from here.

## Entities — `domain/model/entity/` (one flat namespace)

All eight share `AdhdTimeOrganizer.ActivityProfiles.domain.model.entity`. The folder split Core used
(`activity/profile`, `activity/lookup`, `activity/memoryAnchor`) collapsed on the way over — it added
three namespaces and bought nothing once they were alone in a project.

| Entity | Base | Owner | Notes |
|---|---|---|---|
| `ActivityBacklogProfile` | `BaseTableEntity` | `Activity` (1:1) | Energy/effort, participants, cost tier, duration, repeatable. FKs three lookups `Restrict`. |
| `ActivityProjectProfile` | `BaseTableEntity` | `Activity` (1:1) | Difficulty, project area, estimated hours, messy, `jsonb` materials + tools, readiness. No lookup FKs. |
| `ActivityBucketListProfile` | `BaseTableEntity` | `Activity` (1:1) | Experience type, comfort-zone step, travel, financial goal, inspiration source. |
| `ActivityLocationType` | `BaseLookupWithUser` | user | Per-user lookup. Backlog only. |
| `ActivityWeatherDependency` | `BaseLookupWithUser` | user | Per-user lookup. Backlog only. |
| `ActivityExpectedCostTier` | `BaseLookupWithUser` | user | Per-user lookup. Backlog only. |
| `ActivityExperienceType` | `BaseLookupWithUser` | user | Per-user lookup. Bucket list only. |
| `MemoryAnchor` | `BaseEntityWithActivity` | user + `Activity` | Month/year/rating/note. Two check constraints, max 2 per activity per month (validator, not DB). |

⚠ The three profiles are **not** `IEntityWithUser`; the lookups and `MemoryAnchor` are. That single
line decides which reads are scoped for you and which are not — see `summary.md`.

## Enums — `domain/model/enum/`

`EnergyLevel`, `EffortType` (backlog), `DifficultyLevel`, `ReadinessStatus` (project). All four are
referenced **only** by this slice's entities, which is why they moved out of Core's shared enum
namespace. Persisted as strings via `EnumColumn()`.

## Configurations — `infrastructure/persistence/configuration/`

Eight, one per entity, all in one namespace. The three profile configurations and
`MemoryAnchorConfiguration` are the load-bearing ones:

- each declares its `Activity` FK from the dependent side with a **parameterless** `.WithOne()` /
  `IsManyWithOneActivity()`, because the inverse navigations on `Activity` were deleted;
- each **pins its FK constraint name** — the four `HasConstraintName(...)` calls are what keep the
  `ActivityProfilesSlice` migration empty. Removing one is a silent lock-taking DDL migration.
- `MemoryAnchorConfiguration` also carries the two check constraints
  (`ck_memory_anchor_month`, `ck_memory_anchor_rating`) and the `(ActivityId, AnchorYear, AnchorMonth)`
  index.

The four lookup configurations are one-liners over Framework's `BaseLookupWithUserConfiguration<User,
T>`.

## Endpoints — `application/endpoint/`

52 total, in five families. None of them declare an endpoint group; the lookup and anchor routes come
from `BaseGridEndpoint`'s `EntityName.Kebaberize()` convention rather than a literal string, so
renaming one of those entities moves its route.

| Folder | Count | Routes |
|---|---|---|
| `backlog/` | 7 | `/api/activity-backlog-profile*` — explicit routes |
| `bucketList/` | 7 | `/api/activity-bucket-list-profile*` — explicit routes |
| `project/` | 7 | `/api/activity-project-profile*` — explicit routes |
| `memoryAnchor/` | 7 | `/api/memory-anchor/*` — derived |
| `lookup/{expectedCostTier,experienceType,locationType,weatherDependency}/` | 24 | `/api/activity-*-type/*` etc. — derived |

The three `Grid*ProfileEndpoint`s are the ones carrying the hand-written `ApplyUserScoping`.

## DTOs — `application/dto/`

`request/` (3 profile requests + `MemoryAnchorRequest`, which implements Core's `IActivityIdRequest`),
`response/` (4, all `IProjectionResponse`), `filter/` (4 `IFilterRequest`s used by the four grids).
The lookups reuse Framework's generic `LookupResponse<T>` / `LookupFilter` and need no DTOs of their
own.

## Validators — `application/validator/`

8: create + update for each of the three profiles and for `MemoryAnchor`. The four **create**
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
- `AdhdTimeOrganizer.IntegrationTests/Endpoints/ActivityProfileGridTests.cs` — the user-scoping guard
  on the three profile grids. This is the one that matters most: it is the only thing standing between
  a deleted `ApplyUserScoping` override and every user's profiles leaking.

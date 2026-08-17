# AdhdTimeOrganizer.Core — Domain Map

Navigation index. Open only what you need; `summary.md` is the orientation.

## Entities — `domain/model/entity/`

| Type | Path | Notes |
|---|---|---|
| `User` | `user/User.cs` | `BaseUser` + `IBaseTableEntity`. Google OAuth id + calendar refresh token (encrypted), `HasExtensionAccess`, `FirstDayOfWeek`, `WeatherLocation` (free text, nullable — the leisure weather signal's only input; personal data, never log it). Setting `Email` also sets `UserName`. |
| `BaseEntityWithUser` | `user/BaseEntityWithUser.cs` | The portal's closing type over `BaseEntityWithUser<User>`. C# can't infer `TUser` from a constraint, so every user-scoped entity names this shim. |
| `BaseLookupWithUser` | `base/BaseLookupWithUser.cs` | Same idea for `BaseLookupWithUser<User>`. |
| `BaseEntityWithIsDone` | `base/BaseEntityWithIsDone.cs` | Shared base for the done/total-count markers. |
| `Activity` | `activity/Activity.cs` | The second hub. Required `Role`, optional `Category`, the three profiles, memory anchors. `Clone()` is a `MemberwiseClone` that resets the four navigations — see CQ-17. |
| `ActivityRole` / `ActivityCategory` | `activity/` | Per-user lookups. `ActivityRole`'s key is `(user_id, name)`. |
| `BaseEntityWithActivity` | `activity/BaseEntityWithActivity.cs` | Base for anything hanging off an `Activity`. |
| 4 activity lookups | `activity/lookup/` | `ActivityLocationType`, `ActivityWeatherDependency`, `ActivityExpectedCostTier`, `ActivityExperienceType`. |
| 3 activity profiles | `activity/profile/` | `ActivityBacklogProfile`, `ActivityProjectProfile`, `ActivityBucketListProfile`. **Not `IEntityWithUser`** — no global query filter. |
| `MemoryAnchor` | `activity/memoryAnchor/` | |
| `TimerPreset` / `PomodoroTimerPreset` | `timer/` | Timers fold into Core; there is no `AdhdTimeOrganizer.Timers` project. |

`domain/model/entityInterface/` holds the two portal markers `IEntityWithIsDone` and
`IEntityWithDoneAndTotalCount`. The `IBase*Entity` family is Framework's.

`domain/model/enum/` holds all 13 shared enums. They are pure declarations with no dependencies, so
they all live here rather than being split per slice — a slice that needs one already references Core.

## Configuration — `infrastructure/persistence/configuration/`

| File | Covers |
|---|---|
| `extensions/EntityWithUserBuilderExtensions.cs` | `IsManyWithOneUser<TEntity>(nav?, deleteBehavior = Cascade)`, `IsOneWithOneUser<TEntity>(…)`. Here because they name the concrete `User`. **The navigation argument is optional** — that is what let the inverse collections go. |
| `extensions/EntityWithActivityBuilderExtensions.cs` | `IsManyWithOneActivity` / `IsOneWithOneActivity`. |
| `user/UserEntityConfiguration.cs` | `EncryptedColumnNullable` on the Google refresh token; `TimeZoneInfo` ↔ id conversion; the `IsActive` default that stops a migration deactivating every user. |
| `user/RefreshTokenConfiguration.cs` | Configures the FK from the **principal** end, so `User.RefreshTokens` is the navigation. `AppDbContext.ConfigureRefreshTokenUserFk` is a no-op because of this. |
| `activity/**`, `timer/**` | The hub entities, lookups, profiles, anchors and presets. |

Everything general (`BaseEntityConfigure`, `EnumColumn`, `PriceColumn`, `EncryptedColumn`, the
name/text/colour helpers, `IsPartitionedByRange`) comes from `Sydowwe.Framework` — Core adds nothing
to that set.

## HTTP surface — `application/endpoint/`

| Area | Count | Path |
|---|---|---|
| Activity, role, category, 4 lookups, 3 profiles, memory anchors | 78 | `activity/**` |
| Timer presets + pomodoro presets | 10 | `timer/**` |
| `BaseActivityFormSelectOptionsEndpoint<TEntity>` | 1 | `base/read/` |

The endpoint base is generic and subclassed from **three** places — `ActivityFormSelectOptionsEndpoint`
here, plus `FormSelectOptionsActivityHistoryEndpoint` and `FormSelectOptionsPlannerTaskEndpoint` in the
host. It exposes the context to subclasses as `protected readonly DbContext DbContext`; they reach it
as `DbContext.Set<T>()`.

DTOs sit under `application/dto/` (`request/activity`, `request/timer`, `request/extendable`,
`request/generic`, `response/activity`, `response/timer`, `response/extendable`, and the seven
activity-side filters). Validators sit under `application/validator/` — 17 of them, including the
two shared ones `NameTextColorIconValidator` and `ValueRequestValidator`, and `TimeDtoValidator`
(`TimeDto` itself is Framework's).

## Cross-slice events — `application/event/`

`ActivityAddedToToDoListEvent`, `ActivityAddedToRoutineToDoListEvent`,
`PlannerTaskIsDoneChangedEvent`, `TodoListItemIsDoneChangedEvent`, `RoutineTodoListIsDoneChangedEvent`.

These live in Core so the slices on either side of a completion fan-out depend on **Core** rather
than on each other — that is what keeps the graph acyclic. The **handlers** stay host-side for now;
their permanent home is deferred until Planning and Tracking have both landed.

## Seeding — `infrastructure/persistence/seeder/`

`SeederOrderBands.md` is the contract — read it first. Core owns band **010–099**.

- `SeedUserIdProvider` — implements both `ISeedUserProvider` (framework) and `ISeedUserIdProvider`
  (Contracts), so framework and module seeding code can find users without referencing `User`.
- `default/DefaultUsersSeeder` (20) — creates the root admin, assigns `PortalRoleCatalog.Root`, then
  calls `IUserDefaultsService.CreateDefaultsAsync`. Must run after framework's `UserRoleSeeder` (4).
- `userDefault/` — 7 per-user default seeders (10–31). All but `DefaultActivityRoleSeeder`'s siblings
  subclass `BasePerUserDefaultSeeder<TEntity>`; both operations key off `Collides`, never row counts.
- `dev/` — 11 per-user dev fixtures (10–60), in FK order: lookups → role → category → activity →
  profiles → anchors. **Only dev seeders truncate.**

## Invariants

1. **No reference to `AdhdTimeOrganizer`.** Enforced by the csproj having none. If you find yourself
   wanting `AppDbContext`, you want `DbContext`.
2. **Per-user scoping is the DbContext's job**, via the global filter on `IEntityWithUser` — not the
   endpoints (`ApplyUserScoping` is a no-op virtual). Core entities must stay `IEntityWithUser` and
   keep their FKs and cascades.
3. **Except the three profiles**, which have no filter and are hand-scoped on their grids. Breaking
   that is a silent cross-user read.
4. **No inverse collections from `User` / `Activity` into a feature area.** Use the parameterless
   `IsManyWithOneUser()` / `IsManyWithOneActivity()` overloads.
5. **Class names are table names.** Renaming a Core type is a migration; moving it is not.

## Known open items

- **CQ-17** — `Activity.Clone()` still `MemberwiseClone`s. It resets the three profile references and
  `MemoryAnchors`, so the blast radius is small, but it is a shallow clone by construction. Tracked in
  `review/portal/02-findings.md`; it belongs to Core, so fix it here rather than inside a slice.

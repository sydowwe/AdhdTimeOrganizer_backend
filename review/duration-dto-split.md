# Task: split the wire's time types — `DurationDto` alongside `TimeDto`

Self-contained. Read `CLAUDE.md` and `docs/claude/endpoints.md` §DTO conventions first.

## Why

The portal has one convention for time-of-day on the wire (`TimeDto`, `{hours, minutes}`) and **none**
for durations: durations put `IntTime` — the persisted domain value object, complete with EF value
converter and arithmetic operators — straight into request and response DTOs.

That asymmetry is the task. `IntTime` stays exactly where it belongs (entities, domain); a new
`DurationDto` becomes the wire type, so a persistence decision is no longer automatically a public API
change. The two DTOs serialise identically — `{hours, minutes}` — and are deliberately separate types
because they carry opposite rules:

| | `TimeDto` | `DurationDto` (new) |
|---|---|---|
| Means | a time of day | a length of time |
| Bound | 0–23 h — `ToTimeOnly()` **throws** past 23:59 | 0–48 h, a plausibility limit, not a technical one |
| Entity type | `TimeOnly` | `IntTime` (behind `IntTimeConverter`, an `int` of seconds) |

A 30-hour duration is valid; a 30 o'clock is not. One type cannot be right about both — which is the
whole reason not to collapse them, even though the JSON is the same shape.

> This was decided after considering the opposite (delete `TimeDto`, use `IntTime` for everything). The
> deciding argument was the EF coupling above. `MyIntTime` — a third, dead copy of the same idea — has
> already been deleted separately.

## Scope: exactly 5 wire properties

```
AdhdTimeOrganizer.History/application/dto/request/ActivityHistoryRequest.cs      Length   (required)
AdhdTimeOrganizer.History/application/dto/response/ActivityHistoryResponse.cs    Length   (required)
AdhdTimeOrganizer.TodoLists/application/dto/request/base/BaseCreateTodoListRequest.cs  SuggestedTime (nullable)
AdhdTimeOrganizer.TodoLists/application/dto/request/base/BaseUpdateTodoListRequest.cs  SuggestedTime (nullable)
AdhdTimeOrganizer.TodoLists/application/dto/response/BaseTodoListItemResponse.cs       SuggestedTime (nullable)
```

**Entities do not change.** `ActivityHistory.Length` and `BaseTodoListItem.SuggestedTime` stay
`IntTime`. Only the DTO edge converts — the same shape `PlannerTaskResponse` uses for `TimeOnly`.

Mapping sites that follow from those five (6 total):

```
TodoLists/application/dto/request/CreateTodoListItemRequest.cs      SuggestedTime = SuggestedTime?.ToIntTime(),
TodoLists/application/dto/request/UpdateTodoListItemRequest.cs      e.SuggestedTime = SuggestedTime?.ToIntTime();
TodoLists/application/dto/response/TodoListItemResponse.cs          projection
Routines/application/dto/request/CreateRoutineTodoListRequest.cs    SuggestedTime = SuggestedTime?.ToIntTime(),
Routines/application/dto/request/UpdateRoutineTodoListRequest.cs    e.SuggestedTime = SuggestedTime?.ToIntTime();
Routines/application/dto/response/RoutineToDoListResponse.cs        projection (also compiled for From())
History/application/dto/request/ActivityHistoryRequest.cs           ToEntity + UpdateEntity: Length.ToIntTime()
```

## The three things that will bite you

These were all hit on a first attempt. None fails at compile time.

**1. `[JsonConstructor]` is mandatory.** `DurationDto` needs two constructors — `(int hours, int minutes)`
and `(IntTime value)` — and System.Text.Json refuses to guess between them. Without the attribute on the
two-int one, **every request carrying a duration 500s on deserialization**: 24 integration tests failed
this way, all reporting `InternalServerError` with an error id and nothing else. `IntTime` carries the
same attribute for the same reason.

**2. Projections must take the whole `IntTime`, not its members.** `IntTime` reaches the database through
a value converter, so it is one `int` column — `e.SuggestedTime.Hours` has nothing to translate to in
SQL. Use the `DurationDto(IntTime)` constructor, which EF runs client-side after materialising the
column:

```csharp
SuggestedTime = e.SuggestedTime != null ? new DurationDto(e.SuggestedTime) : null,
Length = new DurationDto(e.Length),   // required, so no null arm
```

This is the opposite of `TimeDto`'s situation, where `t.StartTime.Hour` *is* translatable because
`TimeOnly` maps to a real `time` column. Don't copy that pattern here.

**3. `[JsonIgnore]` on `TotalSeconds`.** A computed convenience is not part of the contract. `IntTime`
was given the same treatment for the same reason — check it still has it before assuming.

## Validation

Add `AdhdTimeOrganizer.Core/application/validator/DurationDtoRules.cs`, mirroring `TimeDtoRules.cs`
next to it (read that file's remarks — they explain why this is a rule-builder extension and **not** a
`Validator<DurationDto>`: FastEndpoints resolves validators by the endpoint's exact request type, so one
declared over a member DTO is never instantiated and is dead code that looks like coverage).

```csharp
public const int MaxHours = 48;   // plausibility ceiling; nothing in the type or column objects to more

public static IRuleBuilderOptions<T, DurationDto> ApplyDurationRules<T>(this IRuleBuilder<T, DurationDto> rule) =>
    rule.Must(d => d is null || (d.Hours is >= 0 and <= MaxHours && d.Minutes is >= 0 and <= 59))
        .WithMessage($"{{PropertyName}} hours must be 0–{MaxHours} and minutes 0–59.");
```

Then replace the hand-rolled checks. **There are five**, four of them identical copies:

| File | Current | Note |
|---|---|---|
| `TodoLists/.../CreateTodoListItemValidator.cs` | inline `Hours is >= 0 and <= 23` | wrong bound for a duration |
| `TodoLists/.../UpdateTodoListItemValidator.cs` | same | |
| `Routines/.../CreateRoutineTodoListValidator.cs` | same | |
| `Routines/.../UpdateRoutineTodoListValidator.cs` | same | |
| `History/.../ActivityHistoryValidator.cs` | `Must(x => x.TotalSeconds >= 0)` | no upper bound; a negative `Minutes` cancelled by a positive `Hours` slips through |

Nullable properties use the established pattern (see `PatchPlannerTaskStatusValidator`):

```csharp
RuleFor(x => x.SuggestedTime!).ApplyDurationRules().When(x => x.SuggestedTime != null);
```

⚠ In `ActivityHistoryValidator`, **keep `.Cascade(CascadeMode.Stop).NotNull()` in front of the rule.**
Its existing comment explains why and it is load-bearing: `Length` is a non-nullable reference a client
can still send as JSON `null`, and without `Stop` the range rule runs on the null and 500s.

## Tests

- `TodoListItemDashboardWidgetTests` TEST-5 asserts `SuggestedTime` — the expectation becomes
  `new DurationDto(1, 30)`; the **seed stays `new IntTime(1, 30)`** (entity type), which is what makes
  it a boundary-conversion test.
- `TestSeedEndpointTests.Seed_LeavesTheEstimateVisibleThroughTheProjection` reads `.TotalSeconds` — works
  unchanged if `DurationDto` exposes it.
- Consider a `DurationDtoRangeValidationTests` alongside `TimeDtoRangeValidationTests`, or a theory
  inside it. The valuable case is the asymmetry: **25 hours must be accepted as a duration and rejected
  as a time of day.** That is the one assertion that would catch a future merge of the two types.
- Most other affected tests post anonymous JSON (`length = new { hours, minutes }`) and need no edit —
  they will simply pass or 500, which is how gotcha #1 surfaces.

## Frontend: no changes needed

`AntiProcrastinationApp-frontend` has exactly one `Time` class (`src/_common/dto/dto/Time.ts`) with
`fromJson` destructuring `{ hours, minutes }`, used for both `dueTime` and `suggestedTime`. The wire
shape does not change, so the SPA is unaffected. Do not edit it.

## Mechanics

- **`DurationDto` belongs in `framework/Sydowwe.Framework/application/dto/dto/`**, next to `TimeDto`.
  That is a **git submodule**: commit there first, then the gitlink here. An uncommitted framework edit
  is invisible to the parent's `git status` beyond `m framework`.
- Build and test only through `./scripts/test.ps1` (see CLAUDE.md). Fast loop:
  ```powershell
  ./scripts/test.ps1 -BuildOnly -Project AdhdTimeOrganizer.IntegrationTests
  ./scripts/test.ps1 -NoBuild -Class '*ActivityHistory*','*TodoListItem*','*RoutineTodoList*','*GetAllTodoListItem*','*TimeDtoRange*','*TestSeedEndpointTests'
  ```
  Then the full suite. Baseline before this task: **2649 total, 0 failed, 150 skipped.**
- Docker must be up for integration tests. The Testcontainers Postgres no longer pins a host port, so a
  local Postgres on 5432 is fine.

## Unrelated thing worth a look while you are here

The dev seeders set `SuggestedTime = new IntTime(10, 0)` / `(7, 0)` / `(9, 0)` / `(11, 0)` / `(20, 0)`
(`TodoLists/.../TodoListItemSeeder.cs`, `Routines/.../RoutineTodoListSeeder.cs`). Those are 10, 7, 9, 11
and 20 **hours**. `SuggestedTime` is a duration the SPA adds to a start time to compute an end time
(`PlannerTaskDialog.vue`), so these look like times of day entered into a duration field — probably
meant as minutes. The e2e fixture gets it right via a `FromMinutes` helper. A 48-hour cap will not reject
them, so this is not blocking. **Report it rather than silently changing seed data.**

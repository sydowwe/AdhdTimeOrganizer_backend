# TEST-2 — Core writes accept another user's foreign-key ids

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first. Finding: `SEC-2` 🟠.

> ## ✅ Done, 2026-09-01 — this prompt has been executed and `SEC-2` fixed
>
> `AdhdTimeOrganizer.IntegrationTests/Endpoints/CoreForeignKeyOwnershipTests.cs` exists and is green,
> with **no `KnownGap` traits** — the six cases below were written against the corrected behaviour and
> the behaviour was corrected in the same commit, so they assert rather than pin. Two departures from
> the prompt: case 1 drops the grid half (the create is now a 404, so there is no row to go looking
> for), and a seventh case covers `POST api/activity/{id}/clone`, which shares quick-edit's request
> and therefore its unchecked `categoryId`. Case 6 keeps its `usageCount` / `canDelete` assertions as
> the documentation of *why*. The guard itself is
> `AdhdTimeOrganizer.Core/infrastructure/persistence/extensions/CallerVisibleReferenceExtensions.cs`.

**Status today:** `TimerPresetCrudAuthMatrixTests` seeds foreign presets for six read/update/delete
scenarios; `ActivityEndpointTests` proves a cross-user `{id}` is a 404 on nine routes. All of that
proves you cannot **read** another user's row. None of it proves you cannot **point at one**, and that
is the gap. The identical defect in TodoLists is logged as its `SEC-1` —
`review/todo-lists/testingPrompts/CreateTodoListItemForeignKeyOwnership.md`.

## Why the query filter does not save this

The per-user global filter scopes **reads** of `IEntityWithUser`. These are **writes** of a raw `long`
into an FK column whose target row genuinely exists in the database, so the constraint is satisfied and
nothing rejects it. The validators check magnitude only:

| Route | Unchecked FK | Validator |
|---|---|---|
| `POST/PUT api/activity` | `roleId`, `categoryId` | `ActivityValidator.cs:18-19` — `RoleId` `NotEmpty` |
| `PUT api/activity/{id}/quick-edit` | `categoryId` | `QuickEditActivityValidator.cs:13` — `GreaterThan(0)` |
| `POST api/activity/{id}/clone` | `categoryId` | same validator |
| `POST/PUT api/timer-preset` | `activityId` | `TimerPresetValidator.cs:14-16` — `GreaterThan(0)` |
| `POST/PUT api/pomodoro-timer-preset` | `focusActivityId`, `restActivityId` | `PomodoroTimerPresetValidator.cs:30-36` |

`MoveTodoListItemEndpoint.cs:51-54` in the TodoLists slice already shows the intended shape: look the
referenced row up through the caller's own filtered set and answer **404** when it is not visible —
404 not 403, matching `MergeActivityEndpoint.cs:81-83`, so existence stays unconfirmable across
accounts.

## Create `Endpoints/CoreForeignKeyOwnershipTests.cs`

Every test needs a second user. Build it once per test with the `UserManager` snippet in the shared
context, then seed that user's rows through a `CreateDbContext()` (which is unscoped at the EF level
for writes — you set `UserId` yourself).

All six cases below are `[Trait("Status", "KnownGap")]` — none of this behaviour exists yet.

### 1. A foreign `roleId` on `POST api/activity` is rejected

```csharp
[Fact(DisplayName = "SEC-2: an activity cannot be filed under another user's role")]
[Trait("Status", "KnownGap")]
```

Seed a role owned by `otherUserId`. As the default caller, `POST api/activity` with
`{ Name, IsUnavoidable = false, RoleId = theirRoleId }`.

Correct: **404**, and no `Activity` row exists with that `RoleId` for the caller (assert with
`.IgnoreQueryFilters()`).

Today: **201**. Then add the second half of the damage — `GET api/activity/filtered-table` as the
caller and assert the new activity **is** in the page. Today it is not: the grid projects `e.Role.Name`
through a required navigation into a user-filtered `ActivityRole` set, so the row the caller just
created is invisible to them. That assertion is the one that makes this finding concrete rather than
theoretical, so write it explicitly.

### 2. A foreign `categoryId` on `POST api/activity` is rejected

Same shape, with a category owned by `otherUserId` and a role owned by the caller. Correct: 404, no
row written.

### 3. A foreign `categoryId` on `PUT api/activity/{id}/quick-edit` is rejected

Caller owns the activity; the payload's `categoryId` belongs to `otherUserId`. Correct: 404 (or 400
with a field-level error — pick one and be consistent with #1/#2), and the activity's `CategoryId`
unchanged in the database.

### 4. A foreign `activityId` on `POST api/timer-preset` is rejected

Seed an activity owned by `otherUserId`. `POST api/timer-preset` with
`{ Duration = 25, ActivityId = theirActivityId }`. Correct: 404, no preset row.

### 5. A foreign `focusActivityId`/`restActivityId` on `POST api/pomodoro-timer-preset` is rejected

Both columns, in one test with two cases (`[Theory]` over the property name is fine). Payload shape is
at `TimerPresetCrudTests.cs:191`.

### 6. The cross-user cascade — the reason this matters

```csharp
[Fact(DisplayName = "SEC-2: user B deleting their activity cannot destroy user A's preset")]
[Trait("Status", "KnownGap")]
```

The full chain, asserted end to end:

1. `otherUserId` owns activity X.
2. The default caller creates a timer preset pointing at X (today: succeeds).
3. As `otherUserId` (via `CreateFactory(["User"], otherUserId)`), `GET api/activity/{X}` — assert
   `usageCount == 0` and `canDelete == true`. **This is the broken promise**: B's reference count
   cannot see A's preset because A's preset is behind A's user filter.
4. As `otherUserId`, `DELETE api/activity/{X}` → 204.
5. In a fresh context with `.IgnoreQueryFilters()`: **A's preset row is gone.**

Once `SEC-2` is fixed, step 2 fails and the rest is unreachable — which is the point. Keep the test and
rewrite step 2's expectation to 404 in the same commit; the `usageCount`/`canDelete` assertions in
step 3 stay as the documentation of *why*.

## Doc reconciliation for whoever fixes this

`../../docs/domain-map.md` → Invariants #6 currently reads as a licence to skip these checks
("Per-user scoping is the DbContext's job … not the endpoints"). It needs "reads only — any
caller-supplied FK id is the endpoint's responsibility". Identical wording is `DOC-5` in the TodoLists
review; fix both. See `DOC-2`.

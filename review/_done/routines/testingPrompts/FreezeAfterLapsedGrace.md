# TEST-12 — Freezing while a grace window has lapsed (pins `CQ-13`, and `CQ-14` while you are there)

Read `_SHARED-CONTEXT.md` first. This is a small, high-value addition to an existing, well-built suite —
not a new area.

## Where

| | |
|---|---|
| Endpoint | `POST /api/routine-todo-list/time-period/{timePeriodId}/streak-freeze` |
| File | `application/endpoint/routineTimePeriod/command/SpendStreakFreezeRoutineTimePeriodEndpoint.cs` |
| Domain | `domain/service/RoutineStreakFreezeService.cs` (`Apply`, `RecomputeStreak`, `RefreshFreezeBudget`) |
| Existing suites | `Routines/RoutineStreakFreezeTests.cs` (HTTP) · `Services/RoutineStreakFreezeServiceTests.cs` (pure) |

Note the route: it hangs off **`/routine-todo-list/time-period/…`**, not `/routine-time-period/…`, and
that is the shipped frontend contract — `domain-map.md` flags it explicitly. Don't "correct" it.

Both suites are good and cover every `StreakFreezeRejection` branch, both grace-window cases, the lazy
refill and IDOR. Add to them; do not restructure them.

## Part 1 (`CQ-13`) — the bug

`domain-map.md`'s business rules say `CheckGrace` "must run before `TryReset` on *every* access". The
freeze endpoint never calls it — `HandleAsync` (`:48-98`) goes load period → parse `periodStart` → load
full history → `Apply` → save.

`Apply` clears `StreakGraceUntil` / `GraceNotifiedFor` **only when the frozen entry is the newest one**
(`RoutineStreakFreezeService.cs:99-105`). That is deliberate and correct: only the newest entry can own
the open window.

So consider a period whose grace window **lapsed** (`now > StreakGraceUntil`) and which no read or job
has touched since, and the user freezes an **older** miss:

1. `Apply` leaves `StreakGraceUntil` set, because the entry is not `historyAsc[^1]`.
2. `RecomputeStreak` raises `Streak` back to the trailing run (`:148-149` — it only ever raises).
3. The response returns the restored streak. The user sees the freeze work.
4. The **next** grouped read or nightly sweep calls `RoutineResetService.CheckGrace`, sees the still-set
   lapsed window, and sets `Streak = 0` (`RoutineResetService.cs:151-159`).

The user spends a budgeted resource, is shown that it worked, and loses the streak on the next page
load. The race window is narrow — the client normally loads the grouped read first, which clears the
lapsed window — but the failure is exactly the one the feature exists to prevent, and the fix is one
line.

### Fixture

Copy `RoutineStreakFreezeTests.SeedAsync` and adjust. You need, for the default user:

- a period with `StreakThreshold = 100`, `FreezeBudget = 2`, `FreezesRemaining = 2`,
  `FreezeBudgetResetsAt = UtcNow.AddMonths(1)` (so no refill fires and confuses the count),
  `Streak = 3`, and **`StreakGraceUntil = UtcNow.AddDays(-1)`** — lapsed, uncleared;
- at least three `RoutinePeriodCompletion` rows ordered by `PeriodStart`, where an **older** one is a
  miss (`CompletedCount < TotalCount`, `TotalCount > 0`) and the **newest** one is not the row you are
  going to freeze.

Remember the two unique indexes on the period (`(UserId, Text)` and `(UserId, LengthInDays)`) and
`(TimePeriodId, PeriodStart)` on the completions.

### Tests — add to `Routines/RoutineStreakFreezeTests.cs`

- `SpendFreeze_OnAnOlderMiss_WithALapsedGraceWindow_SurvivesTheNextRead` — **the pin.** `POST` the
  freeze at the older miss's `periodStart`; assert 200 and that the response's `streak` is the restored
  value. Then call `GET /api/routine-todo-list/grouped-by-time-period` (which is what the SPA does next)
  and assert the streak is *still* the restored value, and that the row in a fresh `CreateDbContext()`
  agrees. **Fails today** — the second read reports `0`. This is the assertion that matters: a test that
  stops at the freeze response passes against the bug.
- `SpendFreeze_OnAnOlderMiss_WithALapsedGraceWindow_ClearsTheLapsedWindow` — assert
  `StreakGraceUntil` is `null` in the database after the freeze. This is what the fix does and is the
  cheaper direct pin.
- `SpendFreeze_OnAnOlderMiss_WithALiveGraceWindow_LeavesItAlone` — the control, and it must stay
  green. `StreakGraceUntil = UtcNow.AddDays(+2)` is *not* lapsed, and
  `Apply_OnAnOlderMiss_LeavesTheOpenGraceWindowAlone` in the pure suite already asserts the domain half
  of this. Adding `CheckGrace` before `Apply` must not break it — that is the whole reason to write this
  control alongside the pin.

### The fix

One line in `SpendStreakFreezeRoutineTimePeriodEndpoint.HandleAsync`, before the `Apply` call:

```csharp
RoutineResetService.CheckGrace(period, now);
```

The ordering is right: `CheckGrace` zeroes `Streak`, then `RecomputeStreak` raises it back from the
history *including* the newly frozen entry. Doing it inside `Apply` instead would work but would move a
reset-domain concern into the freeze service; the endpoint is the "every access" site the rule means.

## Part 2 (`CQ-14`) — a freeze on an item-less period is free money in reverse

`IsSuccess` returns false when `TotalCount == 0` (`RoutineStreakFreezeService.cs:65-67`), so `Apply`'s
`NotAMiss` guard (`:88`) lets a freeze through on a period that elapsed with no items. But
`RecomputeStreak` already skips those rows unconditionally (`:133`) and `TryReset` leaves the streak
untouched in that case (outcome `NotEvaluated`). So the budget is charged and nothing changes.

Add to `Services/RoutineStreakFreezeServiceTests.cs`, next to the existing `Apply_On…_Refuses` family:

- `Apply_OnAPeriodWithNoItems_RefusesAsNotAMiss` — build a completion with
  `CompletedCount = 0, TotalCount = 0`, call `Apply`, assert `StreakFreezeRejection.NotAMiss` and that
  `FreezesRemaining` did **not** move and `entry.IsFrozen` is still false. **Fails today** (returns
  `None` and charges the budget).

The existing `RecomputeStreak_PeriodWithNoItems_CarriesTheRun` is the companion that explains why the
freeze is pointless; reference it in a comment so the two read as a pair.

The endpoint-level equivalent is optional — the domain test is the real guard, and
`StreakFreezeRejection`'s own summary already says the rules live there because they are "not knowable
from the request body".

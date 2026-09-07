# TEST-1 — `ComputeNextReset` catch-up loop (pins `CQ-1`)

Read `_SHARED-CONTEXT.md` first.

**This is the highest-value test in the backlog.** It pins a total, silent failure that ships to every
user by default, and the reason it was never caught is that the two existing tests for these branches
are shaped so as to be structurally incapable of reaching the bug.

## The bug being pinned

`AdhdTimeOrganizer.Routines/domain/service/RoutineResetService.cs:36-76`. Both calendar-aligned
branches (`LengthInDays == 30` and `LengthInDays == 365`, with `ResetAnchorDay != 0`) end in:

```csharp
var candidate = MonthlyCandidate(year, month, targetDay);
while (candidate < now) { /* advance a month/year */ candidate = MonthlyCandidate(...); }
return candidate;
```

so the returned instant is always `>= now`. `MonthlyCandidate` always returns **midnight UTC**. Every
caller then gates on `if (now < nextReset) return null/false;`. A reset can therefore only fire if
`now` is exactly midnight to the tick — and the nightly job runs at 02:00. **These periods never
reset.** `RoutineTimePeriodSeeder` gives every user a Monthly (30d, anchor 1) and a Yearly (365d,
anchor 1), so this is universal.

## Why the existing tests miss it

`AdhdTimeOrganizer.IntegrationTests/Services/RoutineResetServiceTests.cs:108` and `:122` both pass
`now == lastReset`, so `candidate` is already in the future and the `while` loop body never executes.
They assert the first-cycle answer and stop.

## What to write

### 1. Pure-domain tests — add to `Services/RoutineResetServiceTests.cs`

Reuse the file's existing `MakePeriod(lengthInDays, anchorDay, streakThreshold, graceDays, lastResetAt)`
and `MakeItem(isDone)` helpers.

- `ComputeNextReset_MonthlyPeriod_WhenTheBoundaryHasPassed_ReturnsThatBoundary_NotTheNextOne`
  — `MakePeriod(30, 1, lastResetAt: new DateTime(2026, 1, 15, 0,0,0, DateTimeKind.Utc))`, `now =
  2026-02-01T02:00:00Z`. Expect `2026-02-01T00:00:00Z` (a due boundary in the past relative to `now`),
  **not** `2026-03-01`.
- `ComputeNextReset_YearlyPeriod_WhenTheBoundaryHasPassed_ReturnsThatBoundary_NotTheNextOne`
  — `MakePeriod(365, 1, lastResetAt: 2025-03-10)`, `now = 2026-03-01T02:00:00Z`. Expect `2026-03-01T00:00:00Z`.
- `TryReset_Batch_MonthlyPeriodPastItsAnchor_Resets` — the same period, one done item, threshold 100,
  `now = 2026-02-01T02:00:00Z`. Assert the result is **not null**, `period.LastResetAt` advanced, and
  `period.Streak == 1`. This is the assertion that actually matters: `ComputeNextReset` is only
  interesting through its callers.
- `TryReset_Batch_MonthlyPeriodBeforeItsAnchor_DoesNotReset` — the negative control, so the fix cannot
  be "always return the past boundary". `now = 2026-01-20`, expect null.
- Keep the two existing first-cycle tests; they are the other half of the contract.

### 2. Integration test — add to `Routines/RoutineTodoListResetJobHandlerTests.cs`

`ResetSweep_MonthlyAnchoredPeriod_ResetsWhenItsAnchorHasPassed`. Seed via the file's own
`SeedPeriodAsync`, which needs a new parameter — it currently hard-codes `ResetAnchorDay = 0`
(`:57`), which is exactly the branch that works. Add `int resetAnchorDay = 0` and pass `1`.

Seed `lengthInDays: 30, resetAnchorDay: 1, lastResetAt: <a date at least one month back, landing so
that today is past the 1st>` plus one done item; run the job; verify through a fresh context that a
`RoutinePeriodCompletion` row exists and `LastResetAt` moved.

⚠ The job uses `DateTime.UtcNow`, so this test's outcome depends on the calendar day it runs. Pick
`lastResetAt` relative to `DateTime.UtcNow` (e.g. `UtcNow.AddMonths(-2)`) rather than a fixed date, so
it is due on every day of the month.

## Expected state

**These tests should fail against the code as it stands** — that is the point. If the fix lands in the
same change, they pass. If the fix is deferred, tag them `[Trait("Status", "KnownGap")]` with a comment
naming `CQ-1` and pointing at `review/routines/02-findings.md`, and do not soften them.

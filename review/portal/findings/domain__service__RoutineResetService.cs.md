# Review: AdhdTimeOrganizer/domain/service/RoutineResetService.cs
Role: other (pure static domain service — reset/streak math, no DB access)
Summary: Solid pure-logic separation, but the two `TryReset` overloads disagree on whether a reset advances the streak, and callers mix them on the same period.
Coverage: n/a

## Issues
- [High][Quality] domain/service/RoutineResetService.cs:135-150 — The single-item `TryReset(period, item, now)` overload advances `period.LastResetAt` to `nextReset` without ever evaluating the streak (no `StreakOutcome`, no `RoutinePeriodCompletion`), yet `ToggleStepIsDoneRoutineTodoListEndpoint.cs:27` calls exactly this overload on `item.RoutineTimePeriod` while other call sites (`RoutineTodoListResetJob`, `GetAllGroupedRoutineTodoListEndpoint`, `RoutineToggleIsDoneTodoListEndpoint`) use the list-based overload that does evaluate the streak.
  Why: if a step toggle reaches its period's boundary before the list-based path does, it silently consumes the reset cycle (advances `LastResetAt` past `nextReset`) with no streak transition applied and no completion history row written — the cycle's outcome is lost permanently and the next list-based reset will only see the *following* cycle.
  Fix: route step-toggle resets through the list-based overload (load all `RoutineTodoList` items for the period) instead of the single-item one, or make the single-item overload defer/refuse to advance `LastResetAt` and let the list-based path own that transition.
  Confidence: Med

- [Medium][Quality] domain/service/RoutineResetService.cs:34-50 — The `LengthInDays == 30` and `LengthInDays == 365` branches of `ComputeNextReset` always step exactly one calendar month/year forward from `lastReset`, with no catch-up for `now`. A period left untouched for several cycles needs one `TryReset` call per missed cycle to catch up, and each intermediate catch-up manufactures a `RoutinePeriodCompletion` row (and streak transition) for a window using the *current* item state, which can fabricate repeated Broken/OnGrace outcomes that never really happened.
  Why: streak/grace history and counts can become inaccurate for routines that go dormant for multiple periods.
  Fix: when computing the next reset, jump directly to the first month/year boundary that is `>= now` rather than always stepping by exactly one.
  Confidence: Med

- [Low][Quality] domain/service/RoutineResetService.cs:15,69 — Reset instants are inconsistent in time-of-day: the weekly/short-period path resets at 00:00 UTC (via `.Date`), while the day-of-month/monthly/yearly path is hardcoded to 02:00 UTC. This makes "reset boundary" reasoning (and the nudge/grace lead-time windows in `EvaluateEndingSoon`/`ShouldWarnGraceExpiring`) depend on which period kind is in play.
  Why: an easy source of off-by-two-hours bugs when adding new nudge/grace features that assume midnight resets.
  Fix: pick one canonical time-of-day (midnight UTC) for all branches, or document/justify the 02:00 offset explicitly.
  Confidence: Med

- [Low][Quality] domain/service/RoutineResetService.cs:31 — Comment `// 1–28` for `targetDay` is stale/misleading: `Math.Min(targetDay, DateTime.DaysInMonth(...))` on line 68 handles values up to 31 fine, and there's no validation here that `ResetAnchorDay` is sane for the given `LengthInDays`.
  Why: misleading comment for future maintainers reasoning about valid input ranges.
  Fix: update the comment or add an explicit range guard/assert.
  Confidence: Low

- [Nit][Quality] domain/service/RoutineResetService.cs:141-146,197-205 — The item-reset loop (`IsDone = false; DoneCount = 0; LastResetDate = today;` plus step reset) is duplicated verbatim between the single-item and list-based `TryReset` overloads.
  Fix: factor into a private `ResetItem(RoutineTodoList item, DateOnly today)` helper called from both.
  Confidence: High

- [Low][Concurrency] domain/service/RoutineResetService.cs (whole file) — All reset logic is lazily re-evaluated on demand from multiple independent entry points (background job, grouped-read endpoint, toggle endpoints), and this file has no way to detect/prevent two of them racing on the same `RoutineTimePeriod` near a reset boundary — both could independently compute the same `nextReset`, apply the streak transition, and build a `RoutinePeriodCompletion` before either write is persisted. Correctness then depends entirely on the caller's optimistic-concurrency (`row_version`) handling on save, which is outside this file.
  Why: without a caller-side conflict check, one of the two writers could silently clobber the other's streak update.
  Fix: not fixable in this pure-logic file; confirm the job/endpoints that call `TryReset` handle `DbUpdateConcurrencyException` on `row_version` and retry/re-evaluate rather than blind-overwrite.
  Confidence: Low (persistence-layer behavior not visible in this file; caller code not reviewed here)

No AuditGap / ExecuteUpdate-ExecuteDelete / Result-pattern issues apply — this file performs no persistence at all (pure domain math over in-memory entities), which is itself a good separation of concerns.

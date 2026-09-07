# TEST-11 — `PUT /routine-time-period/{id}` and the freeze-budget clamp

Read `_SHARED-CONTEXT.md` first.

## Endpoint

`PUT /api/routine-time-period/{id}` →
`application/endpoint/routineTimePeriod/command/UpdateRoutineTimePeriodEndpoint.cs`, a
`BaseUpdateEndpoint<RoutineTimePeriod, RoutineTimePeriodRequest>` with
`Validator<RoutineTimePeriodValidator>()`. All the interesting behaviour is in
`application/dto/request/RoutineTimePeriodRequest.UpdateEntity` (`:59-88`).

**Request:** `text`, `color`, `lengthInDays`, `isHidden`, `resetAnchorDay`, `streakThreshold`,
`streakGraceDays`, `historyDepth`, `reminderLeadDays?`, `freezeBudget`.

The create path is well covered (`RoutineTimePeriodUniqueIndexTests`,
`RoutineTimePeriodRangeConstraintTests`); the update path has **no test at all**, and it carries three
rules that exist only in `UpdateEntity` and are asserted nowhere.

## Rule 1 — lowering the budget clamps the remaining count

`UpdateEntity:76-80`:

```csharp
entity.FreezeBudget = FreezeBudget;
entity.FreezesRemaining = Math.Min(entity.FreezesRemaining, FreezeBudget);
```

Without the clamp, `ck_routine_time_period_freezes_remaining_range`
(`RoutineTimePeriodConfiguration.cs:70-72`, `freezes_remaining <= freeze_budget`) rejects the update —
and the comment says so explicitly. This is the single most breakable line in the file: delete the
`Math.Min` and you get a 400 on an ordinary settings edit.

- `Update_LoweringTheFreezeBudget_ClampsFreezesRemaining` — seed `FreezeBudget = 5, FreezesRemaining = 5`,
  `PUT` with `freezeBudget: 2`, assert 200/204 and that the stored row has `FreezeBudget == 2` **and**
  `FreezesRemaining == 2`.
- `Update_LoweringTheFreezeBudget_DoesNotTopUpASpentBudget` — the other half, and the reason the code
  uses `Math.Min` rather than an assignment: seed `FreezeBudget = 5, FreezesRemaining = 1`, `PUT` with
  `freezeBudget: 3`, assert `FreezesRemaining` is still **1**, not 3. Editing a setting must not hand
  out freezes.
- `Update_RaisingTheFreezeBudget_LeavesRemainingAlone` — seed `2/1`, `PUT` budget 4, assert remaining
  is still 1 (the refill is the service's job, on its own window).

## Rule 2 — a schedule change clears the nudge idempotency mark

`UpdateEntity:64` + `:82-83`: `scheduleChanged` is computed from `LengthInDays` / `ResetAnchorDay`
**before** the assignments overwrite them, and only then is `EndingSoonNotifiedFor` cleared. The
comment explains the asymmetry: clearing unconditionally would re-nudge on every unrelated edit (a
colour change mid-window).

- `Update_ChangingLengthInDays_ClearsTheEndingSoonMark` — seed with `EndingSoonNotifiedFor` set, `PUT`
  a different `lengthInDays`, assert the stored mark is null.
- `Update_ChangingResetAnchorDay_ClearsTheEndingSoonMark` — same for the other input.
- `Update_ChangingOnlyTheColour_KeepsTheEndingSoonMark` — the control. This is the one that would catch
  someone "simplifying" the `scheduleChanged` guard away.

## Rule 3 — streak fields are not writable from a request

`UpdateEntity:85-87` deliberately omits `Streak`, `BestStreak`, `LastResetAt`, `StreakGraceUntil` and
`GraceNotifiedFor`, "so that editing a period cannot hand anyone a streak or silence a warning". The
request record has no such properties, so a JSON body carrying them is simply ignored by the binder.

- `Update_CannotSetTheStreakThroughTheRequest` — seed `Streak = 3`, `PUT` a body that *also* contains
  `"streak": 999, "bestStreak": 999, "lastResetAt": "2020-01-01T00:00:00Z"` (send it as an anonymous
  object with the extra fields — that is exactly the attack), assert the stored `Streak` is still 3 and
  `LastResetAt` unchanged. This is a mass-assignment guard and it is worth having explicitly.

## Also cover

- `Update_ToADuplicateText_Returns409` and `Update_ToADuplicateLengthInDays_Returns409` — both unique
  indexes are exercised on create (`RoutineTimePeriodUniqueIndexTests`) but not on update, and update
  is the easier way to hit them by accident.
- `Update_LoweringLengthInDaysBelowStreakGraceDays_Returns400` — the cross-field rule
  (`RoutineTimePeriodValidator:25-28`, `graceDays < lengthInDays`) evaluated against the *new* length.
- IDOR is **already covered** — `Endpoints/TodoListCrudAuthMatrixTests.cs` has
  `UpdateRoutineTimePeriodEndpointTests`, which asserts that another user's period id is a 404 with the
  row unchanged. Do not write it again.
- If `CQ-3` has been fixed, `historyDepth: 0` / `101` and `lengthInDays: 366` should now come back as
  field-keyed validation errors on this path too.

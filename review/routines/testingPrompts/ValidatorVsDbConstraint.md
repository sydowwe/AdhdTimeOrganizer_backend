# TEST-4 / TEST-10 — Make the range tests assert their own claim (pins `CQ-3`, fixes `DOC-5`)

Read `_SHARED-CONTEXT.md` first. This is a repair job on two existing files, not new coverage.

## Part 1 — `RoutineTimePeriodRangeConstraintTests` certifies a guarantee that does not hold

`AdhdTimeOrganizer.IntegrationTests/Endpoints/RoutineTimePeriodRangeConstraintTests.cs:10-13` says the
suite asserts "a clean 400 from FluentValidation rather than a 500 from ... DB check constraints".

It cannot tell the difference. `DbUtils.HandleException`
(`framework/Sydowwe.Framework/infrastructure/persistence/DatabaseExceptionUtils.cs:30-31`) maps
`PostgresErrorCodes.CheckViolation` → `ValidationError` → **400**. So a request that no validator rule
touches still comes back 400, after a wasted database round-trip, carrying the raw Postgres message
(`SEC-4`) instead of a field-keyed error.

Three of the ten tests are green for exactly that wrong reason, because
`application/validator/RoutineTimePeriodValidator.cs` has **no `HistoryDepth` rule at all** and **no
upper bound on `LengthInDays`**:

- `HistoryDepth_Zero_Returns400` (`:67`)
- `HistoryDepth_101_Returns400` (`:71`)
- `LengthInDays_366_Returns400` (`:39`)

### What to do

1. **Fix the code first** (`CQ-3`) — it is two lines in `RoutineTimePeriodValidator`:
   `RuleFor(x => x.LengthInDays).InclusiveBetween(1, 365);` and
   `RuleFor(x => x.HistoryDepth).InclusiveBetween(1, 100);`. Every other constraint in
   `RoutineTimePeriodConfiguration` already has a mirroring rule with a comment saying so; these two
   were missed.
2. **Strengthen all ten tests** so they can tell the layers apart. A 400 is not enough — assert on the
   response body. FastEndpoints returns a problem-details payload with an `errors` object keyed by
   property name for validation failures; a `DbUtils`-mapped check violation does not carry a property
   key. Read one existing 400 body in the debugger to get the exact shape, then add a shared helper:

   ```csharp
   private static async Task ShouldBeValidationErrorFor(HttpResponseMessage r, string property)
   ```

   asserting status 400 **and** that `property` appears as a key in the errors object. Route every one
   of the ten tests through it. That single helper is what makes the file's summary true.
3. Add the two boundary cases that should *succeed*, so the new rules cannot be over-tight:
   `LengthInDays == 365` and `HistoryDepth == 100` both create fine (mind the unique
   `(UserId, LengthInDays)` index — use a fresh user or a distinct length per test).

## Part 2 — `TryReset_Single_AfterResetTime_ResetsItemAndUpdatesPeriod` asserts nothing

`AdhdTimeOrganizer.IntegrationTests/Services/RoutineResetServiceTests.cs:204`.

The name says "UpdatesPeriod" and the body asserts `period.LastResetAt.Should().NotBeNull()`. The
single-item `TryReset` overload **deliberately never touches `LastResetAt`** — that is its entire
documented contract (`RoutineResetService.cs:161-169`, restated at `domain-map.md:42`), and advancing
it there would let the step-toggle path silently consume a reset cycle with no streak evaluation. The
assertion passes only because the fixture constructs the period with `lastResetAt: UtcNow.AddDays(-8)`.
It would pass if the method body were deleted.

### What to do

Rename to `TryReset_Single_AfterResetTime_ResetsItemWithoutAdvancingLastResetAt` and invert the
assertion: capture `period.LastResetAt` before the call and assert it is **unchanged** afterwards, and
that no completion row is produced (the single overload returns `bool`, not a
`RoutinePeriodReset` — so also assert the period's `Streak` did not move). That is the real guarantee
and it is currently unpinned anywhere in the suite.

While you are in the file, add the sibling that is also missing:
`TryReset_Single_DoesNotClearTheEndingSoonMark` — the list overload clears
`EndingSoonNotifiedFor` (`:240`, pinned at `:382`); the single one must not.

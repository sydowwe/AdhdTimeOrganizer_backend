# TEST-2 — Activity merge collides on two routines in one period (pins `CQ-2`)

Read `_SHARED-CONTEXT.md` first.

## The bug being pinned

`AdhdTimeOrganizer.Routines/application/seam/RoutineTodoListActivityReferenceSource.cs:35-45`.
`RepointAsync` rewrites `ActivityId` on every routine referencing a merged activity, with no collision
handling. `RoutineTodoList` carries a unique index on `(UserId, TimePeriodId, ActivityId)`
(`infrastructure/persistence/configuration/RoutineToDoListConfiguration.cs:53-54`).

If a user has a routine on activity **A** and another on activity **B** in the **same time period**,
merging B into A sets both rows to `ActivityId = A` → duplicate key → `DbUpdateException`. Activity
merge is a single transaction fanning out across six slices, so this fails the **entire merge**, not
just the routine part.

The class's own XML remark claims the opposite outcome ("Two routines on two duplicate activities stay
two routines pointing at one activity") — that is `DOC-3`; the comment and the code disagree with the
schema.

## Why the existing test misses it

`AdhdTimeOrganizer.IntegrationTests/Endpoints/ActivityMergeTests.cs:131-133` seeds exactly **one**
routine, on the duplicate activity. One row cannot collide with itself. It needs a second routine on
the survivor activity, in the same period.

## What to write

Add to `Endpoints/ActivityMergeTests.cs`, beside the existing routine assertions.

`Merge_TwoRoutinesInTheSamePeriod_ResolvesTheCollision`:

1. Seed one `RoutineTimePeriod` (use `TodoListTestSeedHelper.SeedRoutineTimePeriodAsync`).
2. Seed **two** `RoutineTodoList` rows in that one period — one on the survivor activity, one on the
   duplicate — via `TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, activityId, periodId, …)`.
   Give them different `Streak` values (say survivor 7, duplicate 2) so you can assert which one won.
3. Perform the merge the way the existing tests do.
4. Assert the merge **succeeds** (it currently throws), that exactly one routine remains in that
   period, that it points at the survivor activity, and that its `Streak` is 7 — the survivor's.

Also add the control that must keep working:
`Merge_TwoRoutinesInDifferentPeriods_KeepsBothAndRepointsBoth` — same two activities, two *different*
periods (remember: distinct `LengthInDays` per user). Both rows survive, both point at the survivor,
both streaks intact. This is the case the existing remark describes correctly and it must not regress
when the collision rule lands.

## Note for whoever fixes it

The collision rule is a **product decision** and `MIG-4` flags it: one of the two streaks is
destroyed, and the seam's own comment calls a streak the thing "no amount of re-creating the activity
brings back". Keep-the-survivor is the least surprising rule and is what the test above assumes; if a
different rule is chosen, change the test to match and update the XML remark and
`domain-map.md → Invariants` in the same pass.

## Expected state

Fails today (with a `DbUpdateException` surfacing as a 500 or a failed merge result, depending on how
the merge endpoint handles it). If the fix is deferred, tag `[Trait("Status", "KnownGap")]` naming
`CQ-2`.

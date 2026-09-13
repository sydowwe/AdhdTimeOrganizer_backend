# AdhdTimeOrganizer.Routines — Testing

## How to test this project

Routines has **no test project of its own**. Everything lives in
`AdhdTimeOrganizer.IntegrationTests`, split by what the test actually needs:

| Folder | Needs | What's there |
|---|---|---|
| `Services/` | Nothing — plain xUnit | `RoutineResetServiceTests`, `RoutineStreakFreezeServiceTests`. Both domain services are static and pure, so the streak arithmetic is unit-tested against hand-built objects with an injected `now`. |
| `Endpoints/` | Postgres | `RoutineGroupedReadAndMoveItemTests`, `RoutinePeriodCompletionHistoryTests`, `RoutineTimePeriodRangeConstraintTests`, `RoutineTimePeriodUniqueIndexTests`, `RoutineStreakFreezeTests`, `UserRoutineSettingsTests`. |
| `Routines/` | Postgres | The two job sweeps, the notification mapping and the write races: `RoutineTodoListResetJobHandlerTests`, `RoutinePeriodNudgeJobTests`, `RoutineNotificationTests`, `RoutineStreakFreezeGraceTests`, `RoutineResetConcurrencyTests`. |

Infrastructure comes from `Sydowwe.Framework.Testing`: `PostgresTestBase` over the `"Postgres"`
collection (`AppDbContextFixture`, a real container), and `UserRoleTestAuthHandler` for role and
ownership scenarios. See the portal's `docs/testing.md` and `docs/claude/testing.md`.

Two bespoke helpers you will want:

- **`TodoListTestSeedHelper`** (`Endpoints/`) — `SeedRoutineTimePeriodAsync` /
  `SeedRoutineTodoListAsync`. Use these rather than hand-rolling rows; the period has nine CHECK
  constraints and two unique indexes, and a hand-built row usually trips one of them for reasons
  unrelated to the test.
- **The two test doubles in `RoutinePeriodNudgeJobTests`** — `RoutinePeriodNotificationServiceDouble`
  (records calls, and can be told to throw on the *n*-th one) and `NoAmbientUserLoggedUserService` /
  `UnauthenticatedLoggedUserService` (the reset job's equivalent). The second matters more than it
  looks: **the jobs run unauthenticated**, which is exactly when the host's per-user query filter goes
  inert, and that is the condition a sweep depends on. A test that runs the job with an ambient user
  is testing the wrong thing — it will pass while a real sweep only ever touches one user.

## Strategy

**Pure streak logic is unit-tested; anything that depends on the filter, the constraints, or the
schedule is integration-tested.** `RoutineResetService` and `RoutineStreakFreezeService` persist
nothing and take `now` as a parameter, so every branch — anchor-day snapping across weekly/biweekly/
monthly/yearly, grace expiry, threshold evaluation, the freeze budget window rolling over December —
is a cheap unit test. Don't push those into Postgres.

**CRUD goes through the Framework base test classes**, since most of the 23 endpoints subclass a
Framework or TodoLists base. The hand-written endpoint tests cover only what those bases can't know:
the grouped read's reset-on-read side effect, the freeze endpoint's rejection matrix, the completion
history's depth cap and ordering, and the DB-layer range/uniqueness rejections.

**Assert on rows, not on routes.** Almost everything load-bearing here fails silently — a missing
registrar line means the jobs never fire, a stale freeze budget means a 400 the user didn't earn, a
renamed seam key means History's routine filter just stops narrowing. None of that changes a status
code.

**Cross-user sweeps get an explicit test.** `ResetSweep_ResetsPeriodsForEveryUser_NotJustTheAmbientOne`
and `NudgeSweep_SweepsAllUsers_NotJustTheAmbientOne` exist because the failure — a sweep that quietly
only ever processes one person — is invisible in a single-user test, which is what you write by
default.

## The guards — do not delete these

| Test | Pins |
|---|---|
| `ModuleWiringTests` (job-key + schedule assertions) | Both handlers resolve by key **and** both schedules are actually registered. The registrar is a separate `Program.cs` line from the handler scan — dropping it builds fine and the jobs never fire |
| `RoutineTodoListResetJobHandlerTests.ResetSweep_DoubleFire_ProducesExactlyOneCompletionRowAndOneStreakAdvance` | The reset is idempotent within a period. A double fire that double-advances is invisible until a user's streak is wrong by an unexplainable amount |
| `ResetSweep_PersistsGraceExpiry_WhenNoPeriodIsDueForReset` | A grace-expiry streak break is persisted even when nothing else in the sweep changed — the early-return has to honour `CheckGrace`'s `periodsChanged`, not just the reset list |
| `ResetSweep_PersistsTheReset_EvenWhenTheNotifierThrows` | The commit-then-notify ordering. A notification failure must never roll back a reset that already happened |
| `NudgeSweep_MidLoopNotifierFailure_PreservesEarlierMarkersAndStillProcessesLaterPeriods` | One period's failure doesn't lose the markers already set in this sweep or skip the periods after it — otherwise a single bad period re-nudges everyone tomorrow |
| `NudgeSweep_*_IsSentExactlyOnceAcrossTwoRuns` (×2) | Both idempotency marks (`EndingSoonNotifiedFor`, `GraceNotifiedFor`). A daily sweep with a broken mark is a daily duplicate notification |
| `NudgeSweep_AllItemsDone_SkipsWithoutMarking_ThenNudgesOnceAnItemIsUnticked` | The deliberate asymmetry: an already-done period is skipped **unmarked**, so un-ticking tomorrow still earns its nudge. "Simplifying" this to mark-on-evaluate silently loses that nudge |
| `RoutineStreakFreezeServiceTests.RecomputeStreak_FrozenPeriodCarriesTheRun_WithoutExtendingIt` | The rule the whole feature rests on. If a freeze ever incremented, spending one would beat doing the routine |
| `RecomputeStreak_NeverLowersAnExistingStreak` | History is finite and a run can predate its oldest row. The one action a user spends a resource on to protect a streak must not be what cuts it |
| `SpendFreeze_LeavesTheRecordedCountsAlone` | A freeze changes scoring, not what happened — the completion *rate* stays honest |
| `SpendFreeze_AfterTheWindowElapsed_RefillsTheBudgetFirst` · `GroupedRead_ServesTheFreezeFields_AndRefillsAnElapsedWindow` | The lazy refill, from both a write and a read path. There is no job; a path that forgets `RefreshFreezeBudget` serves a stale count and 400s a freeze the user is owed |
| `SpendFreeze_OnAnotherUsersPeriod_IsNotFound` · `GroupedRead_GroupsByPeriod_AndScopesToCallingUser` · `UserRoutineSettingsTests.AnotherUsersDismissal_IsNotVisible` | Per-user scoping actually holds on the module's reads — the endpoints don't scope by hand, the DbContext filter does, and nothing here proves that except a second user |
| `RoutineTimePeriodUniqueIndexTests.Seeder_SetupDefaults_SeedsAllFourDefaultsWithoutUniqueViolation` | `RoutineTimePeriodSeeder.Collides` checks **both** unique indexes. Simplify it to `Text` only and the fourth default collides on `LengthInDays` |
| `RoutineTimePeriodRangeConstraintTests` (10 cases) | Every CHECK constraint is reachable through the API and rejects at 400, not 500 |
| `RoutinePeriodCompletionHistoryTests.History_MoreCompletionsThanDepth_CapsToNewestAndReturnsOldestFirst` | Cap-to-newest then render oldest-first — two orderings that look interchangeable and are not |
| `NudgeSweep_IgnoresUserTimezone_IdenticalUtcPeriodsProduceIdenticalResultsRegardlessOfOffset` | Periods are UTC end to end. This is the pin that catches someone "helpfully" adding zone conversion to the sweep |
| `ComputeNextReset_{Monthly,Yearly}Period_WhenTheBoundaryHasPassed_ReturnsThatBoundary_NotTheNextOne` · `ResetSweep_MonthlyAnchoredPeriod_ResetsWhenItsAnchorHasPassed` | The calendar-aligned branches must answer with the boundary that is **due**, not the next one. Every user is seeded a Monthly and a Yearly with a non-zero anchor, so an answer that is always in the future means the default configuration never resets at all — silently, with a plausible `NextResetAt` still projected. The pre-existing tests passed `now == lastReset`, which cannot reach it |
| `TryResetCatchUp_*` (6 cases) · `ResetSweep_PeriodDormantForSeveralCycles_ClosesThemAllInOneRun` · `GroupedRead_PeriodDormantForSeveralCycles_CatchesThemAllUpInOneRead` | One `TryReset` closes one cycle, so a reset site that calls it directly resolves one cycle per request — a period dormant for sixty cycles needs sixty nightly runs, each announcing a miss weeks old. Pins that one pass leaves the period current, writes a row per closed cycle, caps them at `HistoryDepth`, notifies once, and breaks the streak on a lapsed grace at the cycle a live run would have |
| `RoutineTodoListUpdateEndpointTests` (6 cases) | The three things `PUT /routine-todo-list/{id}` must not do. `IsDone` / `DoneCount` are server-owned, because completing an item anywhere but the toggle endpoint skips the item streak and `RoutineTodoListIsDoneChangedEvent` — the only channel into Planning, and its handler log-and-swallows, so the paired task just stays stale. `DisplayOrder` is per-period, so it is re-derived when the edit moves the item and left alone when it does not. Steps are reconciled by id: rebuilding them mints a fresh `Guid` per step per save, and three step routes take that id in the path |
| `RoutineFlatPeriodReadTests` (7 cases) | The four reads that sit on the framework projection bases. `Projection` is an expression tree, so on its own it serves an epoch `NextResetAt`, an empty heatmap, a budget nothing refills (invariant 9) and a `Streak` whose grace window lapsed days ago — all behind a well-formed 200. Asserted on rows too, because both refreshes must be *persisted*: projecting them alone hands out a fresh budget on every read. The two "leaves it alone" cases are half the pin — an over-eager refresh makes the budget infinite and grace worthless |
| `RoutineStreakFreezeGraceTests.SpendFreeze_WithALapsedGraceWindow_ClearsIt_SoTheNextReadKeepsTheRestoredStreak` | `CheckGrace` runs **before** `Apply`. `Apply` clears the window only for the newest entry, so freezing an older miss against a lapsed window leaves it standing; `RecomputeStreak` only ever raises, so the response is correct and the *next* read zeroes the streak. Asserted through a second request for exactly that reason |
| `RoutineStreakFreezeGraceTests.SpendFreeze_OnAPeriodThatElapsedWithNoItems_Refuses_AndChargesNothing` | `TotalCount == 0` is `NotAMiss`. `IsSuccess` says false for an item-less period, but `RecomputeStreak` skips those rows and `TryReset` leaves the streak `NotEvaluated` — so the run already carries across it and a freeze would be charged for a no-op |
| `RoutineTodoListPeriodRulesTests` (create + update, foreign `TimePeriodId`) | Invariant 11, asserted on rows. A routine item pinned into another user's period is stamped with the attacker's own `UserId`, so nothing errors and no read of theirs shows it — the only visible effect is the victim's streak breaking. Deleting the validators' `p.UserId == userId` re-opens it silently |
| `ResetSweep_AcrossPageBoundaries_ResetsEveryUsersPeriods` · `NudgeSweep_AcrossPageBoundaries_NotifiesAndMarksEveryUser` | Both sweeps walk the table a page of users at a time and release the page's entities with `ChangeTracker.Clear()`. A clear that lands while later periods are still tracked detaches them and their saves write nothing — no error, no log line, just users who quietly stop being swept. Both force a boundary with `userPageSize: 1` and assert on the **rows** (the nudge one on the persisted markers, not the notify calls, because a lost per-page commit looks fine today and re-notifies everyone tomorrow). The reset case gives the first user two periods, since a page is a set of users and one user's periods must never be split across two of them |
| `RoutineResetConcurrencyTests` (5 cases) | Three sites close a cycle and none locks, so the completion insert's unique `(TimePeriodId, PeriodStart)` is where two callers meet. The loser is simulated by seeding the row the winner would have written, which is deterministic where a real race is not. Pins that the grouped read still serves 200, the toggle still applies the user's tick, and — the one that used to hurt most — that a collision on one period no longer aborts the rest of the nightly sweep. The no-collision control is half the pin: "catch and carry on" must not degrade into "never write anything". The last case races two first-time `PUT /routine/settings` writes, which is the only way to reach that endpoint's recovery branch |
| `RoutineToggleIsDoneTests` (14 cases) | Both toggle routes, which had no coverage of any kind. The item streak is **once per cycle, not once per toggle**, asserted through the endpoints on both routes into inflation — un-tick/re-tick, and a repeated `ForceValue = true` at an already-complete counted item, which needs no un-tick at all and is the case a guard keyed on "was it un-ticked" would miss. Since ask B9 (2026-09-13) the un-tick is also a **walk-back**: `Toggle_UntickInsideTheSameCycle_GivesTheStreakBack` pins the point and the record both returning, `Toggle_UntickAfterTheCycleClosed_LeavesTheEarlierScoreAlone` that a closed cycle's score is beyond reach, and `Toggle_OnAnElapsedPeriod_BreaksTheStreakOfAnItemLeftUndone` that an item's own streak now breaks on a miss at all — before it, nothing anywhere zeroed `RoutineTodoList.Streak`. The bulk toggle is a **reset site**: it catches an elapsed period up, writes the completion row and unticks the other items *before* applying the tick, and announces the summary from `AfterSave` — pinned by a probe on a second connection, so a hook that drifts ahead of the commit fails rather than passing by luck. `RoutineTodoListIsDoneChangedEvent` is asserted leaving **this** route (`CompletionFanOutTests` drives the TodoLists sibling, so it proves nothing here). The step route pins the single-item `TryReset` asymmetry at endpoint level — item fresh, `LastResetAt` unchanged, zero completion rows, period streak untouched — and that completing an item by its last step scores at all, and un-ticking that step gives it back: the hook (`OnItemDoneChanged`, then `OnItemCompleted`) had its only call site commented out in TodoLists' `BaseToggleStepIsDoneEndpoint`, so it scored nothing until 2026-09-07 while the bulk route scored normally. Neither endpoint scopes by `UserId`; both IDOR cases assert the 404 **and** the unchanged row |
| `RoutineResetServiceTests.UpdateItemStreak_*` (8 cases) · `TryReset_*_Item*` · `TryResetCatchUp_ItemDoneAtTheFirstBoundaryOnly_KeepsThenBreaks` | The pure-domain half of the above. The one that matters most is `UpdateItemStreak_UntickAfterReachingButNotBeatingTheRecord_LeavesTheRecordAlone`: `BestStreak == Streak` after a tick reads identically whether the tick set the record or merely reached it, so a walk-back derived from the counters would lower a record the tick never raised. The `BestStreakBeforeScore` mark exists for exactly that case; a "simplification" that replaces it with `if (BestStreak == Streak) BestStreak--` goes red here and nowhere else |

Owned elsewhere but load-bearing for this module: `SeamWiringTests` (both keyed source families cover
every key exactly once — a renamed `ActivityMembershipSourceKeys.RoutineTodoList` is otherwise
silent), `ActivityMergeTests` / `ActivityForeignKeyInventoryTests` (the reference seam's repoint),
`ModuleSchemaTests` (this assembly resolves to the `routines` schema),
`UserDataExportAndDeletionTests` (see below), `PerUserDefaultSeederTests` /
`PerUserDefaultMatcherTests`, and `CompletionFanOutTests` (the host-side
`RoutineTodoListIsDoneChangedEventHandler` fan-out into Planning).

## Known gaps (living list)

Nothing here is carried as an expected-failing `[Trait("Status","KnownGap")]` test.

Real gaps, in decision terms:

- **`RoutineTimePeriod` and `UserRoutineSettings` are not in the user data export.** Both are on
  `UserDataExportAndDeletionTests.KnownGaps`; only `RoutineTodoList` is exported. That means an
  exported archive carries the items but not the periods that group them, nor the streaks and freeze
  budgets on those periods. Adding a new user-scoped entity to this project fails that test until it
  is classified one way or the other — intended, not a flake.
- **`RoutinePeriodCompletion` has no direct test of its per-user reachability**, because it is a plain
  `BaseEntity` scoped only transitively through `TimePeriodId`. The freeze and history endpoints cover
  it at the edges (`SpendFreeze_OnAnotherUsersPeriod_IsNotFound`, `History_ForeignPeriod_Returns404`);
  a new read path that reaches completions without going through the period has no safety net.
- **No test asserts the two jobs' cron cadence**, only that both schedules are registered. Changing
  `0 0 2 * * ?` to a different hour moves everyone's reset instant with nothing going red.
- ~~**The single-item `TryReset` overload's deliberate asymmetry** … the unit tests are the whole
  guard.~~ **Closed 2026-09-07.** `RoutineToggleIsDoneTests.StepToggle_OnAnElapsedPeriod_ResetsTheItemButNotThePeriod`
  is the endpoint-level guard this asked for: it drives the real step route at an elapsed period and
  asserts all four halves of the asymmetry on rows. Writing it also turned up that the same route scored
  no item streak at all — the hook it needed was commented out in a TodoLists base — which is the kind of
  thing an entry like this one exists to flush out. Keep the unit tests; they cover the overload directly.

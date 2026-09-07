# AdhdTimeOrganizer.Planning — Testing

## How to test this project

Planning has **no test project of its own**. Everything lives in
`AdhdTimeOrganizer.IntegrationTests`, split by what the test actually needs:

| Folder | Needs | What's there |
|---|---|---|
| `Services/` | Nothing — plain xUnit | `PlannerStreakServiceTests` (17), `PlanVsActualTrendServiceTests` (12). Both domain services are static and pure, so every rule is a cheap unit test against hand-built tallies with an injected `today`. |
| `Endpoints/` | Postgres | `PlanningRouteSmokeTests`, `PlanningCrudAuthMatrixTests`, `PlannerDeleteBehaviorTests`, `PlannerStreakTests`, `PlannerTaskStatusAndSpanTests`, `CalendarBatchEndpointTests`, `DayPlanEndpointTests`, `LazyCalendarCreationTests`, `PlanVsActualTrendTests`, `ApplyTemplatePlannerTaskConflictResolutionTests`, `RepeatingPlannerTaskSuggestionsTests`, `TaskImportanceReorderTests`, `TaskPlannerDayTemplatePinTests`, `UserPlannerSettingsTests`. |
| `Reminders/` | Postgres | This slice's `Reminder` **and** the `Sydowwe.Reminders` module surface it registers against: `ReminderRegistrationTests` (the seam), `ReminderEndpointTests` (CRUD bases), `ReminderCrudAuthMatrixTests`, `ReminderOccurrencePreviewTests`, `ReminderOverviewFailuresTests`, `ReminderBatchLifecycleTests`. |

Infrastructure comes from `Sydowwe.Framework.Testing`: `PostgresTestBase` over the `"Postgres"`
collection (`AppDbContextFixture`, a real container), and `UserRoleTestAuthHandler` for role and
ownership scenarios. See the portal's `docs/testing.md` and `docs/claude/testing.md`.

Two bespoke helpers you will want:

- **`PlanningTestSeedHelper`** (`Endpoints/`) — builds the calendar / activity / planner-task
  scaffolding a planning test needs. Use it rather than hand-rolling rows: `(user_id, date)` on
  `calendar` is unique and `planner_task.CalendarId` is `Restrict`, so a hand-built day usually trips
  one of those for reasons unrelated to the test.
- **`ReminderSeedHelper`** (`Reminders/`) — the portal `Reminder` plus the module-side
  `ReminderDefinition` it registers into. A reminder test that only writes the portal row is testing
  half the seam; the whole point of `ReminderRegistrationService` is what happens on the other side.

## Strategy

**Pure rules are unit-tested; anything that depends on the query filter, the FK graph, the timezone
or the registry is integration-tested.** `PlannerStreakService` and `PlanVsActualTrendService`
persist nothing and take `today` as a parameter, so day-boundary, midnight-wrap, empty-day and
never-happened branches are all cheap unit tests. Don't push those into Postgres — but do keep the
*predicate* halves (`PlannerStreakReader`, `GetPlanVsActualTrendPlannerTaskEndpoint`) covered by an
HTTP test, because that is where the split can silently drift.

**CRUD goes through the Framework base test classes.** Most of the ~60 endpoints subclass a
Framework base, and `PlanningCrudAuthMatrixTests` wires 23 of them into the base suites in one file.
The hand-written endpoint tests cover only what those bases cannot know: the batched entry points'
per-day outcomes, lazy day creation, the four conflict-resolution modes, the streak, the trend, the
pin's absoluteness, the settings row's get-or-create, and the reminder re-registration paths.

**Assert on rows, not on routes.** Nearly everything load-bearing here answers `200` when it is
broken: a batch that wrote to the wrong day, a day plan rendering somebody else's tasks, a streak
field left null, a reminder whose module-side definition was never cancelled. None of that changes a
status code.

**Every batched or cross-day surface gets a second user.** `CalendarBatchEndpointTests`,
`DayPlanEndpointTests`, `PlanVsActualTrendTests` and `TaskPlannerDayTemplatePinTests` each hand the
endpoint at least one id belonging to somebody else and assert it comes back `succeeded: false` /
404 / uncounted, with that user's rows untouched. The single-user version of each of these passes
while a lost query filter serves other people's days.

## The guards — do not delete these

| Test | Pins |
|---|---|
| `PlanningRouteSmokeTests.PlannerTaskToTodoListItem_ForeignKey_SurvivesTheSliceSplit` | The one cross-slice FK — its column, `SetNull`, the pinned constraint name, and the *absence* of navigations on both ends. It is declared host-side; deleting that line builds fine and the FK just leaves the model |
| `PlanningRouteSmokeTests.PlanningRoutes_AreRegistered` | The slice is in FastEndpoints' `o.Assemblies`. Missing → every planner route silently 404s |
| `PlannerDeleteBehaviorTests` (5) | The whole delete graph in one place: calendar→tasks is `Restrict` (a day with tasks must not vanish), activity→task cascades, importance→task is `SetNull`, and **planner-task→reminders cascades** — the cascade the reminder delete endpoints are written around |
| `PlannerStreakServiceTests.Walk_UntickingAndRetickingATask_IsExactlyReversible` | The reason the streak is derived rather than stored. A counter with a hand-written inverse is what this replaced |
| `Walk_IncompleteToday_DoesNotBreakTheStreak` · `Walk_EmptyDay_DoesNotBreakTheStreak` · `Walk_EmptyDay_DoesNotItselfCount` | The three leniencies that make the number mean "days I completed". Each looks like an off-by-one and is a product ruling |
| `PlannerStreakTests.Streak_OfThreeCompleteDays_RidesAlongOnThePlanResponse` | The streak is actually non-null on the response. `GetByDateCalendarEndpoint` fills it in *after* the projection; dropping that line nulls the field and the chip sticks at zero with nothing failing |
| `Streak_DayWhereEverythingWasSkipped_DoesNotCount` · `Streak_DayOfOnlyBackgroundTasks_IsInvisible` | Zero denominator is *empty*, not complete. Otherwise skipping everything earns a day |
| `PlanVsActualTrendServiceTests.Aggregate_CompletedTaskMissingAnActualTime_IsCountedButNotMeasured` | The trap in the response: `plannedMinutes` and `actualMinutes` do not span the same tasks. Inferring the missing end would fabricate exactly the zero drift the number measures |
| `Aggregate_TaskPlannedAcrossMidnight_HasAPositiveSpan` · `Aggregate_StartedJustAfterMidnight_IsLateNotWildlyEarly` | Midnight wrap in both `SpanMinutes` and `DriftMinutes`. Without it a 23:50→00:10 task reads as 23 hours early |
| `PlanVsActualTrendTests.Trend_ExcludesToday_HoweverLateItIs` | Today's untouched tasks have not been missed yet. Including them drags a thirty-day trend with every day's unfinished work |
| `CalendarBatchEndpointTests.*_FailsOnlyTheUnreachableDay` (×2) | A batch is deliberately **not** transactional and reports per day. A day the caller does not own is `succeeded: false`, not a 403 and not a failed batch |
| `CalendarBatchEndpointTests.ApplyTemplateBatch_RejectsDuplicateCalendarIds` | The response promises one result per requested id *in order*; a duplicate makes that mapping ambiguous |
| `CopyToDays_CopiesPlanWithoutExecutionState_AndFailsOnlyTheUnreachableDay` | A copied day arrives `NotStarted` with no actual times, no skip reason, no Google event id and **no `TodolistItemId`** — a copied link would fan one done-toggle across every copy |
| `BatchDayType_WeekendRuleFailsOneDayWithoutFailingTheBatch` | `CalendarDayTypeRule` has one implementation behind two failure shapes (400 vs. one day's `succeeded: false`) |
| `DayPlanEndpointTests.DayPlan_ForADateWithNoCalendar_Is200WithNothingInIt_NotA404` | An unplanned day is a page load, not a lookup. A 404 there renders a retry button over a day that is merely empty |
| `DayPlan_CarriesTheStreakAtTheTopLevel_AndNotAlsoOnTheCalendar` | One copy of the streak. Two would be filled by different paths and eventually disagree |
| `LazyCalendarCreationTests.ALazilyCreatedDay_CarriesTheSameMetadataTheSeederWouldHaveGivenIt` | The drift guard on `CalendarDayFactory`. If the seeder and the provisioner stop sharing it, a lazily created Christmas quietly loses its holiday name while every "does a row exist" assertion still passes |
| `LazyCalendarCreationTests.ATaskNamingItsDayTwice_OrNotAtAll_IsRejected` | Exactly one of `CalendarId` / `Date`. A precedence rule instead would file tasks on a day nobody asked for |
| `ApplyTemplatePlannerTaskConflictResolutionTests` (4) | All four modes, in the one place they are implemented. The batched apply shares `PlannerTemplateApplier`, so these cover both routes |
| `ReminderRegistrationTests.TaskLinkedReminder_DerivesItsInstantFromTheTask` · `MovingTheTask_ReRegistersTheReminder` | `Reminder.RemindAt` is a cache of the task's instant. A move that skips the re-sync leaves a reminder firing at the old time, silently |
| `ReminderRegistrationTests.DeletingTheTask_CancelsItsReminder` · `FinishingTheTask_CancelsItsReminder` | The FK cascade takes the portal rows; it does **not** reach the module's `ReminderDefinition`. That is why the delete paths capture ids *before* the delete and cancel after the commit |
| `ReminderRegistrationTests.PlannerTaskWithoutAReminder_NeverNotifies` | Reminders are opt-in per task. The absence of a row is the "no reminder" state |
| `ReminderRegistrationTests.RemindersDisabled_StillCreatesTheReminderWithoutTheLeadDefault` | `UserPlannerSettings.RemindersEnabled` suppresses the lead-offset *prefill*, never the reminder. It is a preference, not a kill switch |
| `PositiveLeadOffset_IsRejected` · `PastOneShotReminder_IsRejected` · `PastAnchoredRecurringReminder_IsAccepted` | The three rules that look inconsistent and are not: offsets are `<= 0`, a one-shot in the past is a mistake, a recurring anchor in the past is a birthday |
| `TaskImportanceReorderTests.NaiveReorder_DirectlyOntoLiveRank_Returns409AndLeavesBothRowsUnchanged` · `OptionalSentinel_SurvivesNeighbouringReorders` | `(user_id, importance)` is unique, so reordering needs a temporary value — and `OptionalMarkerValue` (666) must survive it, because `BasePlannerTask.IsOptional` reads that exact number |
| `TaskPlannerDayTemplatePinTests.Pin_IsAbsolute_NotAToggle` · `UpdatingTheTemplate_LeavesThePinAlone` | Two devices pinning converge, and an edit from a form opened before the pin cannot carry a stale `false` back — which is why `IsPinned` is absent from `TaskPlannerDayTemplateRequest` |
| `UserPlannerSettingsTests.RepeatedGet_CreatesExactlyOneRow` · `GetThenPut_StillOnlyOneRow` · `ConcurrentFirstGet_BothSucceed_AndLeaveOneRow` · `ConcurrentFirstPut_BothSucceed_AndLeaveOneRow` | The settings read *and* write are both get-or-create against a table whose `user_id` is unique, so a second row cannot exist — the failure it converts into is the loser of a first-time race taking a unique violation, which used to be a 500 on a plain page load. The two race tests overlap real requests; `UncontendedFirstGet_StillCreatesTheRow` is the control that stops the recovery degrading into never inserting |
| `PlannerTaskStatusAndSpanTests.SettingCancelledOrNotStarted_ClearsActualTimes` | `PlannerTask.ApplyStatus` is the shared reset. A call site that sets `Status` directly leaves stale actual times that the trend then measures |
| `RepeatingPlannerTaskSuggestionsTests` (5) | The suggestion tiers: three occurrences make a pattern, a cancelled one doesn't count, and a higher tier suppresses the ones below it |
| `PlannerReferencedIdOwnershipTests` (10) | The ids a planner write carries but does not mint — `ActivityId`, `ImportanceId`, `TodolistId`, `DefaultApplyTemplateId`. No FK spans the user column, so the database cannot express this and every check is one endpoint's own. Each negative case has a control, because a guard that rejected everything would satisfy all of them — and the `TodolistId` control is the one that catches the `IUserOwnedReferenceVerifier` seam failing **closed** for everyone. One case pins *ordering* rather than presence: the guard runs before `ICalendarProvisioner`, which commits, so a rejected create leaves no lazily-provisioned day behind |
| `TodoListItemFanOutUserScopingTests` | `TodoListItemIsDoneChangedEventHandler` scopes on the **event's** `UserId`, not the ambient filter. Driven against the handler from the unauthenticated factory's scope, because that is the only way to reach the `ScopeUserId == null` state that turns the global filter off — which is the finding's actual precondition, not an artificial one |
| `RoutineCompletionFanOutTests` (6) | `RoutineTodoListIsDoneChangedEventHandler`, which must stay the same shape as its to-do sibling: `ApplyStatus` rather than a bare `Status` write, `Cancelled` tasks skipped, `SyncForPlannerTasksAsync` after the save, and the whole body wrapped — a throw here 500s a Routines endpoint that already committed. All four were defects (`CQ-1`…`CQ-4`) found only because the handler had no tests |

Owned elsewhere but load-bearing for this project: `SeamWiringTests` (the keyed
`IActivityReferenceSource` family covers every key exactly once — a renamed
`ActivityReferenceSourceKeys.PlannerTask` is otherwise silent), `ActivityMergeTests` /
`ActivityForeignKeyInventoryTests` (the reference seam repoints **all three** task tables),
`ModuleSchemaTests` (this assembly resolves to the `planning` schema), `NamedQueryFilterTests`,
`UserScopingQueryFilterTests`, `SuggestionPatternRefreshTests` (the host-side views the two
suggestion endpoints read), `CompletionFanOutTests` (the host-side
`PlannerTaskIsDoneChangedEventHandler` and this project's `TodoListItemIsDoneChangedEventHandler`;
despite its class doc it never touched the routine handler — see the row above for that one),
`UserDataExportAndDeletionTests`, `PerUserDefaultSeederTests` /
`PerUserDefaultMatcherTests`, and `SyncCalendarToGoogleTests` / `GoogleCalendarConnectionTests` (the
host-side integration that reads `Calendar`).

## Known gaps (living list)

Nothing here is carried as an expected-failing `[Trait("Status","KnownGap")]` test.

Real gaps, in decision terms:

- **Only `Calendar`, `PlannerTask` and `TaskPlannerDayTemplate` are in the user data export.**
  `Reminder`, `RepeatingPlannerTask`, `TemplatePlannerTask`, `TaskImportance`, `UserPlannerSettings`
  and `PlannerSuggestionFromDayTemplate` are all on `UserDataExportAndDeletionTests.KnownGaps` — so
  an exported archive carries the days and their tasks but not the recurrences, the templates' lines,
  the importance levels those tasks point at, or the user's reminders. Adding a new user-scoped
  entity to this project fails that test until it is classified one way or the other — intended, not
  a flake.
- **The two suggestion endpoints have asymmetric coverage.**
  `RepeatingPlannerTaskSuggestionsTests` covers the repeating-task tiers;
  `GetSuggestionsTaskPlannerDayTemplateEndpoint` (over `mv_template_suggestion_pattern`) has no
  equivalent. Its view is refreshed by the same host-side job, so a broken projection there is silent.
- **`CalendarProvisioner`'s race branch is not exercised concurrently.** The `DbUpdateException`
  path — detach the failed insert, read the winner — is reasoned about in the code and covered only
  by the single-writer tests. A regression that forgets the detach shows up as the *endpoint's* next
  save re-sending a duplicate insert, which no current test reaches.
- **Nothing asserts the batch ceilings at their boundary.** `PlannerBatchLimits.MaxCalendarIds` /
  `MaxSummaryRangeDays` (366 each) are enforced by the validators, but the tests exercise inverted
  and duplicate ranges rather than an over-length one, so raising a ceiling by accident goes unnoticed.
- **`Calendar.Location`, `Weather` and `Notes` have no behavioural coverage.** They round-trip
  through the CRUD bases and nothing else reads them; `Weather` in particular is a free-text column
  unrelated to the Leisure weather-fit machinery, which is a naming collision worth not deepening.
- **`RepeatingPlannerTask` has no materialisation path under test** — nothing in the solution turns
  an active recurrence into `PlannerTask` rows on a matching day. The entity, its CRUD and its
  suggestion feed are covered; the "and then it appears on the day" half does not exist yet.

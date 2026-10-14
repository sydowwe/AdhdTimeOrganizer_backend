# Cross-slice seams

Every way one slice reaches another's rows, in one table. **All of it is declared here, in
`AdhdTimeOrganizer.Core/application/seam/` and `../event/`** — a slice never references another slice.
(The one exception in the whole solution is `Routines → TodoLists`, which is a real project reference
and not a seam.)

Two markers make the surface findable: `ISeam` on the interfaces, `ISeamEvent` on the events. Put the
cursor on either and ask for the type hierarchy — that is the list below, and `SeamWiringTests` fails
if the two ever drift apart.

## Interface seams (`ISeam`)

| Seam | Kind | Resolution | Implemented by | Consumed by |
|---|---|---|---|---|
| `IActivityLastDoneSource` | read | keyed `IEnumerable<>` | History (`activityHistory`), Planning (`plannerTask`), ActivityProfiles (`memoryAnchor`) | ActivityProfiles — `DrawLeisureSuggestionEndpoint` (F3-06): staleness from "last done" as well as "last shown", the soft unkept-plans demotion, and the card's "last time" |
| `IActivityMembershipSource` | read | keyed `IEnumerable<>` | TodoLists (`todoList`), Routines (`routineTodoList`) | **nobody** — its one consumer, History's `grid` grid, was deleted with `CQ-1`. Kept for the next one |
| `IActivityReferenceSource` | read **+ write**, in the caller's transaction | keyed `IEnumerable<>` | Core (`timerPreset`), History (`activityHistory`), TodoLists (`todoList`), Routines (`routineTodoList`), Planning (`plannerTask`), Tracking (`trackerMapping`), ActivityProfiles (`activityProfile`) | Core — `GridActivityEndpoint`, `GetByIdActivityEndpoint`, `MergeActivityEndpoint`, all through `IActivityReferenceService` |
| `IActivityRemovalParticipant` | read before, **any** work after the removal commits | keyed `IEnumerable<>` | TodoLists (`todoList`) — re-syncs the due reminders of the to-dos on the removed activities; Planning (`plannerTask`) — cancels the personal reminders cascaded away with the activity's planner tasks | Core — `DeleteActivityEndpoint`, `MergeActivityEndpoint`, through `IActivityRemovalService` |
| `IActivityTimeAttributionSink` | **write**, in the caller's transaction | single | History | Tracking — `DesktopActivityHeartbeatEndpoint` (`AttributeAsync`), `AndroidSyncEndpoint` (`AttributeSpanAsync`, FO9) |
| `ICalendarDayLookup` | read | single | Planning | History — `CalendarActivityEndpoint`; Routines — `RoutineSuggestedTodayJobHandler`, for the day's `WakeUpTime` |
| `IFocusIntervalSource` | read | single | History | Tracking — `DesktopMappingSuggestionsEndpoint` (FO9), which learns process → activity from timer focus phases |
| `IRoutineItemLoggedTimeSource` | read | single | History | Routines — `GetAllGroupedRoutineTodoListEndpoint`, `GetAllRoutineTodoListEndpoint`, `GetByIdRoutineTodoListEndpoint`, all through `RoutineItemTypicalDurationFiller`, for each item's `TypicalSeconds` (F2-06) |
| `IRunningTimerLookup` | read | single | History (pending `TimerBoundaryAlarm` rows) | Planning — `PlannerSlipNudgeJobHandler`, which holds the overrun nudge while the user has a timer running (audit N5) |
| `ITodoListItemLoggedTimeSource` | read | single | History | TodoLists — `GetDailyRecapTodoListItemEndpoint`; `GetEstimateFitTodoListItemEndpoint` (F3-01), over all days |
| `ITodoListItemPlanSource` | read | single | Planning | TodoLists — `GetAllTodoListItemEndpoint`, `GetByIdTodoListItemEndpoint`, through `TodoListItemPlannedSlotFiller`, for `NextPlannedSlot`; the same two plus `GetDashboardTodoListItemEndpoint` for `PlanHelp` (`PlanHistoryAsync`, FO3); `GetEstimateFitTodoListItemEndpoint` for each item's first block (`FirstPlannedSecondsAsync`, F3-01) |
| `IUserOwnedReferenceVerifier` | read | keyed `IEnumerable<>` | TodoLists (`todoListItem`), Routines (`routineTodoList`) | History — `Create`/`UpdateActivityHistoryEndpoint`; Planning — `Create`/`UpdatePlannerTaskEndpoint` and both apply-template endpoints, for `PlannerTask.TodolistItemId`. Both through Core's `IReferencedIdOwnershipService` |

## Event seams (`ISeamEvent`)

| Event | Published by | Handled by | Handler touches |
|---|---|---|---|
| `ActivityTimeRecordedEvent` | Tracking (heartbeat; Android sync for the user's today — FO9), History (both creates, via `IActivityTimeRecordedPublisher` — FO2) | host `ActivityTimeRecordedEventHandler` | Planning + TodoLists + Routines |
| `PlannerTaskIsDoneChangedEvent` | Planning, and the host handler above | host | TodoLists + Routines |
| `TodoListItemIsDoneChangedEvent` | TodoLists | **Planning**; **ActivityProfiles** (`ProjectShoppingDoneEventHandler`, FO13) | Planning only; ActivityProfiles only — a done shopping item makes its linked `NeedsShopping` project `ReadyToStart`. The two subscribers share no rows and need no ordering |
| `RoutineTodoListIsDoneChangedEvent` | Routines | **Planning** | Planning only |
| `ActivityArchivedChangedEvent` | Core (`SetArchivedActivityEndpoint`, on a real change) | **TodoLists** | TodoLists only — re-syncs the due reminders of the activity's to-dos |
| `CalendarDayTypesChangedEvent` | Planning — `UpdateCalendarEndpoint`, `BatchUpdateDayTypeCalendarEndpoint`, and the three time-away writes (F4-02), each only for the dates whose type really moved | **Routines** (`CalendarDayTypesChangedEventHandler`, FO1); **Planning** (`AwayDayOccurrenceWithdrawalHandler`, FO6) | Routines excuses closed cycles that are now mostly away (never un-excuses); Planning withdraws untouched auto-placed occurrences from today on. Disjoint rows, no ordering needed |

**A handler belongs to the slice it writes into, not to the host and not to the publisher.** The two
Planning-side handlers each touch only `PlannerTask` (and, for the to-do one, Planning's own
`IReminderRegistrationService`), so they moved out of the host into
`AdhdTimeOrganizer.Planning/application/eventHandler/`. The other two stay host-side because they
write into **several** slices in one `SaveChanges`, and `ActivityTimeRecordedEventHandler`
additionally enforces an *exclusive* rule — the to-do / routine fallback runs only when no planner
task matched — which independent subscribers cannot express under `Mode.WaitForAll`. Splitting it
would silently double-complete an activity holding both.

A handler in a slice needs that slice's assembly in the FastEndpoints `o.Assemblies` list in
`Program.cs`. A missing one is not a build error — the event simply publishes into nothing.

All six are published `Mode.WaitForAll` — the publisher blocks on the handler, so these are
synchronous in-process calls, not background work. Every handler runs **after** the publisher's own
commit, in its own scope, and logs-and-swallows rather than failing the request that raised it.

## Choosing between them

By **who owns the transaction and who owns the decision**:

- **Interface** when the caller needs a result back, or needs the work inside its own `SaveChanges`.
  `IActivityTimeAttributionSink` mutates the caller's `DbContext` and deliberately never saves — that
  is what keeps the attribution atomic with the raw tracking rows it came from.
- **Event** when the caller has already committed and the decision is genuinely someone else's.
  `ActivityTimeRecordedEvent` is the only shape that can invert a **write** dependency, which is why
  Tracking has no outbound slice edges.

Not the FastEndpoints **command bus**: one handler per type can't express the keyed fan-in
`IActivityMembershipSource` needs, a `DbContext` and a composable `IQueryable` don't belong in a
message envelope, and a missing handler throws deep inside a request instead of at endpoint
activation.

## Adding one

1. Declare the interface here, deriving from `ISeam` (or the record from `ISeamEvent` in `../event/`).
2. Implement it in the **owning** slice's own `application/seam/` folder, marked `IScopedService`.
   Never mark it `ISeam` for DI purposes — Scrutor registers `AsImplementedInterfaces()`, so that
   would make every seam resolvable as every other one.
3. Confirm the owning slice's assembly is in `ModuleServiceExtensions.ModuleAssemblies`.
4. Add a row above, and a case to `SeamWiringTests`.

## How each one fails

None of these break the build, and only one of them throws.

- **`IActivityMembershipSource` fails silently.** The consumer pattern is
  `FirstOrDefault(s => s.Key == key)` and, on `null`, returning the query **unfiltered** — so a slice
  dropped from `ModuleAssemblies`, a renamed key, or a second implementation claiming an existing key
  all just quietly stop narrowing. It has no consumer at present;
  `SeamWiringTests.ActivityMembershipSources_CoverEveryKey_Exactly` and
  `SeamWiringTests.TodoListMembershipSource_NarrowsToActivitiesOnAList` keep the sources honest until
  there is one again.
- **`IActivityReferenceSource` fails silently in two directions at once**, and it is the worst of the
  set. `ActivityReferenceService` folds whatever resolved into one `UNION ALL`; a slice dropped from
  `ModuleAssemblies` or a renamed key simply contributes nothing. On the **read** side `usageCount`
  comes back lower, so an activity with a year of history reads `Records: 0` and the settings table
  offers an enabled delete button. On the **write** side `POST /activity/merge` leaves that slice's
  rows pointing at activities the same request then deletes — and every activity FK in the solution is
  `Cascade`, so they are destroyed rather than orphaned, while the response still reports a
  plausible-looking count. `SeamWiringTests.ActivityReferenceSources_CoverEveryKey_Exactly` pins
  coverage; `ActivityMergeTests.Merge_RepointsEveryReferencingSlice` asserts on surviving rows in six
  projects rather than on `repointedCount`. **Its third silent mode is ambient query filters.** A
  source's subquery runs under whatever global filters its tables carry, and only the per-user one is
  safe to lean on — `AppQueryFilters.ActivityArchived` is mirrored onto the three `Activity*Profile`
  tables, so reading them unlifted hides an archived activity's profiles from its own `usageCount`.
  `canDelete` then comes back `true` on the one surface that lifts the archival filter for the delete
  (`DeleteActivityEndpoint`), and the cascade destroys the rows the count said were not there. The
  same hidden row makes `RepointUniqueProfileAsync`'s survivor-collision check read `false` and fails
  an entire merge on a unique index. The rule is on `ReferencingActivityIds` and each implementation
  applies it; it cannot be centralised, because by the time `ActivityReferenceService` sees the
  queryable it is an `IQueryable<long>` and `IgnoreQueryFilters` needs an entity type.
- **`IActivityLastDoneSource` fails silently in the quiet direction.** A dropped slice or renamed key
  contributes no evidence, and the draw falls back to staleness from "last shown" alone — three plausible cards,
  just ones that offer tonight what was done yesterday. `SeamWiringTests.ActivityLastDoneSources_CoverEveryKey_Exactly`
  pins coverage; `LeisureLastDoneTests` asserts each source's `lastDoneAt` on the card it reaches and the
  demotion's effect across seeds. The other way it goes wrong is reading *absence* as evidence: only a
  recording, a completed block or an anchor may move `LastDoneAt`, and an untouched block counts against an
  activity only after the latest of those.
- **`IFocusIntervalSource` fails silently if it widens.** Admitting stopwatch, manual or tracker-attributed
  rows looks like "more data", but tracker rows were produced by the very rules a suggestion creates, so the
  suggestions would start confirming themselves. `DesktopMappingSuggestionsTests` seeds a non-timer row and
  asserts it suggests nothing.
- **`IRunningTimerLookup` fails silently if it answers wrongly.** Answering "nobody" sends the overrun nudge over a
  running Pomodoro — the pre-seam behaviour, noisy but harmless; answering "everybody" (a dropped `BoundaryAt > now`
  predicate, which stale alarm rows would satisfy) silences every overrun nudge for good.
  `PlannerSlipNudgeJobTests` asserts both directions on sent notifications: a pending alarm holds the nudge, a past
  one does not.
- **The single seams throw at endpoint activation** if unregistered. That is the point of
  resolving them as a single service rather than an `IEnumerable<>`. `IActivityReferenceService` —
  the one face Core's endpoints inject over the keyed sources — is registered the same way and for the
  same reason.
- **`ITodoListItemLoggedTimeSource` has a failure mode registration cannot catch.** It reads
  `ActivityHistory.TodoListItemId`, the link stamped when a user saves a recording from a completed
  task. Widening it to "any time logged against this item's activity" — which looks like it would
  only ever find *more* — double-counts, because two to-do items may share one activity and would
  each be credited the same seconds. `TodoListItemDailyRecapTests.TimeIsAttributedByItem_NotByActivity`
  is the guard, and it asserts on the returned totals rather than on the query. Its second method,
  `LatestStartBetweenAsync`, keys on `ActivityId` **deliberately** — it answers "was the paired
  leisure activity started after the item was done", and that recording carries no item link.
  `TodoListItemDailyRecapTests.RewardTaken_OnlyByARecordingOfThePairedActivity_StartingAfterCompletion`
  guards it. Both methods take the day as UTC instants the caller resolved in the user's zone.
- **`IRoutineItemLoggedTimeSource` fails the same way as its to-do twin**, keyed on
  `ActivityHistory.RoutineTodoListId`. Widened to the activity, two routine items sharing one would each be
  credited the other's time, and so would every tracker-attributed row of that activity.
  `Endpoints.RoutineItemTypicalDurationTests.Time_IsKeyedOnTheItem_NotTheActivity` asserts on the returned
  figures with unlinked rows seeded in every cycle. It returns rows, not sums, because the cycles they fall
  into are the Routines slice's to define.
- **`ITodoListItemPlanSource` fails silently if it answers wrongly.** It is what "this to-do is
  scheduled" means since the planner stopped stamping `DueDate`; an implementation that dropped the
  status or the from-date predicate would report every item as scheduled forever (or none ever), and
  the list's "not scheduled" nudge would go quiet or never clear. `TodoListItemNextPlannedSlotTests`
  asserts on the returned slots — open blocks only, earliest first, nothing before the user's today.
  Its second method, `PlanHistoryAsync`, fails the same way in the other direction: a dropped day
  or background predicate marks items stuck that are not, and the list offers "a smaller first step"
  on a task planned for this afternoon. `TodoListItemPlanHelpTests` asserts on the token per seeded
  history (and one built by three real carry-overs). It also asserts the response carries no count.
  `PlanHistory.Postponed` (FO3 phase 2) sums the planner's `PostponeCount`, which is disjoint from
  `CarryOverCount` by construction — summing both is correct, and is what `FailedAttempts` does.
  Its third method, `FirstPlannedSecondsAsync` (F3-01), reads the **first-created** block per item. Reading
  the latest instead still answers with a plausible length — usually the leftover slot after a slip — so
  `TodoListItemEstimateFitTests.PlannedRatio_ReadsTheFirstBlock` seeds a short block and a later long one
  and asserts on the ratio.
- **`IUserOwnedReferenceVerifier` is the one that fails *closed*.** `ReferencedIdOwnershipService`
  looks a verifier up by key and answers `false` when none matches, so a slice dropped from
  `ModuleAssemblies` or a renamed key rejects every write carrying that id instead of waving them all
  through. That is the right direction for a security check and still an outage nothing logs: saving a
  recording from a completed to-do item starts 404ing for everyone, with a "To-do item not found."
  message that is true of no row. `SeamWiringTests.UserOwnedReferenceVerifiers_CoverEveryKey_Exactly`
  pins coverage; `ActivityHistoryReferencedIdOwnershipTests` asserts on rows, both directions, so a
  verifier that answers `true` for everything fails too.
- **Seam events fail silently in both directions**: an event with no handler is inert, and a handler
  that throws is logged and swallowed by design. Guarded by
  `SeamWiringTests.SeamEvents_AllHaveAHandler` plus behavioural tests on rows —
  `ActivityTimeAutomationTests`, not route smoke tests.
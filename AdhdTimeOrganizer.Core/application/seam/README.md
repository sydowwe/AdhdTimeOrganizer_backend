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
| `IActivityMembershipSource` | read | keyed `IEnumerable<>` | TodoLists (`todoList`), Routines (`routineTodoList`) | History — `GetFilteredTableActivityHistoryEndpoint` |
| `IActivityTimeAttributionSink` | **write**, in the caller's transaction | single | History | Tracking — `DesktopActivityHeartbeatEndpoint` |
| `ICalendarDayLookup` | read | single | Planning | History — `CalendarActivityEndpoint` |
| `ITodoListItemLoggedTimeSource` | read | single | History | TodoLists — `GetDailyRecapTodoListItemEndpoint` |

## Event seams (`ISeamEvent`)

| Event | Published by | Handled by | Handler touches |
|---|---|---|---|
| `ActivityTimeRecordedEvent` | Tracking | host `ActivityTimeRecordedEventHandler` | Planning + TodoLists + Routines |
| `PlannerTaskIsDoneChangedEvent` | Planning, and the host handler above | host | TodoLists + Routines |
| `TodoListItemIsDoneChangedEvent` | TodoLists | **Planning** | Planning only |
| `RoutineTodoListIsDoneChangedEvent` | Routines | **Planning** | Planning only |

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

All four are published `Mode.WaitForAll` — the publisher blocks on the handler, so these are
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

- **`IActivityMembershipSource` fails silently.** `ApplyMembershipFilter` does
  `FirstOrDefault(s => s.Key == key)` and, on `null`, returns the query **unfiltered** — so a slice
  dropped from `ModuleAssemblies`, a renamed key, or a second implementation claiming an existing key
  all just quietly stop narrowing the grid. `SeamWiringTests.ActivityMembershipSources_CoverEveryKey_Exactly`
  and `HistoryRouteSmokeTests.Grid_MembershipFilter_NarrowsThroughTheSeam` are the guards.
- **The three single seams throw at endpoint activation** if unregistered. That is the point of
  resolving them as a single service rather than an `IEnumerable<>`.
- **`ITodoListItemLoggedTimeSource` has a failure mode registration cannot catch.** It reads
  `ActivityHistory.TodoListItemId`, the link stamped when a user saves a recording from a completed
  task. Widening it to "any time logged against this item's activity" — which looks like it would
  only ever find *more* — double-counts, because two to-do items may share one activity and would
  each be credited the same seconds. `TodoListItemDailyRecapTests.TimeIsAttributedByItem_NotByActivity`
  is the guard, and it asserts on the returned totals rather than on the query.
- **Seam events fail silently in both directions**: an event with no handler is inert, and a handler
  that throws is logged and swallowed by design. Guarded by
  `SeamWiringTests.SeamEvents_AllHaveAHandler` plus behavioural tests on rows —
  `ActivityTimeAutomationTests`, not route smoke tests.

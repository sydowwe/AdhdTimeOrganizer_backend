# Vertical slicing — verification pass

> **⚠ This is an evidence record, not instructions.** The executable plan is
> `slicePrompts/` — one self-contained prompt per slice, in `slicePrompts/00-README.md` order.
> This file exists so the *proof* behind those prompts survives: the greps that established
> each seam, and what was measured rather than assumed. Where the two disagree, the prompts win.
>
> **Corrections since this was written (2026-08-10):**
> - **§3 is done.** The inverse collections were removed. The ⛔ in its heading is historical.
> - **Two dependency edges are missing from this document** and were found later:
>   `Planning → History` (`GetSuggestionsRepeatingPlannerTaskEndpoint` reads `ActivityHistory`)
>   and `History → TodoLists + Routines`
>   (`GetFilteredTableActivityHistoryEndpoint.cs:91-106` filters via `Any(...)` subqueries).
>   The graph below and its "two slice→slice edges" claim are therefore understated, and the
>   **Sequencing** section at the bottom is wrong — History must fall between Routines and
>   Planning. Use `slicePrompts/00-README.md` for ordering.
> - **`AdhdTimeOrganizer.Timers` is not happening.** Timers folds into Core.
> - The integration suite is green: **198 passed, 6 skipped, 0 failed.**

Read-only. Answers the four questions blocking the slice map, and corrects two claims I made
before checking.

## Verdict

The split is viable, and cheaper than expected — **but not along the folder structure**, and not
before one preparatory refactor. Two of my earlier assumptions were wrong.

---

## 1. `Routines → TodoLists` is acyclic ✅

Grepped `Routine(TimePeriod|ToDoList|TodoList|PeriodCompletion)` across all 59 files in
`application/endpoint/todoList/`. **Every hit is inside `routineTodoList/` or `routineTimePeriod/`.**
Zero hits in `todoList/`, `todoListItem/`, `todoListCategory/`, `taskPriority/`, `steps/`, or the
three shared bases.

`GetDashboardTodoListItemEndpoint` — the one I flagged as the likely offender — reads only
`TodoListItem`, filtered on `!IsDone && DueDate <= today+3`. No routine reference.

**So the chain works:** shared bases (`BaseTodoListItem`, `TodoListStep`, the six toggle/step/reorder
endpoint bases, `BaseTodoListConfigure`, `TodoListExtensions`) stay in `TodoLists`; `Routines`
references it; nothing points back.

## 2. `TodoListItem → PlannerTask` does not exist as a navigation ✅

`docs/domain-map.md` shows `TodoListItem ||--o| PlannerTask : "planned as"`, which read as
bidirectional. It isn't. `TodoListItem` has only `TaskPriorityId`/`TaskPriority`, `DueDate`,
`DueTime`, `TodoListId`/`TodoList`. The relationship is owned **entirely from the Planning side**:

```csharp
// PlannerTask.cs
public long? TodolistItemId { get; set; }
public virtual TodoListItem? TodolistItem { get; set; }
```

So at the entity level `Planning → TodoLists` is one-way. This was the risk I thought might collapse
the two slices into one; it doesn't.

What *is* bidirectional is the **completion fan-out**, and it already runs through events
(`PlannerTaskIsDoneChangedEvent`, `TodoListItemIsDoneChangedEvent`,
`RoutineTodoListIsDoneChangedEvent`) rather than direct references. Put the event records in `Core`
and the cycle is broken by construction — the mechanism you'd need is already in place.

## 3. ✅ *(resolved 2026-08-10)* `User` and `Activity` held inverse collections into every slice

This was the real obstacle, and I missed it in the sketch. **It has since been fixed** — see the
Sequencing note below for what landed. Kept here for the measurement.

`Core`'s two hub entities carry collection navigations pointing at **every** slice:

- **`User`** — `Calendar`, `ActivityList`, `CategoryList`, `RoleList`, `ActivityHistoryList`,
  `WebExtensionActivityEntryList`, `DesktopActivityEntryList`, `AndroidSessionDataList`,
  `TodoListItemColl`, `TodoListColl`, `TodoListCategoryColl`, `TaskPriorityList`, `PlannerTaskList`,
  `RoutineTodoListColl`, `RoutineTimePeriodList`, `Reminders`, `PlannerSettings`,
  `TrackerDesktopMappingByPatternList`, `TrackerAndroidMappingByPatternList`, `MemoryAnchors`.
- **`Activity`** — `TodoListItems`, `RoutineTodoLists`, `ActivityHistoryList`, `PlannerTaskList`,
  `TrackerDesktopMappingByPattern`, `TrackerAndroidMappingByPattern`, `MemoryAnchors`, the three
  profiles.
- **`ActivityRole` / `ActivityCategory`** also carry `TrackerDesktopMappingByPatternList` /
  `TrackerAndroidMappingByPatternList`.

As written, `Core` references every slice and every slice references `Core`. Total cycle. **No
project split is possible until these are removed.**

### The good news: they are nearly unused

I grepped every one of those collection names across the whole tree. **Cross-slice inverse
collections appear only inside EF entity configurations** — never in application code:

```
ActivityConfiguration.cs:17          builder.IsManyWithOneUser(u => u.ActivityList);
PlannerTaskConfiguration.cs:15-16    builder.IsManyWithOneUser(u => u.PlannerTaskList);
                                     builder.IsManyWithOneActivity(a => a.PlannerTaskList);
ActivityHistoryConfiguration.cs:16-17, ToDoListItemConfiguration.cs:21-22,
MemoryAnchorConfiguration.cs:14-15, RoutineToDoListConfiguration.cs:44-45,
DesktopActivityEntryConfiguration.cs:29, WebExtensionActivityEntryConfiguration.cs:27,
AndroidSessionDataConfiguration.cs:19, TodoListConfiguration.cs:14,
TodoListCategoryConfiguration.cs:14, RoleConfiguration.cs:15, CategoryConfiguration.cs:15,
ReminderConfiguration.cs:15, Tracker{Desktop,Android}MappingByPatternConfiguration.cs:21-27
```

The only **runtime** uses of any inverse collection are *within* a single slice:

| Use | Slice | Verdict |
|---|---|---|
| `TodoListResponse.cs:23-24` — `e.TodoListItemColl.Count()` | TodoLists → TodoLists | keep |
| `RoutineTodoListResetJob.cs:22,32` — `RoutineTimePeriodColl` | Routines → Routines | keep |
| `RoutinePeriodNudgeJob.cs:42,53` — same | Routines → Routines | keep |

> **Correction (found while applying the change):** one place *did* traverse a cross-slice collection
> — `GetFilteredTableActivityHistoryEndpoint.cs:87-103` filtered on `Activity.TodoListItems` and
> `Activity.RoutineTodoLists`. Rewritten as `dbContext.TodoListItems.Any(...)` subqueries, which EF
> translates to the same `EXISTS`. Everything else above held.

### The fix

`IsManyWithOneUser<TEntity>(navigationProperty?, deleteBehavior = Cascade)` already takes the
navigation **optionally** — the parameterless form configures the FK from the dependent side without
an inverse. Same for the `Activity` variant. So:

1. Delete the cross-slice collections from `User`, `Activity`, `ActivityRole`, `ActivityCategory`.
2. Change ~20 configuration call sites from `IsManyWithOneUser(u => u.XColl)` to `IsManyWithOneUser()`.
3. For the three hand-rolled ones (`TrackerDesktopMappingByPatternConfiguration:21,24`,
   `TrackerAndroidMappingByPatternConfiguration:20,23`), change `.WithMany(e => e.XList)` to
   `.WithMany()`.

**No schema change** — FK columns, names and cascade behavior are all unaffected. This is a pure
C#-side refactor that can land *today*, independently of any project split, and it makes the
split mechanical afterwards.

It also **reduces** `CQ-17` — `Activity.Clone()`'s `MemberwiseClone` now shares far fewer references —
but does **not** eliminate it: `Activity` keeps `MemoryAnchors` (a collection) plus the three profile
references, all still shared by a shallow clone. `CQ-17` stays open.

## 4. ⛔ Correction: `Tracking` is **not** self-contained

I said twice that tracking is the cleanest slice and "only touches `Activity` at read time for
attribution." That is wrong.

`DesktopActivityHeartbeatEndpoint.cs:129-152` runs an **automation feature**: on each heartbeat it
queries `PlannerTasks` for today, compares tracked seconds against the task's planned duration,
**mutates `PlannerTask.Status`** to `Completed`/`InProgress`, saves, publishes
`PlannerTaskIsDoneChangedEvent`, and — when no planner task matches — falls back to
`AutomateWithoutPlannerTaskAsync`, which reaches into `TodoListItems`.

So today `Tracking → Planning + TodoLists`, with writes, not just reads.

**Seam:** have Tracking publish something like `ActivityTimeRecorded(userId, activityId, secondsToday)`
and move the automation into a handler owned by `Planning`. That inverts the dependency, matches the
event pattern already used for the fan-out, and has the side benefit of pulling a chunk of business
logic out of an ingest endpoint. Until that lands, Tracking cannot be extracted.

## 5. The folder structure lies — do not slice mechanically

Several configuration files sit in a folder that does not match their entity's domain:

| File | Lives in | Entity actually belongs to |
|---|---|---|
| `configuration/activityHistory/DesktopActivityEntryConfiguration.cs` | History | **Tracking** |
| `configuration/activityHistory/WebExtensionActivityEntryConfiguration.cs` | History | **Tracking** |
| `configuration/activityHistory/AndroidSessionDataConfiguration.cs` | History | **Tracking** |
| `configuration/activityPlanning/TaskPriorityConfiguration.cs` | Planning | **TodoLists** |

`Calendar.cs` also sits at the entity root (`domain/model/entity/Calendar.cs`) rather than under
`activityPlanning/`. Any script that assigns files to projects by directory will misplace at least
these five.

---

## Resulting project graph

```
framework/Sydowwe.Framework                     (submodule, unchanged)
        │
AdhdTimeOrganizer.Core
   User · Activity + Role/Category/4 lookups/3 profiles/MemoryAnchor
   base shims · builder extensions · TimeDto · shared enums
   the cross-slice event records
        │
        ├── AdhdTimeOrganizer.Timers            ~20 files   (pilot)
        ├── AdhdTimeOrganizer.History           ActivityHistory + dashboards
        ├── AdhdTimeOrganizer.Tracking          ingest + mappings + dashboards   ⚠ needs §4 first
        ├── AdhdTimeOrganizer.TodoLists         lists · items · steps · priorities
        │      └── AdhdTimeOrganizer.Routines   periods · routine items · completions · 2 jobs
        ├── AdhdTimeOrganizer.Planning ────────► TodoLists   (PlannerTask.TodolistItemId)
        └── AdhdTimeOrganizer.Reminders ───────► Planning    (task-linked reminders)
        │
AdhdTimeOrganizer  (host)
   Program.cs · AppDbContext · migrations · DI · Serilog
   SuggestionPatternRefreshInterceptor  (spans Planning + History + Calendar)
   SuggestionPatternViewInstaller · the 3 pattern views
   DeleteUserAccountEndpoint · GetUserDataExportEndpoint  (touch everything by nature)
```

Acyclic. Two slice→slice edges (`Planning→TodoLists`, `Reminders→Planning`), both verified one-way,
both real FKs that keep their cascade and their global query filter.

> **⚠ Understated — two more edges were found later (2026-08-10), and neither is an FK:**
> - **`Planning → History`** — `GetSuggestionsRepeatingPlannerTaskEndpoint` reads
>   `ActivityHistory`.
> - **`History → TodoLists + Routines`** — `GetFilteredTableActivityHistoryEndpoint.cs:91-106`
>   filters on `dbContext.TodoListItems` / `dbContext.RoutineTodoLists` through `Any(...)`
>   subqueries. These are the subqueries §3's correction note introduced, so this edge is a
>   *consequence* of the inverse-collection fix, not something it missed.
>
> Still acyclic, but the ordering is forced:
> **TodoLists → Routines → History → Planning → Reminders → Tracking.**
> Also note `Tracking → History` (the heartbeat writes `ActivityHistory`), on top of the §4
> writes.

### Endpoint counts (verified)

| Slice | Endpoints |
|---|---|
| Activity + profiles + lookups (→ Core) | 78 |
| Planning | 44 |
| TodoLists | 33 + 6 shared bases |
| Routines | 20 |
| Tracking | 29 |
| History | 14 |
| Reminders | 5 |
| Timers | 10 |
| User/auth (→ host) | 34 |

## Sequencing

> **⚠ Superseded.** Only item 1 below is still accurate. The rest predates the two edges above
> and the decision to fold Timers into Core. **Use `slicePrompts/00-README.md`.**

1. ~~**Kill the inverse collections** (§3).~~ **DONE 2026-08-10** — 22 collections removed from
   `User` / `Activity` / `ActivityRole` / `ActivityCategory`, ~18 configuration call sites switched to
   the parameterless helpers, `GetFilteredTableActivityHistoryEndpoint` rewritten to subqueries. No
   schema change. Portal compiles clean.
2. **Pilot with `Timers`** — ~20 files, proves csproj + config contribution + DI marker scan +
   FastEndpoints assembly list + one migration round-trip.
3. **`Routines`** next — biggest correctness payoff (five 🟠 findings live there) and the seam is
   verified clean.
4. **`TodoLists`**, then **`Planning`**, then **`Reminders`**.
5. **`Tracking` last**, after the automation seam in §4 is built.
6. **`History`** whenever convenient — the only entanglement is the suggestion views, which stay
   host-side regardless.

## Still open

- **Seeder `Order` is global.** FK dependencies are expressed through a single ordering across all
  seeders, and truncation runs in reverse. Split across projects, slices can't choose `Order` in
  isolation — needs banded ranges per slice, defined before the first extraction.
- **Test projects.** `AdhdTimeOrganizer.IntegrationTests` must stay in the parent (it pins host
  composition), so this is a split, not a move: per-slice test projects plus a thin host-composition
  project.
- **Validators (62) and DTOs (198)** not yet individually assigned — they follow their slice, but
  shared request/response primitives (`TimeDto`, the `extendable`/`generic` DTO folders) need a
  home in `Core`.
- `application/eventHandler/` — the five handlers cross slices by nature. Probably host-side, or
  split per subscribing slice once the event records live in `Core`.
# Slice extraction prompts

One self-contained prompt per slice. Hand exactly one to an agent; it needs no other file
in this folder and no prior conversation.

**Run them in this order.** The order is forced by verified one-way dependencies — doing
them out of order produces a slice→host reference, which does not compile.

| # | Prompt | Depends on | Notes |
|---|---|---|---|
| ~~1~~ | ~~`01-core.md`~~ | — | ✅ **DONE.** `AdhdTimeOrganizer.Core` exists: 220 files, Timers folded in, seeder `Order` banded. |
| ~~2~~ | ~~`02-todolists.md`~~ | Core | ✅ **DONE.** `AdhdTimeOrganizer.TodoLists` exists: 90 files, `TaskPriority` pulled out of the Planning folder, `TodoListSettings` moved in, one FK constraint name pinned. |
| ~~3~~ | ~~`03-routines.md`~~ | TodoLists | ✅ **DONE.** `AdhdTimeOrganizer.Routines` exists: routine lists, time periods, completions, the reset service, two Quartz jobs, two seeders. `RoutineTodoListActivityMembershipSource` moved with the entity. |
| ~~4~~ | ~~`04-history.md`~~ | ~~TodoLists, Routines~~ → **Core only** | ✅ **DONE.** `AdhdTimeOrganizer.History` exists: 39 files. Its two outbound edges were *removed* rather than honoured — see below — so it landed before Routines. |
| ~~5~~ | ~~`05-planning.md`~~ | ~~TodoLists, History~~ → **Core only** | ✅ **DONE.** `AdhdTimeOrganizer.Planning` exists: 123 files, reminders folded in. **Both** of its predicted edges were deleted rather than honoured — see below. |
| 6 | `07-tracking.md` | Planning, TodoLists, Routines | Has a **seam to build first** — read the prompt. |

> **Planning landed with zero outbound slice edges. Neither predicted dependency was real.**
> - **`Planning → History` never existed.** `GetSuggestionsRepeatingPlannerTaskEndpoint` reads
>   `ActivityHistoryPattern` — the entity over the `mv_activity_history_pattern` materialized view —
>   **not** the `ActivityHistory` entity. That type was always host-side and moved into Planning with
>   its two siblings. The materialized view *is* the decoupling. `04-slicing-verification.md` and
>   `05-planning.md` both recorded this edge; both were wrong.
> - **`Planning → TodoLists` was deleted, not honoured.** `PlannerTask.TodolistItemId` keeps its FK
>   and its `SetNull`; the unused `PlannerTask.TodolistItem` **navigation** was removed, and the
>   relationship is declared in `AppDbContext.ConfigureCrossSliceRelationships` where both types are
>   visible. Empty migration. This is the third pattern for killing a slice→slice edge, after the
>   `IActivityMembershipSource` seam and the materialized view: **when the only thing forcing a
>   reference is a navigation property nothing reads, move the relationship to the host.** The host
>   owns the schema anyway.
> - Same silent-failure cost as the seam: delete the host-side declaration and the FK vanishes from
>   the model with no build error. `PlanningRouteSmokeTests.PlannerTaskToTodoListItem_ForeignKey_SurvivesTheSliceSplit`
>   asserts the FK, its column, its delete behaviour, its pinned constraint name, and the absence of
>   navigations.
>
> ⚠ **Trap this extraction hit, which the next one will hit too.** `ApplyHostConfigurations`'s final
> `ApplyConfigurationsFromAssembly(typeof(PlannerTaskConfiguration).Assembly)` *was the host's own
> scan* — anchored on a type that then moved into the slice, silently turning it into a second
> Planning scan and dropping every remaining host configuration from the model. No build error; the
> next `migrations add` emitted ~500 lines of table renames and dropped partition keys. It is now
> anchored on `typeof(AppDbContext).Assembly`, which cannot move. **Before extracting Tracking,
> check that no `ApplyConfigurationsFromAssembly`, `ModuleAssemblies` entry or FastEndpoints
> `o.Assemblies` entry is anchored on a type you are about to move.**

> **`06-reminders.md` was deleted on 2026-08-11 — reminders fold into Planning.** The prompt
> claimed `Reminders → Planning` was a one-way edge; it is bidirectional. `Reminder` carries an FK
> and cascade to `PlannerTask` and its service derives `RemindAt` from `Calendar.Date` +
> `StartTime` + `Status` (plus two `UserPlannerSettings` fields), while six planner-task endpoints
> inject `IReminderRegistrationService`. Separating them needs **two** Core seams, one of which
> abstracts "read this task's start time" for a single consumer and inherits the seam's silent
> failure mode on a notification path. `Reminder.RemindAt` is documented as a cache of the task's
> instant — the coupling is the design. `07-tracking.md` keeps its filename; only its position in
> this table moved.

> **⚠ The ordering above is weaker than it looks — try to delete an edge before obeying it.**
> History was supposed to wait for Routines because its grid filtered on to-do and routine
> membership. Instead the edge was inverted:
> `AdhdTimeOrganizer.Core/application/seam/IActivityMembershipSource.cs` declares "which activities
> are in your area?", the owning slice implements it, History resolves implementations by key from
> DI. Neither side references the other, and History's only project reference is Core.
>
> Reach for the same trick on any remaining *"slice A filters on slice B's rows"* edge before
> accepting a sequencing constraint. It does **not** apply to `Tracking → Planning`, which is a
> **write** (§4 of `../04-slicing-verification.md`) — that one still needs the event seam.
>
> The cost of the seam is that a source is resolved by *string key*, so a missing or misregistered
> implementation is silent: no build error, no exception, the filter merely stops narrowing. Every
> consumer of the seam needs a behavioural test asserting the rows, not just a route smoke test —
> `HistoryRouteSmokeTests.Grid_MembershipFilter_NarrowsThroughTheSeam` is the pattern.

## Baseline

`dotnet test` on `AdhdTimeOrganizer.IntegrationTests`: **228 passed, 6 skipped, 0 failed**
(after the History extraction). Any prompt that ends with a *lower* number has broken something.

> Each slice adds its own route smoke test, so this number goes **up** per extraction. Core took it
> from 214 to 216 (`CoreRouteSmokeTests`); TodoLists to 219 (`TodoListsRouteSmokeTests`, three
> `[Theory]` cases); History to 228 (`HistoryRouteSmokeTests` — six dashboard `[Theory]` cases, the
> grid, `form-select-options`, and the membership-seam behaviour test). Update this line and the
> per-prompt figures when you land a slice.
>
> ⚠ Two traps when writing a route smoke test, both hit during the History extraction: `GET
> /{entity}/{id}` answers **404 for a missing row**, indistinguishable from a missing route, so never
> assert `NotBe(NotFound)` on it; and a GET against a POST-only route answers 405, which passes
> `NotBe(NotFound)` for the wrong reason. Assert `Be(OK)` on a route you can actually satisfy, or
> POST with a real body.

> The 198 recorded here originally was already stale when written — `ActivityProfileGridTests` (14)
> and `PerUserDefaultMatcherTests` were added by commits `9f4bca7` / `b601637` / `064fada`, which
> land after that count. The pre-Core figure was **214 passed, 6 skipped, 0 failed**; the Core
> extraction added the 2 tests in `Endpoints/CoreRouteSmokeTests.cs` and changed no existing test
> beyond `using` lines.

## What the Core extraction changed for the remaining prompts

- Slice code takes a plain **`DbContext`**, never `AppDbContext` — that alias already exists in
  `ModuleServiceExtensions`. No `dbContext.SomeDbSet`; use `dbContext.Set<T>()`.
- Namespaces carry the project name: moved types are `AdhdTimeOrganizer.<Slice>.*`.
- A new slice project is a plain `Microsoft.NET.Sdk` library and therefore does **not** get the Web
  SDK's implicit usings. Copy the `<FrameworkReference>` + `<Using>` block from
  `AdhdTimeOrganizer.Core.csproj` or ~50 files fail on `ILogger<>` alone.
- `AppDbContext.ApplyHostConfigurations` now holds one `ApplyConfigurationsFromAssembly` call per
  project. Add yours; do not replace the existing ones.
- Seeder `Order` values are banded per slice — see
  `AdhdTimeOrganizer.Core/infrastructure/persistence/seeder/SeederOrderBands.md`. Stay in your band.
- `CoreRouteSmokeTests` is the template for the two registration traps (routes 404-ing, seeders
  double-registering); extend it per slice rather than writing a new pattern. Its seeder-duplication
  test already asserts over *every* registered seeder, so a new slice only needs the route half —
  `TodoListsRouteSmokeTests` is the example.
- **A non-empty migration may be a constraint *rename*, not a schema change.** Adding a slice adds an
  `ApplyConfigurationsFromAssembly` call, which changes the order entity configurations run in — and
  EF derives a relationship's constraint name from whether the principal's `ToTable` has already run
  when the FK is named. TodoLists hit this: the `PlannerTask` → `TodoListItem` FK flipped from
  `fk_planner_task_todo_list_items_…` (entity-set name) to `fk_planner_task_todo_list_item_…` (table
  name). The fix is `.HasConstraintName(...)` pinning the existing name at the relationship, which
  also makes it immune to the next reordering. Expect one or two more of these; check the diff rather
  than assuming a non-empty migration means you renamed a type.

## Evidence

`../04-slicing-verification.md` holds the measurements these prompts were derived from — the
greps that established each seam, and what was checked rather than assumed. It is an **evidence
record, not instructions**; where it disagrees with a prompt, the prompt wins. Its header lists
its own known-stale sections.

`05-slicing-playbook.md` was deleted on 2026-08-10. It was fully superseded by these prompts and
had gone actively wrong (it pilots a `Timers` project that is no longer happening, and gives a
slice order that does not compile). Don't restore it from history without re-reading it against
this folder.

## Deferred — decide during the slice that hits them

Carried over from the deleted playbook so they aren't lost. None of them block an extraction.

- **Per-slice test projects.** `AdhdTimeOrganizer.IntegrationTests` stays in the parent because
  it pins *host composition*, which is a property of the host. So the eventual breakup is a
  **split**, not a move: per-slice test projects plus a thin host-composition project. Every
  prompt in this folder assumes the single parent test project and a 216/6/0 baseline; revisit
  once the slices exist.
- **`application/eventHandler/`'s final home.** The five handlers cross slices by nature. The
  event *records* live in Core; the prompts keep the handlers host-side throughout. Either leave
  them there permanently or split them per subscribing slice — decide once Planning and
  Tracking have both landed, not before.
- **CQ-17 (`Activity.Clone()`).** `MemberwiseClone` still shallow-shares `MemoryAnchors` and the
  three `Activity*Profile` references. The inverse-collection refactor reduced the blast radius
  but did not close it. Tracked in `../02-findings.md`; it belongs to Core, so fix it there
  rather than inside a slice.
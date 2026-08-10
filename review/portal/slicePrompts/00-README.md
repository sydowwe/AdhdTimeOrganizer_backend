# Slice extraction prompts

One self-contained prompt per slice. Hand exactly one to an agent; it needs no other file
in this folder and no prior conversation.

**Run them in this order.** The order is forced by verified one-way dependencies — doing
them out of order produces a slice→host reference, which does not compile.

| # | Prompt | Depends on | Notes |
|---|---|---|---|
| ~~1~~ | ~~`01-core.md`~~ | — | ✅ **DONE.** `AdhdTimeOrganizer.Core` exists: 220 files, Timers folded in, seeder `Order` banded. |
| 2 | `02-todolists.md` | Core | Zero outbound edges. The de-facto pilot. |
| 3 | `03-routines.md` | TodoLists | Highest correctness payoff. |
| 4 | `04-history.md` | TodoLists, Routines | Filters into both. |
| 5 | `05-planning.md` | TodoLists, History | |
| 6 | `06-reminders.md` | Planning | Smallest slice, but blocked until Planning lands. |
| 7 | `07-tracking.md` | Planning, TodoLists, Routines | Has a **seam to build first** — read the prompt. |

## Baseline

`dotnet test` on `AdhdTimeOrganizer.IntegrationTests`: **216 passed, 6 skipped, 0 failed**
(after the Core extraction). Any prompt that ends with a *lower* number has broken something.

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
  double-registering); extend it per slice rather than writing a new pattern.

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
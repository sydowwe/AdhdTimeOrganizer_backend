# AdhdTimeOrganizer.TodoLists

> The to-do domain: lists, the items on them, the checklist steps inside an item, the categories that
> group lists, and the per-user priority ranking that orders everything.

## What it does

A to-do item is an activity you intend to do, with a rank, an optional due date and an optional
checklist. This slice owns that: **`TodoList`** groups items, **`TodoListCategory`** groups lists,
**`TaskPriority`** is the per-user ranking ("Today", "This week", "This month", "This year") that the
to-do view sorts and buckets by, and **`TodoListStep`** is the checklist inside an item.

Two features here are shaped specifically by the product's ADHD focus:

- **Temptation bundling** — an item can be paired with a leisure activity
  (`TodoListItem.PairedLeisureActivityId`), so the thing you want to do is attached to the thing you
  have to do.
- **The daily recap** — "what did I actually finish today, and how long did it take", answered from
  `CompletedTimestamp` plus the time History logged against each item.

It also owns the **shared to-do primitives that Routines builds on**: `BaseTodoListItem`,
`TodoListStep`, `BaseTodoListConfigure`, `TodoListExtensions`, `TodoListSettings`, and the
toggle / step / reorder endpoint bases. Routines → TodoLists is the solution's only outbound slice
edge, and it exists for these.

## Setup / running

No module-specific setup — see the root README. A few things worth knowing before running anything:

- This is a plain class library. It has **no migrations of its own**; every migration for its tables
  lives in the host (`AdhdTimeOrganizer/Migrations/`).
- Its tables live in the **`todo`** schema (`ModuleSchemas.TodoLists`). You never pass a schema to
  `ToTable` — a finalizing convention applies it.
- `TodoListSettings` is bound from configuration (`MaxItems`, `DisplayOrderGap`) in `Program.cs`.
  `DisplayOrderStart` is derived from the two, not configured.
- Seeders own the **100–199** band. Read
  `AdhdTimeOrganizer.Core/infrastructure/persistence/seeder/SeederOrderBands.md` before adding one.
- `TodoListItemCompletionInterceptor` is registered **host-side**, in `Program.cs`'s
  `AddInterceptors` call. Nothing in this project wires it up, and nothing fails loudly if that
  registration goes missing — the daily recap just comes back empty.

## Docs

- `summary.md` — **start here** if you're working in this project
- `domain-map.md` — model, invariants, business rules, navigation index
- `testing.md` — test strategy and which guards are load-bearing

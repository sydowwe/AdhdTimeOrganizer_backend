# AdhdTimeOrganizer.History

> The record of time actually spent: every stretch of an activity the user logged or that was tracked
> for them, plus the dashboards that read it back — and the alarms that ring when a timer phase ends.

## What it does

Everything else in the product is about intent — what you plan, what you mean to do, what a routine
says you should do. This slice is the only one that records **what actually happened**: an
`ActivityHistory` row is "you spent this many seconds on this activity, from here to here". Rows
arrive three ways: typed in by hand, saved from the prompt raised when a to-do or routine item is
completed, or attributed automatically by the desktop / web-extension / Android tracking heartbeat.

On top of that ledger sit the read surfaces: a filterable grid, an all-time per-activity aggregate,
and seven dashboard panels split into **detail** (one picked day/window) and **summary** (a date
range) — pie chart, stacked bars, summary cards, and a time-of-day fold that answers *when in the
day* a user's time sits.

The slice also owns **timer-boundary alarms**, which look unrelated but are not: a timer is a
recording about to happen. The browser's `setTimeout` cannot fire in a closed tab, so the client posts
the whole cycle's boundary instants on Start and the server pushes the notification. The countdown
itself stays client-side — see `summary.md`, this is the single most important thing not to
misunderstand about the feature.

## Setup / running

No module-specific setup beyond the solution's — see the root README. Four things are worth knowing
before you run or test anything here:

- **The tables live in the `history` schema** (`ModuleSchemas.History`). Hand-written SQL must say so;
  EF-generated SQL is qualified for you.
- **Migrations are not here.** This is a plain class library; every migration lives in the host
  (`AdhdTimeOrganizer/Migrations/`).
- **Saving an `ActivityHistory` row requires the three suggestion-pattern materialized views to
  exist**, or Postgres answers `42P01`. The app installs them at boot
  (`SuggestionPatternViewInstaller`); the test fixture applies `sqlScripts/*.sql`. If history work
  starts failing with `42P01`, that plumbing broke — not this slice.
- **The alarms need `services.AddHostedService<TimerBoundaryAlarmRegistrar>()` in `Program.cs`.**
  Nothing scans for it, nothing fails without it, and every alarm registered before a deploy just
  never fires.

Dev fixture data comes from `ActivityHistorySeeder` (per-user dev seeder, Order 300 — see
`AdhdTimeOrganizer.Core/.../seeder/SeederOrderBands.md`), which seeds 30 days of history per user.

## Docs

- `summary.md` — **start here** if you're working in this slice
- `domain-map.md` — the entities, indexes, endpoint inventory, invariants and cross-slice edges
- `testing.md` — test strategy and the guards that must not be deleted

# AdhdTimeOrganizer.Routines

> Recurring habits: to-do items that come back every period, the streak you build by keeping them,
> and the two leniencies (grace days, streak freezes) that stop one bad week from erasing months.

## What it does

A **routine time period** is a recurring window — "daily", "weekly", "monthly", whatever length and
anchor day the user picks. Routine to-do items live inside one. When the period rolls over, the items
un-tick themselves, the period's completion is scored against its threshold, and the streak either
extends, goes on grace, or breaks. Every elapsed period leaves a `RoutinePeriodCompletion` row, so the
user gets a real completion history rather than just a number.

The streak is the product here, so the module spends most of its complexity protecting it honestly:
**grace days** let you finish late, **streak freezes** let you skip a period outright from a small
budget that refills each calendar month, and a frozen period *carries* the run without extending it —
spending a freeze must never be worth more than actually doing the routine.

Two daily background sweeps do the unattended work: a 02:00 reset (addressed to the database) and a
09:00 nudge (addressed to a person — "3 of 8 done, resets tomorrow", and "your grace window lapses
today").

## Setup / running

No module-specific setup — see the root README. A few things worth knowing before you run anything:

- **Tables live in the `routines` schema** (`ModuleSchemas.cs` in the host maps this assembly to it).
  You never pass a schema to `ToTable`; a finalizing convention applies it. Hand-written SQL is *not*
  qualified for you.
- **Migrations live in the host** (`AdhdTimeOrganizer/Migrations/`), not here. This is a plain class
  library with no `AppDbContext` reference.
- **The two scheduled jobs only fire if `AddHostedService<RoutinesScheduledJobsRegistrar>()` is in
  `Program.cs`.** Dropping that line builds and boots fine; the jobs simply never run.
- **Seeders own the 200–299 band.** Read
  `AdhdTimeOrganizer.Core/infrastructure/persistence/seeder/SeederOrderBands.md` before adding one
  anywhere in the solution. `dev/RoutineTodoListSeeder` truncates; the per-user default seeder does not.

## Docs

- `summary.md` — **start here** if you're working in this project
- `domain-map.md` — model, invariants, business rules, navigation index
- `testing.md` — test strategy, the load-bearing guards, and the known gaps

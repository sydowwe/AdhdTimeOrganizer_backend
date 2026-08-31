# AdhdTimeOrganizer.Core

> The shared foundation every vertical slice sits on: the two hub entities (`User`, `Activity`), the
> activity roles and categories, the timer presets, and the seams the slices talk to each other
> through.

## What it does

An activity is the noun the whole product is built around — you plan one, track time against one,
tick one off a to-do list, build a routine out of one. Core owns that noun, its owner (`User`), and
the two lookups that classify it: an **activity role** (the life area it belongs to) and an optional
**activity category**. It also owns the timer presets, because a timer is just a duration pointed at
an activity and there is no timers feature area beyond that.

Everything else in the product — history, planning, to-do lists, routines, tracking, activity
profiles — is a slice that references Core. Core references none of them. What it offers instead is a
seam surface (`application/seam/`): interfaces and events declared here so a slice can reach another
slice's rows without either one naming the other.

## Setup / running

No module-specific setup — see the root README. Core is a plain class library with no migrations of
its own; every migration for its tables lives in the host (`AdhdTimeOrganizer/Migrations/`).

Two things a new contributor should know before running anything:

- Core's tables are split across **three** schemas — `user`, `activity`, and `public` (the timer
  presets). See `ModuleSchemas.cs` in the host.
- Core's seeders own the **010–099** band. `infrastructure/persistence/seeder/SeederOrderBands.md` is
  the contract for the whole solution — read it before adding a seeder anywhere, not just here.

## Docs

- `summary.md` — **start here** if you're working in this project
- `domain-map.md` — model, invariants, business rules, navigation index
- `testing.md` — how to test this project and which guards are load-bearing
- `../application/seam/README.md` — the cross-slice seam registry (every seam, who implements it,
  who consumes it, and how each one fails)

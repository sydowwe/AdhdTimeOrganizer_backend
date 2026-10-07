# AdhdTimeOrganizer.Planning

> Planning a day: the calendar, the four planner-task shapes, day templates, task importance — and
> the reminders that nudge you before any of it starts.

## What it does

A **calendar** row is one day of one user: its day type, its holiday name, its sleep window, its
notes. **Planner tasks** hang off it — a start time, an end time, an activity, an importance — and
the day view draws them on a timeline. Three other task shapes feed that one: a **repeating planner
task** describes a recurrence the user wants on matching days, a **template planner task** is a line
in a **day template** ("Office day", "Sick day") the user stamps onto whichever days they choose, and
the **suggestion read-models** notice patterns in what has actually been planned or logged and offer
them back.

Two numbers are computed from those rows rather than stored: the **day-plan completion streak** (the
flame on the home page — consecutive days where everything that mattered got done) and the
**plan-vs-actual trend** (how the user's estimates compared with their logged time over a range).
Neither has a column, a migration, or a repair path.

When a morning slips, **re-planning the rest of today** packs the missed and not-yet-started blocks
into the time left before bed, around whatever is fixed — previewed first, applied only on a tap, and
anything that will not fit goes to tomorrow or is skipped. The same rule answers "where is the next
free slot?" for a single block.

**Reminders** live here too rather than in a project of their own, because a reminder attached to a
planner task derives its instant from that task and has to be re-registered every time the task
moves. The actual scheduling — occurrences, dispatch, retries — belongs to the `Sydowwe.Reminders`
framework module, reached only through contracts.

## Setup / running

No module-specific setup — see the root README. Worth knowing before you run anything:

- **Tables live in the `planning` schema** (`ModuleSchemas.cs` in the host maps this assembly to it).
  You never pass a schema to `ToTable`; a finalizing convention applies it. Hand-written SQL is *not*
  qualified for you.
- **Migrations live in the host** (`AdhdTimeOrganizer/Migrations/`), not here. This is a plain class
  library with no `AppDbContext` reference.
- **The three suggestion read-models are materialized views**, and the view SQL, its installer, the
  refresh interceptor and the refresh job are all **host-side**. On a database where the views have
  never been installed, the two suggestion endpoints return nothing rather than failing.
- **Seeders own the 400–499 band.** Read
  `AdhdTimeOrganizer.Core/infrastructure/persistence/seeder/SeederOrderBands.md` before adding one
  anywhere in the solution. `CalendarSeeder` fills this year and next per user; the two `dev/`
  template seeders are fixtures.
- **Google Calendar sync is host-side**, not here — it carries a `Google.Apis.Auth` dependency that
  must not spread into a slice.

## Docs

- `summary.md` — **start here** if you're working in this project
- `domain-map.md` — model, invariants, business rules, navigation index
- `testing.md` — test strategy, the load-bearing guards, and the known gaps

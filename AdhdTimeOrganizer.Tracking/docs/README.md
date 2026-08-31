# AdhdTimeOrganizer.Tracking

> Automatic time tracking: what the desktop agent, the browser extension and the Android app saw the
> user actually doing — and the dashboards that turn those three raw ledgers into one picture of a day.

## What it does

Everywhere else in the product the user tells the app what they did. Here the app finds out on its
own. Three agents report in:

- the **desktop agent** posts a window of foreground/background seconds per process,
- the **browser extension** posts a per-minute window per domain,
- the **Android app** syncs finished app sessions.

Each lands in its own raw ledger. On top of that sit **pattern mappings** — user-authored rules like
"`rider64.exe` ⇒ *Programming*" or "`*.reddit.com` ⇒ ignore" — which resolve a tracked window to an
`Activity`, a role/category, or nothing at all. When a desktop window resolves to an activity, its
active seconds are attributed to the time ledger (History's `ActivityHistory`) and the day's totals
are announced, which is what lets a planner task or a to-do item complete itself without the user
ticking anything.

The read side is the larger half: per-source dashboards (pie chart, stacked bars, summary cards,
timeline, focus metrics, plus the per-source detail drill-downs) and the **unified** dashboards, which
merge all three ledgers into one non-double-counted day — the hard part, since an hour in a browser is
seen by the extension *and* by the desktop agent as `chrome.exe`.

## Setup / running

No module-specific setup beyond the solution's — see the root README. Five things are worth knowing
before you run or test anything here:

- **The tables live in the `tracking` schema** (`ModuleSchemas.Tracking`). Hand-written SQL must say
  so; EF-generated SQL is qualified for you.
- **Migrations are not here.** This is a plain class library; every migration in the solution lives in
  the host (`AdhdTimeOrganizer/Migrations/`).
- **Two of the ledgers are RANGE-partitioned tables.** The DDL comes from
  `PartitionedNpgsqlMigrationsSqlGenerator`, wired host-side in **both** `Program.cs` and
  `config/AppCommandDbContextFactory.cs`. Miss either and the next migration emits plain tables.
- **The ingest endpoints are reachable by extension clients only because they say so.** They carry
  `[AllowExtensionClients]` *and* `Policies(PortalAuthorizationPolicies.ActivityTracking)`; everything
  else here is web-only by the `Program.cs` configurator's default.
- **The retention purge needs `AddHostedService<TrackingScheduledJobsRegistrar>()` in `Program.cs`.**
  It re-pushes the 03:30-daily schedule on every boot (the Scheduler's RAM job store drops triggers on
  restart). Dropping the line builds fine and the purge simply never runs — and it carries a GDPR
  Art. 5(1)(e) control.

Dev fixture data comes from `WebExtensionDataSeeder` (per-user dev seeder, Order 600 — see
`AdhdTimeOrganizer.Core/.../seeder/SeederOrderBands.md`), which seeds seven days of browsing windows
per user. It truncates, which is why it is a dev seeder and not a default one.

## Docs

- `summary.md` — **start here** if you're working in this slice
- `domain-map.md` — entities, invariants, the dashboard contracts and the navigation index
- `testing.md` — test strategy and the guards that must not be deleted

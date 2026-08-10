# Modules

Registry of the modules in this solution. Each documents itself in its own `docs/` folder, with the
four-file convention described in [`document-module`](../.claude/commands/document-module.md):

- `README.md` — human-facing (setup, business intro)
- `summary.md` — **agent entry point** (read this first when working in a module)
- `domain-map.md` — model, invariants, business rules, navigation index (domain-rich modules)
- `testing.md` — test strategy + known gaps (when warranted)

Infra-shaped modules may keep their invariants inline in `summary.md` and skip `domain-map.md` — in
that case the summary says it is the single oracle ("summary-only").

> **Working in a module? Read its `summary.md` first.** It orients you and points to the navigation
> index in `domain-map.md` so you open only the files you need.

⚠ Every `framework/*` row below lives in the **`framework/` git submodule**, so its docs are
versioned in that repo, not this one — editing one is a two-repo operation. Only the portal's docs
are versioned here.

## Feature modules

| Module | Location | Owns | Docs |
|---|---|---|---|
| AdhdTimeOrganizer (portal) | `AdhdTimeOrganizer` | Activities + roles/categories, day planning (calendar, planner tasks, day templates, suggestions), to-do lists, routines + streaks, timers, desktop/web/Android time tracking, personal reminders, users/auth — **and** the composition root: `AppDbContext`, every migration, DI/Quartz/FastEndpoints wiring | ✅ ([summary](../AdhdTimeOrganizer/docs/summary.md) · [domain map](../AdhdTimeOrganizer/docs/domain-map.md) · [testing](../AdhdTimeOrganizer/docs/testing.md)) |
| Notifications | `framework/Sydowwe.Notifications` | Notification delivery (SignalR + Web Push + email), history, per-user preferences, quiet hours, push subscriptions | ✅ ([summary](../framework/Sydowwe.Notifications/docs/summary.md)) |
| Reminders | `framework/Sydowwe.Reminders` | Reminder/deadline registry, recurring scan for due occurrences, dispatch policy + digests, per-recipient snooze/dismiss | ✅ ([summary](../framework/Sydowwe.Reminders/docs/summary.md)) |
| Scheduler | `framework/Sydowwe.Scheduler` | Recurring-job registry, append-only run log, keyed dispatcher | ✅ ([summary](../framework/Sydowwe.Scheduler/docs/summary.md)) |

## Shared / infrastructure (not feature modules)

| Project | Role | Docs |
|---|---|---|
| `Sydowwe.Framework` | `framework/Sydowwe.Framework` — base entities, builder extensions, base endpoints, auth flows, persistence helpers, seeders, audit machinery | ✅ ([summary](../framework/Sydowwe.Framework/docs/summary.md) · [architecture](../framework/Sydowwe.Framework/docs/architecture.md)) |
| `Sydowwe.Framework.Contracts` | `framework/Sydowwe.Framework.Contracts` — the cross-module contract layer (`IScheduler`, `INotificationService`, `IQuietHoursReader`, `IReminderRegistry`, `ISubjectDataEraser`, the payload types). Contract types only: no services, no EF, no package references | TODO |
| `Sydowwe.Framework.Testing` | `framework/Sydowwe.Framework.Testing` — Postgres-container fixture, test base, role auth handler, base endpoint test classes | ✅ ([summary](../framework/Sydowwe.Framework.Testing/docs/summary.md)) |
| `Sydowwe.Scheduler.Xlsx` | `framework/Sydowwe.Scheduler.Xlsx` — opt-in XLSX export for the scheduler dashboard; the only project carrying a licensed dependency (Syncfusion), split out so the Scheduler core needs none | TODO |
| `AdhdTimeOrganizer.IntegrationTests` | Pins *this host's* composition and endpoints; stays in the parent repo because it is a property of the portal, not of the modules | — ([portal testing doc](../AdhdTimeOrganizer/docs/testing.md)) |

---

*Update a module's `Docs` cell from `TODO` to ✅ once its docs exist. See
[`document-module`](../.claude/commands/document-module.md) for what to write.*

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
| AdhdTimeOrganizer (portal) | `AdhdTimeOrganizer` | **The split is finished — the host owns no feature entities of its own.** What is left is users/auth (password login, 2FA, email confirmation + change, forgot/reset, refresh tokens, extension auth, sessions), the Google integrations (Sign-In, Calendar sync), user preferences, account deletion + the GDPR data export, and the two cross-slice event handlers — **and** the composition root: `AppDbContext` + `ModuleSchemas`, **every** migration in the solution (module entities live in the submodule; their migrations live here), the cross-slice relationship declarations, the suggestion-pattern views (installer, interceptor, queue, refresh job), `PortalScheduledJobsRegistrar`, and the DI / Quartz / FastEndpoints wiring | ✅ ([README](../AdhdTimeOrganizer/docs/README.md) · [summary](../AdhdTimeOrganizer/docs/summary.md) · [domain map](../AdhdTimeOrganizer/docs/domain-map.md) · [testing](../AdhdTimeOrganizer/docs/testing.md)) |
| Core | `AdhdTimeOrganizer.Core` | The two hub entities (`User`, `Activity`), activity roles + categories, timer presets, the closing base shims and configuration helpers, the three shared enums, the role/policy catalogs — **and** the whole cross-slice seam surface (`ISeam`, `ISeamEvent`) every slice talks through | ✅ ([README](../AdhdTimeOrganizer.Core/docs/README.md) · [summary](../AdhdTimeOrganizer.Core/docs/summary.md) · [domain map](../AdhdTimeOrganizer.Core/docs/domain-map.md) · [testing](../AdhdTimeOrganizer.Core/docs/testing.md)) |
| History | `AdhdTimeOrganizer.History` | The `ActivityHistory` ledger — the only record of time actually spent — its CRUD/filter/grid endpoints, the seven detail + summary dashboard panels, and the timer-boundary alarms that ring after the tab that scheduled them is gone | ✅ ([README](../AdhdTimeOrganizer.History/docs/README.md) · [summary](../AdhdTimeOrganizer.History/docs/summary.md) · [domain map](../AdhdTimeOrganizer.History/docs/domain-map.md) · [testing](../AdhdTimeOrganizer.History/docs/testing.md)) |
| TodoLists | `AdhdTimeOrganizer.TodoLists` | To-do lists, their items, the JSON-stored checklist steps, list categories and the per-user `TaskPriority` ranking — **plus** the shared to-do primitives Routines derives from (`BaseTodoListItem`, `TodoListStep`, `BaseTodoListConfigure`, the toggle/step/reorder endpoint bases). Also the completion stamp, temptation bundling and the daily recap | ✅ ([README](../AdhdTimeOrganizer.TodoLists/docs/README.md) · [summary](../AdhdTimeOrganizer.TodoLists/docs/summary.md) · [domain map](../AdhdTimeOrganizer.TodoLists/docs/domain-map.md) · [testing](../AdhdTimeOrganizer.TodoLists/docs/testing.md)) |
| Routines | `AdhdTimeOrganizer.Routines` | Recurring routines and the time periods that group them, streaks with their two leniencies (grace windows and budgeted streak freezes), the period completion history, per-user routine settings, and the two daily sweeps — the 02:00 reset and the 09:00 nudge — plus their notification producer. Holds the solution's **one** outbound slice edge, `Routines → TodoLists` | ✅ ([README](../AdhdTimeOrganizer.Routines/docs/README.md) · [summary](../AdhdTimeOrganizer.Routines/docs/summary.md) · [domain map](../AdhdTimeOrganizer.Routines/docs/domain-map.md) · [testing](../AdhdTimeOrganizer.Routines/docs/testing.md)) |
| Planning | `AdhdTimeOrganizer.Planning` | Planning a day: the calendar, the four planner-task types (plain, repeating, template, day-template), task importance, per-user planner settings, the three suggestion read-models over the pattern materialized views, `PlannerTemplateApplier` / `CalendarDayTypeRule` — **and personal reminders** (`Reminder`, `ReminderRegistrationService`), which are part of this slice rather than a project of their own (not to be confused with the `Sydowwe.Reminders` framework module below, which is the substrate they sit on). Reminder instants are *derived* from `Calendar.Date + PlannerTask.StartTime` in the user's zone, never authored. Seeder band **400–499** | ✅ ([README](../AdhdTimeOrganizer.Planning/docs/README.md) · [summary](../AdhdTimeOrganizer.Planning/docs/summary.md) · [domain map](../AdhdTimeOrganizer.Planning/docs/domain-map.md) · [testing](../AdhdTimeOrganizer.Planning/docs/testing.md)) |
| Tracking | `AdhdTimeOrganizer.Tracking` | Automatic time tracking: the three raw ingest ledgers (desktop agent, browser extension, Android app — two of them RANGE-partitioned), the two pattern-mapping lookups that resolve a tracked window to an `Activity`/role/category or ignore it, the 21 dashboards (15 per-source plus the six **unified** ones that merge all three ledgers into one non-double-counted day), and the GDPR retention purge over the ledgers. Reaches the rest of the solution only through two Core seams — `IActivityTimeAttributionSink` and `ActivityTimeRecordedEvent` — because its old dependencies were *writes*, not reads | ✅ ([README](../AdhdTimeOrganizer.Tracking/docs/README.md) · [summary](../AdhdTimeOrganizer.Tracking/docs/summary.md) · [domain map](../AdhdTimeOrganizer.Tracking/docs/domain-map.md) · [testing](../AdhdTimeOrganizer.Tracking/docs/testing.md)) |
| ActivityProfiles | `AdhdTimeOrganizer.ActivityProfiles` | The optional facets of an `Activity`: the three `Activity*Profile` entities (backlog, project, bucket list), the four per-user lookups they FK into, and `MemoryAnchor` — which is also what makes a bucket-list or one-time backlog entry *done*, since nothing stores that. Plus the leisure picker (`LeisureDrawRanker`, the seeded reproducible draw and its `LeisureSuggestionRecord` memory) and the weather signal — `WeatherFitRule` and `OpenMeteoWeatherProvider`, **the only outbound HTTP call any slice makes**. Zero outbound slice edges, and it needed no seam to get there | ✅ ([README](../AdhdTimeOrganizer.ActivityProfiles/docs/README.md) · [summary](../AdhdTimeOrganizer.ActivityProfiles/docs/summary.md) · [domain map](../AdhdTimeOrganizer.ActivityProfiles/docs/domain-map.md) · [testing](../AdhdTimeOrganizer.ActivityProfiles/docs/testing.md)) |
| Notifications | `framework/Sydowwe.Notifications` | Notification delivery (SignalR + Web Push + email), history, per-user preferences, quiet hours, push subscriptions | ✅ ([README](../framework/Sydowwe.Notifications/docs/README.md) · [summary](../framework/Sydowwe.Notifications/docs/summary.md) · [domain map](../framework/Sydowwe.Notifications/docs/domain-map.md) · [testing](../framework/Sydowwe.Notifications/docs/testing.md)) |
| Reminders | `framework/Sydowwe.Reminders` | Reminder/deadline registry, recurring scan for due occurrences, dispatch policy + digests, per-recipient snooze/dismiss | ⚠ partial ([README](../framework/Sydowwe.Reminders/docs/README.md) · [summary](../framework/Sydowwe.Reminders/docs/summary.md)) — summary-only, no domain map or testing doc |
| Scheduler | `framework/Sydowwe.Scheduler` | Recurring-job registry, append-only run log, keyed dispatcher | ⚠ partial ([README](../framework/Sydowwe.Scheduler/docs/README.md) · [summary](../framework/Sydowwe.Scheduler/docs/summary.md) · [domain map](../framework/Sydowwe.Scheduler/docs/domain-map.md)) — no testing doc yet |

## Shared / infrastructure (not feature modules)

| Project | Role | Docs |
|---|---|---|
| `Sydowwe.Framework` | `framework/Sydowwe.Framework` — base entities, builder extensions, base endpoints, auth flows, persistence helpers, seeders, audit machinery | ✅ ([README](../framework/Sydowwe.Framework/docs/README.md) · [summary](../framework/Sydowwe.Framework/docs/summary.md) · [domain map](../framework/Sydowwe.Framework/docs/domain-map.md) · [architecture](../framework/Sydowwe.Framework/docs/architecture.md)) |
| `Sydowwe.Framework.Contracts` | `framework/Sydowwe.Framework.Contracts` — the cross-module contract layer (`IScheduler`, `INotificationService`, `IQuietHoursReader`, `IReminderRegistry`, `ISubjectDataEraser`, the payload types). Contract types only: no services, no EF, no package references | TODO |
| `Sydowwe.Framework.Testing` | `framework/Sydowwe.Framework.Testing` — Postgres-container fixture, test base, role auth handler, base endpoint test classes. Not runnable on its own | ✅ ([README](../framework/Sydowwe.Framework.Testing/docs/README.md) · [summary](../framework/Sydowwe.Framework.Testing/docs/summary.md) · [testing](../framework/Sydowwe.Framework.Testing/docs/testing.md)) |
| `Sydowwe.Notifications.Testing` | `framework/Sydowwe.Notifications.Testing` — abstract test bases for the rules the Notifications module owns. Deliberately **not** part of `Sydowwe.Framework.Testing`, so a host that does not use the module never references it. Not runnable on its own | TODO |
| `Sydowwe.Scheduler.Xlsx` | `framework/Sydowwe.Scheduler.Xlsx` — opt-in XLSX export for the scheduler dashboard; the only project carrying a licensed dependency (Syncfusion), split out so the Scheduler core needs none | TODO |
| `AdhdTimeOrganizer.IntegrationTests` | Pins *this host's* composition and endpoints; stays in the parent repo because it is a property of the portal, not of the modules | — ([portal testing doc](../AdhdTimeOrganizer/docs/testing.md)) |

---

*`Docs` cell: ✅ = documented to the convention — all four files for a feature module, or, where
`domain-map.md` / `testing.md` don't apply, whichever of them do (the guide makes those two
conditional; `Sydowwe.Framework.Testing` is ✅ on three files for that reason). ⚠ partial = a file the
module *should* have is missing, and the cell says which. `TODO` = nothing yet. Update it as docs
land. See [`document-module`](../.claude/commands/document-module.md) for what to write.*

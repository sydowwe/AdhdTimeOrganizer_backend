# Modules

Registry of feature modules in this solution. Each module documents itself with
the four-file convention described in [`document-module`](../.claude/commands/document-module.md):

- `README.md` — human-facing (setup, business intro)
- `summary.md` — **agent entry point** (read this first when working in a module)
- `domain-map.md` — model, invariants, business rules, file index (domain-rich modules)
- `testing.md` — test strategy + known gaps (when warranted)

Infra-shaped modules (e.g. Scheduler, Reminders) may keep their invariants inline
in `summary.md` and skip `domain-map.md` — in that case the summary states it is the
single oracle ("summary-only"). `domain-map.md` is for domain-rich modules.

> **Working in a module? Read its `summary.md` first.** It orients you and points
> to the navigation index in `domain-map.md` so you open only the files you need.

A "module" is either a standalone Core project or a feature folder inside a
portal — the docs live in the module's own folder either way.

## Core feature modules

| Module | Location | Owns | Docs |
|---|---|---|---|
| Attendance | `MojaDigitalnaFirma.Core.Attendance` | Work logs, leave, leave balances, public holidays, attendance calendar | ✅ |
| Employee | `MojaDigitalnaFirma.Core.EmployeeModule` | Employees (pracovný pomer **and** dohody mimo pracovného pomeru), onboarding, equipment, termination/anonymization | ✅ |
| Inventory | `MojaDigitalnaFirma.Core.Inventory` | Stock items, batches, per-location levels, movement ledger, custom fields | ✅ |
| Majetok | `MojaDigitalnaFirma.Core.Majetok` | Fixed-asset register (DHM/DNM/DDHM), append-only lifecycle ledger, inventarizácia + period lock, depreciation input export / read-only schedule snapshot (no computation); **Integrácie producer** — `ICanonicalExportSource` for `AccountingJournal` (register-owned postings only) | ✅ ([summary](../MojaDigitalnaFirma.Core.Majetok/docs/summary.md)) |
| Notifications | `MojaDigitalnaFirma.Core.Notifications` | Notification delivery (SignalR + Web Push), history, per-user preferences, push subscriptions | ✅ |
| Partneri | `MojaDigitalnaFirma.Core.Partneri` | Business-partner master data (odberatelia / dodávatelia): identity (IČO/DIČ/IČ DPH), legal form, addresses, contacts, bank accounts, Peppol id, register-derived risk flags | ✅ ([summary](../MojaDigitalnaFirma.Core.Partneri/docs/summary.md)) |
| Registratúra | `MojaDigitalnaFirma.Core.Registratura` | Správa registratúry: registratúrny plán/poriadok, záznamy + spisy, append-only registratúrny denník, vyraďovacie/skartačné konanie, hash-chained tamper-evidence trail | ✅ |
| Scheduler | `MojaDigitalnaFirma.Core.Scheduler` | Generic time substrate: recurring-job registry, append-only run log, keyed dispatcher (infra + invocation, never job bodies) | ✅ ([summary](../MojaDigitalnaFirma.Core.Scheduler/docs/summary.md)) |
| Approvals | `MojaDigitalnaFirma.Core.Approvals` | Generic approval envelope: polymorphic request, append-only decision ledger, cross-module inbox + audit (thin primitive, not a workflow engine) | ✅ ([summary](../MojaDigitalnaFirma.Core.Approvals/docs/summary.md)) |
| Ochrana osobných údajov (GDPR) | `MojaDigitalnaFirma.Core.OchranaUdajov` | GDPR governance register: records of processing (RoPA), data-subject requests, consent, breaches, processors, authorized persons (governance & evidence, never the executor) | ✅ ([summary](../MojaDigitalnaFirma.Core.OchranaUdajov/docs/summary.md)) |
| Reminders | `MojaDigitalnaFirma.Core.Reminders` | When a notification fires on a schedule: deadline/reminder registry, recurring scan for due occurrences, dispatch policy + digests, dashboard, per-recipient snooze/dismiss (delegates the send to Notifications, the scan job to Scheduler) | ✅ ([summary](../MojaDigitalnaFirma.Core.Reminders/docs/summary.md)) |
| Vehicles | `MojaDigitalnaFirma.Core.Vehicles` | Vehicle register + Slovak kniha jázd (trip log), fuel records, odometer snapshots, driver-assignment history; §85n DPH + §5/§19 tax exports | ✅ ([summary](../MojaDigitalnaFirma.Core.Vehicles/docs/summary.md)) |
| Zmluvy | `MojaDigitalnaFirma.Core.Zmluvy` | Business/commercial contract register: contract card, append-only amendment (dodatok) ledger + lifecycle timeline, obligations + calendar, document metadata, Slovak lifecycle rules, reports/exports (build plan 01–05 complete) | ✅ ([summary](../MojaDigitalnaFirma.Core.Zmluvy/docs/summary.md)) |
| Cestovné náhrady | `MojaDigitalnaFirma.Core.CestovneNahrady` | Travel orders, settlements (stravné/krátenie/own-car/expenses/vreckové), effective-dated rate tables, payroll export; **Integrácie producer** — `ICanonicalExportSource` for `PayrollComponents` (the platform's first) | ✅ ([summary](../MojaDigitalnaFirma.Core.CestovneNahrady/docs/summary.md)) |
| Integrácie | `MojaDigitalnaFirma.Core.Integracie` | Exports & integrations adapter/transport layer: connector config, append-only transmission ledger, inbound e-invoices (maps canonical exports → vendor formats + e-Faktúra; no domain calc) | ✅ ([summary](../MojaDigitalnaFirma.Core.Integracie/docs/summary.md)) |

## External integrations

| Module | Location | Owns | Docs |
|---|---|---|---|
| Microsoft Integration | `MojaDigitalnaFirma.Integration.Microsoft` | Entra ID (Azure AD) SSO + delegated tokens, SharePoint/Graph document storage — the **implementation** of the Kernel `IDocumentStorage` / Entra contracts (the contracts live in `MojaDigitalnaFirma.Kernel`) | ✅ ([summary](../MojaDigitalnaFirma.Integration.Microsoft/docs/summary.md)) |

## Portal hosts & feature areas

| Module | Location | Owns | Docs |
|---|---|---|---|
| HBCleaning Admin Portal | `MojaDigitalnaFirma.HBCleaning.AdminPortal` | Composition host; apartment buildings + entrances, property managers, caretakers, cleaned companies, quotations, complaints, cleaning inspections, HB work-log override | ✅ ([summary](../MojaDigitalnaFirma.HBCleaning.AdminPortal/summary.md)) |
| Users / Identity | `MojaDigitalnaFirma.AdminPortal/…/user` | User accounts, auth, roles | TODO |

## Shared / infrastructure (not feature modules)

| Project | Role | Docs |
|---|---|---|
| `Sydowwe.Framework` | Base entities, builder extensions, base endpoints, audit, persistence helpers, identity/auth, seeders | ✅ ([summary](../framework/Sydowwe.Framework/docs/summary.md) · [architecture](../framework/Sydowwe.Framework/docs/architecture.md)) |
| `Sydowwe.Framework.Testing` | Postgres-container fixture, test base, role auth handler, base endpoint test classes | ✅ ([summary](../framework/Sydowwe.Framework.Testing/docs/summary.md)) |
| `MojaDigitalnaFirma.Kernel` | The cross-module **contract hub** every module stands on: `CoreUser`/`BaseEntityWithCoreUser` (`user/`), and the seam namespaces other modules consume instead of referencing each other — `notification`, `scheduling`, `reminders`, `export`, `storage` (`IDocumentStorage`), `approvals`, `gdpr`, `partneri`, `registratura` — plus the cross-module decoupling commands (`GetStockItemDisplayCommand`, `GetVehicleEntryPricesCommand`, `LinkVehicleToAssetCommand`), `IWordTemplateService`, `BusinessClock` | TODO |
| `MojaDigitalnaFirma.Core.EmployeeModule.Contracts` | Contracts-only sibling of the Employee module: the employee command/value-object/service-interface surface other modules may depend on **without** referencing the Employee impl assembly; holds the decoupling map/template (`decoupling/`). See [architecture §3](architecture.md) | ✅ ([summary](../MojaDigitalnaFirma.Core.EmployeeModule.Contracts/docs/summary.md)) |
| `MojaDigitalnaFirma.Core` | Shared product spine: `AppCoreDbContext` (aggregates every module's DbSets), `CoreUserEntityConfiguration`, concrete auth-endpoint closures, "me"/profile reads. **References every feature module** (see [architecture.md](architecture.md)) | TODO |

---

*Update a module's `Docs` cell from `TODO` to ✅ once its docs exist. See
[`document-module`](../.claude/commands/document-module.md) for what to write.*

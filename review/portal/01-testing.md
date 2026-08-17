# Portal review — 01 · Testing — open backlog

> This file previously carried a full narrative review (test-infra summary, coverage matrix, resolved
> items). That content is gone as of 2026-08-17 — most of it described work that is now done, and the
> full write-up isn't worth maintaining. What's left is just the open items, each with a self-contained
> prompt under `testingPrompts/` so it can be picked up without re-deriving context. `00-STATUS.md`,
> `02-findings.md`, `03-risks-rollout.md`, `04-slicing-verification.md` no longer exist — don't follow
> references to them from old commits.
>
> The original review covered 42 of 712 portal source files (~6%) before hitting a session limit —
> `application/endpoint/**`, `application/validator/**`, `application/dto/**`, and most of
> `infrastructure/persistence/configuration/**` / `seeder/**` were never reviewed. Absence of an item
> below is not a clean bill of health on that unreviewed surface.

## Open items

| ID | Gap | Prompt |
|---|---|---|
| `TEST-1` | Timer preset (`TimerPreset`, `PomodoroTimerPreset`) CRUD has validation coverage but no auth matrix — no test proves cross-user 404, anonymous rejection, or role gating | [TimerPresetAuth.md](testingPrompts/TimerPresetAuth.md) |
| `TEST-2` | Reminder CRUD + day view has partial edge coverage but no auth matrix | [ReminderAuth.md](testingPrompts/ReminderAuth.md) |
| `TEST-6` | `ActivityHistory` has full CRUD (Create/Update/Delete/GetById/Filter/FetchTable) with zero auth coverage — only dashboards/grid/aggregate routing and one membership-filter behavior are tested | [HistoryEndpoints.md](testingPrompts/HistoryEndpoints.md) |
| `TEST-7` | `TrackerDesktopMapping` / `TrackerAndroidMapping` CRUD has zero auth coverage — only dashboard/grid routing, the partition filter, and the retention job are tested | [TrackingEndpoints.md](testingPrompts/TrackingEndpoints.md) |
| `TEST-8` / `TEST-19` | Google sign-in + Google Calendar connect/disconnect/status — only 3 facts exist (`SyncCalendarToGoogleTests.cs`), no auth or failure-injection coverage | [GoogleServices.md](testingPrompts/GoogleServices.md) |
| `TEST-9` | User data export / account deletion — zero test files; highest-severity gap in this list (GDPR-relevant) | [UserDataExportAndAccountDeletion.md](testingPrompts/UserDataExportAndAccountDeletion.md) |
| `TEST-10` | Timer preset CRUD business logic (delete-cascade behavior, update semantics, GetAll scoping/ordering) beyond validation | [TimerCrud.md](testingPrompts/TimerCrud.md) |
| `TEST-15` | `AppDbContextFixture` reimplements suggestion-pattern view creation in parallel with the real `SuggestionPatternViewInstaller`, rather than calling it — drift risk, not just a missing test | [SuggestionPatternViewInstaller.md](testingPrompts/SuggestionPatternViewInstaller.md) |
| `TEST-18` | 12 per-user default seeders have their shared matcher unit-tested (`PerUserDefaultMatcherTests.cs`) but no seeder-level integration test (double-seed 23505 safety, per-user scoping) | [PerUserDefaultSeeders.md](testingPrompts/PerUserDefaultSeeders.md) |

## Also still open, no prompt written yet

- **`SEC-1`** — the Serilog/PII logging pipeline is untested.
- **Concurrency** — the `row_version` / `DbUpdateConcurrencyException` surface is untested outside the
  one case in `Endpoints/CompletionFanOutTests.cs`.

Both of these are cross-cutting rather than single-endpoint gaps and need scoping work before a prompt
can be written the same way as the items above.

# R1 — handoff: what is done, what is left

Worktree `.claude/worktrees/r1-notification-kinds`, branch `r1-notification-kinds` (from `dev` 6adae7d). Its
`framework/` is its own clone on branch `r1-notification-kinds`; that clone's `origin` is the main checkout's
`framework/`, so `git -C framework push origin r1-notification-kinds` moves the framework commits there locally.
Nothing is pushed anywhere yet. The prompt is `R1-notification-kinds-owned-by-the-app.md` (untracked here, copied
from the main checkout — do not commit it).

## Done (committed, both repos, framework first)

| Step | Parent | Framework | Verified |
|---|---|---|---|
| Phases 1–2: struct key, registry, kit, framework's 5 kinds | c54b8d4 | f73a3ac | phase 1 full suite 3643/0 failed |
| Phase 3 History | 7c6eccd | cfc82c5 | full suite 3647/0 failed (also covers phase 2) |
| Phase 3 Routines | 4a32218 | 4c41080 | targeted 626/0 |
| Phase 3 Planning | e31c48d | 5cea27f | targeted 830/0 |
| Phase 3 TodoLists | 60f577c | ce155f8 | build only |
| Phase 3 Tracking | fd28c28 | 0947355 | build only |
| Phase 3 host (MorningBriefing) | c829979 | 395f2d1 | targeted 1588/0 |
| Phase 4 code (see below) | WIP commit | WIP commit | **build only — tests not run yet** |

A full-suite run on the end of phase 3 (c829979) was started; its result is at the bottom of `artifacts-p3-full.log`.

Phase 4 code, committed as WIP: the nine business kinds deleted (payloads, renderer, words), the struct's temporary
members and `Known` gone, `NotificationTexts`/`Sk…`/`En…` deleted (`NotificationTextOptions` now maps locale →
`NotificationTextKit`), `NotificationChannelDefaults` deleted, `NotificationCatalogOptions` down to `UnraisedTypes`
+ `MaxActionsPerKind`, registry fallbacks gone, `ReminderRegistryService` now refuses a `NotificationType` no kind
defines (+ test `NotificationKindRegistryTests.ReminderRegistration_RefusesAnUnregisteredKind`), and
`framework/Sydowwe.Notifications/docs/HANDOFF-R1.md` (for MojaDigitalnaFirma) written.

## Left

1. **Run the tests on the phase-4 commit.** `./scripts/test.ps1` (full, ~15 min). If anything is red, fix and commit.
   Likely spots: `NotificationLocaleTests` (house-style kit now `EnNotificationTextKit`), anything that read
   `NotificationCatalogOptions.SupportedTypes`.
2. **Sort usings.** The repoint passes prepended `using` lines at the top of files (out of order). Files touched since
   c54b8d4: `git diff --name-only 6adae7d -- '*.cs'` in both repos. Order: `System*` first, then alphabetical.
3. **Stale references in comments** — grep and fix wording (no behaviour): `NotificationType\.\w+` (crefs to deleted
   members), `QuietHoursExemptTypes`, `NotificationChannelDefaults`, `HeldWhileAwayTypes`, `PushOffByDefault`,
   `AdminOnlyTypes`, `SupportedTypes`, `NotificationTexts`, `PortalNotificationTexts`, `AddNotificationVocabulary`.
   Known spots: `NotificationService.cs` (BuildPushPayload doc), `NotificationSubject.cs`, `IReminderRenderer.cs`
   ("a generic/passthrough NotificationType to add in the Notification module"), `JobAlertKind.cs`,
   `INotificationTextRenderer.cs`, `TakeNotificationActionEndpoint.cs`, `GetNotificationChannelAvailabilityEndpoint.cs`,
   `BaseNotificationVocabularyTests.cs` (doc + failure message), `NotificationCatalogTests.cs` / `NotificationVocabularyTests.cs`
   docs ("sixteen of twenty-seven", "AddNotificationVocabulary").
4. **Docs to rewrite** (prompt, *Docs to rewrite*): `framework/Sydowwe.Notifications/docs/summary.md` (Kinds, Quiet
   hours, Held while away, Catalog, Extension playbook) + `domain-map.md`; `review/notifications-opportunities/_SHARED-CONTEXT.md`
   → *Adding a NotificationType — the full checklist* becomes: kind class + payload + key consts in the owning slice
   (`<slice>/application/notification/`), the second copy in `NotificationCatalogTests`, a row in
   `NotificationKindPolicyTests.Expected`, frontend `notificationTypeMeta`. `docs/claude/slices.md` if it names the
   enum; each slice's `docs/summary.md` that names `NotificationType.X` or the payload's old location.
   **`review/notifications-opportunities/README.md` — leave it**: the main checkout has uncommitted edits to it, a
   change here would conflict on rebase; tell the user what to change (*Parallelising*, *What each new kind costs*).
5. Full suite again, commit (framework first, then parent with the gitlink), push the framework branch to the main
   checkout's framework (`git -C framework push origin r1-notification-kinds`) so the parent's gitlink resolves there.
6. Clean up: `artifacts-r1/` and `artifacts-*.log` in the worktree root (not gitignored).

## Decisions taken (report to the user)

- All four open decisions: the prompt's recommended option.
- Kinds' base class lives in Contracts (slices still reference only Core + framework). Words live on the kind; the
  shared `NotificationTextKit` (quotes, instants, `Duration`, `Plural`, fallback, button labels, mail chrome) is
  what a host can still override via `NotificationTextOptions.Use`. A host can no longer reword a framework kind's
  own sentence.
- **Visible change:** the settings matrix is in key order (ordinal) instead of enum order — the client renders the
  server's order. Pinned by `BaseNotificationCatalogTests.Mine_ListsKindsInKeyOrder`.
- `QuietHoursExemptTypes` was deleted in the History step (its only member moved).
- Frontend optional clean-up (prompt, *Frontend*) not done.

## Tooling notes

- Build privately: `dotnet build AdhdTimeOrganizer.IntegrationTests --artifacts-path artifacts-r1`, run
  `.\artifacts-r1\bin\AdhdTimeOrganizer.IntegrationTests\debug\AdhdTimeOrganizer.IntegrationTests.exe -class '*Notification*' …`
  (xunit wildcards only at the ends).
- Migration gate (only needed if an entity changes): set `DB_HOST/PORT/USER/PASSWORD/NAME` to placeholders,
  `FIELD_ENCRYPTION_KEY` to the test fixture's key, `UseArtifactsOutput=true`, `ArtifactsPath=<worktree>\artifacts-r1`,
  then `dotnet ef migrations add R1Probe …`, check `Up()` is empty, delete it, `git checkout` the snapshot.
- Don't edit source while `test.ps1` is still in its build step; wait for `Discovered:` in its log.

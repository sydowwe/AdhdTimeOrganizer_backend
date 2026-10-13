# P3-07 — Account deletion with a grace period

> Paste into a fresh session. Read `review/notifications-opportunities/_SHARED-CONTEXT.md` first. Backlog #13.
> Host + frontend. Account lifecycle more than notifications: the only notification is one transactional email.

## Why

Deleting an account on impulse is the textbook ADHD regret, and today it's immediate and irreversible. Requiring the
password and a 2FA code slows it down but doesn't make it reversible.

## Facts

- `DELETE user/account` is `DeleteUserAccountEndpoint` (`AdhdTimeOrganizer/application/endpoint/user/command/settings/`)
  over `BaseDeleteUserAccountEndpoint` (framework). It's gated by `VerifyUserPreProcessor` (password + 2FA),
  revokes sessions through `ISessionRevocationService`, and has `Before`/`AfterDeleteAsync` hooks.
- **The one deletion path** is `IUserAccountDeletionService` (`AdhdTimeOrganizer/application/service/user/UserAccountDeletionService.cs`):
  module erasure over `ISubjectDataEraser`, then `UserManager.DeleteAsync`, committed as one unit.
- Purge-job precedent: `AdhdTimeOrganizer/infrastructure/jobs/UnconfirmedAccountJobHandler.cs` (P2-05). It runs
  hourly with a batch cap, gives each account its own scope, marks with `ExecuteUpdate` and logs ids only.
- "What you'll lose" is already served by `GetAccountDeletionSummaryEndpoint`.
- **`User.IsActive` isn't enforced anywhere.** Sign-in ignores it, and only `MorningBriefingJobHandler` filters on it,
  so "deactivate during grace" would stop neither.
- Sign-in paths: `LoginUserEndpoint` → `ValidateTwoFactorAuthForLoginWebEndpoint`
  (`BaseValidateTwoFactorAuthForLoginEndpoint.OnAuthenticatedAsync`), `GoogleSignInEndpoint`, and the tracker login
  `ExtensionValidateTwoFactorAuthForLoginEndpoint`.
- Mail: transactional, through `IUserEmailSenderService` / `ISecurityAlertService` (P1-05), never `NotifyAsync`. The
  email-change revert link (`SecurityAlert.RevertToken`) is the precedent for a link that reverses an account action.
- Portal kinds mail only when the user opted in (`NotificationChannelDefaults`). The admin-only Scheduler kinds are the
  exceptions.

## Design

1. `DELETE user/account` (still password + 2FA) **schedules**: `User.DeletionScheduledFor DateTime?` = now + grace.
   In the same request:
   - revoke every session, trackers included;
   - **delete the user's push subscriptions**, so nothing can buzz during the grace period without touching any
     producer;
   - send the mail: "deleted on <date>; keep it with this link or by signing in".
2. **Undo**, either way, nulls the column:
   - The mail link: single-use, valid until the deletion, unauthenticated, revert-token pattern.
   - Signing in on the web: show a "scheduled for deletion — keep my account?" step rather than cancelling silently.
     The user may be signing in only to export their data.
   - Tracker logins are **refused** during grace, because a tracker can't show that step.
   - After an undo, push re-subscribes on the next app open (verify in `UsePushNotifications.ts`).
3. **Purge:** a job (copy `UnconfirmedAccountJobHandler`) deletes accounts past `DeletionScheduledFor` through
   `IUserAccountDeletionService.DeleteAsync`. The state is a column, not a `RunOnceAt` trigger, so it survives
   restarts with no boot registrar.
4. **During grace**, background sweeps still compute for the user. With no sessions and no push subscriptions that's
   harmless, except for email kinds the user opted into. Either accept that for the grace length, or add a
   recipient-suppression seam the Notifications module consults (decision).
5. The privacy text and the confirm dialog state the date. GDPR Art. 17 says "without undue delay"; a short grace the
   subject chose, with a stated date, is the usual reading. Log the decision.

## Frontend

- The confirm dialog says when the account will be deleted.
- Settings shows the pending state with "keep my account".
- The web sign-in flow handles the scheduled state.

## Tests

- Scheduling deletes nothing.
- Sessions are revoked and push subscriptions are gone.
- The mail went out (fake sender) with a working token.
- The token undoes once and is rejected the second time.
- Web sign-in offers to keep; a tracker login is refused.
- The purge deletes after the grace through the shared path, with module rows erased (copy the assertions in
  `UserDataExportAndDeletionTests`), and skips a cancelled schedule.
- The seeded root admin is treated as the immediate delete treats it today. Check that first; this wasn't
  verified.

## Open decisions

1. Grace length: 7 days, or 14 to match P2-05?
2. Signing in: prompt to keep (recommended), or cancel automatically?
3. Opted-in emails during grace: accept, or add the suppression seam?

# P2-05 — Unconfirmed accounts: remind once, then purge

> Paste into a fresh session. Read `review/notifications-opportunities/_SHARED-CONTEXT.md` first.

## Facts

- `AdhdTimeOrganizer/config/IdentityServiceExtensions.cs:128` — `options.SignIn.RequireConfirmedEmail = true`. An
  unconfirmed account **cannot sign in**.
- `ResendEmailConfirmationEndpoint` exists but only the user can trigger it. Nothing reminds, and nothing deletes.
- So every abandoned sign-up keeps an email address (and whatever the per-user default seeders created at
  registration) forever, for an account that can never be used. That is a GDPR storage-limitation problem, not just
  clutter.

## Build

One daily host job (`AdhdTimeOrganizer/infrastructure/jobs/`, `PortalScheduledJobsRegistrar`):

1. **+24 h, once:** resend the confirmation link — generate the token the way `ResendEmailConfirmationEndpoint` does,
   send with `IUserEmailSenderService.SendConfirmationLinkAsync`. Mark it (new column, e.g.
   `User.ConfirmationReminderSentAt`). Transactional — the person asked for the account.
2. **+N days (decision: 14 or 30):** delete the account through **the same path as account deletion** — extract
   `DeleteUserAccountEndpoint`'s `ISubjectDataEraser` fan-out + `UserManager.DeleteAsync` into a service the job can
   call, so the per-user defaults and module rows go with it exactly as they do for a self-deletion.
3. Exclude Google sign-in accounts if they are created confirmed (verify), and seed / test users
   (`ISeedUserProvider`).
4. The registration instant: `User` is an `IBaseTableEntity`, so it should carry a created timestamp — verify which.
5. Log by `{UserId}` only.

## Tests

An unconfirmed user at +25 h gets exactly one mail, marked; at +N days the user row and its per-user data are gone (use
`UserDataExportAndDeletionTests`' inventory to assert nothing is left); a confirmed user is never touched.

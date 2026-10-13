# P1-05 — Security emails

> Paste into a fresh session. Read `review/notifications-opportunities/_SHARED-CONTEXT.md` first.
> **Mostly a framework change** — two-repo operation.

## Why

When a password, email address or 2FA setting changes, the account owner is never told. If that change wasn't them,
they have no way to find out. Every mainstream service sends these mails; they are transactional (no consent
needed) and must not be mutable.

## Facts (verified 2026-10-09, framework at 5646e89)

- The only account mails: `IUserEmailSenderService`
  (`framework/Sydowwe.Framework/domain/extServiceContract/user/IUserEmailSenderService.cs`) —
  `SendConfirmationLinkAsync`, `SendEmailChangeConfirmationAsync`, `SendPasswordResetLinkAsync`,
  `SendPasswordResetCodeAsync`. Templates in `framework/Sydowwe.Framework/infrastructure/templates/email/`
  (`ConfirmEmail.html`, `ResetPassword.html`, `ResetPasswordCode.html`).
- The host endpoints are thin subclasses of framework bases, and **only one base has a hook**:

| Host endpoint (`AdhdTimeOrganizer/application/endpoint/user/command/…`) | Framework base | Hook |
|---|---|---|
| `settings/ChangePasswordEndpoint` | `BaseChangePasswordEndpoint` | `AfterPasswordChangedAsync(TUser)` (`:57`) |
| `auth/forgotPassword/ResetPasswordEndpoint` | `BaseResetPasswordEndpoint` | none |
| `settings/2fa/ToggleTwoFactorAuthenticationEndpoint` | `BaseToggleTwoFactorAuthEndpoint` | none |
| `settings/2fa/ResetTwoFactorAuthEndpoint` | `BaseResetTwoFactorAuthEndpoint` | none |
| `settings/2fa/RegenerateTwoFactorAuthRecoveryCodesEndpoint` | `BaseRegenerateRecoveryCodesEndpoint` | none |
| `settings/email/ConfirmEmailChangeEndpoint` | `BaseConfirmEmailChangeEndpoint` | none |

## Build

1. **One method, one template, a kind enum** in the framework:
   `IUserEmailSenderService.SendSecurityAlertAsync(TUser user, SecurityAlertKind kind, string? toAddress = null)`,
   kinds `PasswordChanged`, `PasswordReset`, `EmailChanged`, `TwoFactorDisabled`, `TwoFactorReset`,
   `RecoveryCodesRegenerated` (later `NewDeviceSignIn`).
2. **Send from the framework bases after success**, so every host gets it; give hosts an opt-out option rather than an
   opt-in. (The alternative — add `After…Async` hooks and call from the host subclasses — leaves the next host
   without security mail by default.)
3. **Email change goes to the OLD address.** Capture it in `BaseConfirmEmailChangeEndpoint` *before* the change is
   applied. The mail may show the new address in full — it goes to the account owner, and if an attacker made the
   change, the victim needs to see it. Never log either address.
4. Every mail says what changed, when, and "If this wasn't you" → forgot-password (which revokes every session via
   `RevokeAllSessionsAsync`).
5. **Not through `NotifyAsync`.** Preferences, quiet hours, the away hold and the catalog must not apply, and bell rows
   are purgeable and dismissable. (Optionally *also* a bell row — that would need a kind; skip for now.)
6. A send failure must not fail the operation: log by `{UserId}`, as the notification email channel does.
7. **Check the locale:** notification text follows `User.Locale` (B15); check whether `UserEmailSenderService`'s
   templates do, and match it.

### Later: new-device sign-in

Needs a device identity on the session. Check what `RefreshToken` stores today (session id and started-at exist; FO7
marks trackers; a user agent is **not verified**). Exclude tracker sign-ins.

## Tests

Find how existing tests observe sent mail (start from the forgot-password / register tests) and reuse that fake. For
each kind: exactly one mail to the right address; the old address for an email change; the operation still succeeds
when the sender throws.

## Open decisions

1. Default-on in the framework with a host opt-out (recommended) vs host opt-in.
2. Whether to add a bell row as well.

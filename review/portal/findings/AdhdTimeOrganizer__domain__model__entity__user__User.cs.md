# Review: AdhdTimeOrganizer/domain/model/entity/user/User.cs
Role: entity
Summary: Portal's Identity user entity is structurally sound (correct closing-shim pattern, Timezone inherited from BaseUser), but stores a Google Calendar OAuth refresh token in plaintext and neither it nor GoogleOAuthUserId carry `[AuditIgnore]`.
Coverage: n/a

## Issues
- [High][Security] AdhdTimeOrganizer/domain/model/entity/user/User.cs:21 — `GoogleCalendarRefreshToken` is a long-lived Google OAuth credential (calendar read/write scope) stored as a plain `string`/`nvarchar(500)` column (see `UserEntityConfiguration.cs:16`) with no `EncryptedColumn()`.
  Why: A DB read (backup leak, SQL injection, insider access, or a lower-trust reporting replica) hands an attacker a working Google Calendar token for the user, not just an app-internal secret — this is exactly the "high-sensitivity string" case `EncryptedColumn` exists for per CLAUDE.md.
  Fix: Switch `builder.Property(u => u.GoogleCalendarRefreshToken)` to `builder.EncryptedColumn(u => u.GoogleCalendarRefreshToken)`; note the column then can't be filtered/sorted, which is fine since it's only ever read by user id (`SyncCalendarToGoogleEndpoint.cs`, `ConnectGoogleCalendarEndpoint.cs`).
  Confidence: High

- [Medium][Security] AdhdTimeOrganizer/domain/model/entity/user/User.cs:20-21 — Neither `GoogleOAuthUserId` nor `GoogleCalendarRefreshToken` carry `[AuditIgnore]`.
  Why: CLAUDE.md notes auditing exists but isn't wired today — however the attribute should be correct now so that turning the interceptor on later doesn't start writing a raw OAuth refresh token and Google account id into `audit_log`/`ChangedProperties` snapshots by default.
  Fix: Add `[AuditIgnore]` to both properties (and to `PasswordHash`-adjacent sensitive fields if this class is ever audited) ahead of the interceptor being enabled.
  Confidence: Med

- [Low][Quality] AdhdTimeOrganizer/domain/model/entity/user/User.cs:28-32 — `PhoneNumber`/`PhoneNumberConfirmed` are re-overridden with `[NotMapped]` even though `BaseUser` already declares the same overrides with the same attribute.
  Why: Redundant override; if `BaseUser`'s implementation ever changes (e.g. adds a private backing field) this shadow could silently diverge without a compiler error.
  Fix: Drop the duplicate override here unless there's a reason (e.g. Identity's `UserManager` binding) that isn't documented in this file — if there is one, a short comment would help the next reader.
  Confidence: Low

- [Low][Security] AdhdTimeOrganizer/domain/model/entity/user/User.cs:20 — `GoogleOAuthUserId` (the user's Google account identifier) is also unencrypted, lower sensitivity than the refresh token but still a stable cross-service identifier for the user.
  Why: Combined with the refresh token above, a DB leak fully de-anonymizes and authorizes against the user's Google account.
  Fix: Optional — lower priority than the refresh token; consider `EncryptedColumn` only if this ID is treated as sensitive PII under the app's threat model.
  Confidence: Low

No other issues found — `Timezone` is present (inherited from `BaseUser`, required, `TimeZoneInfo.Id`-converted) so routine rollover has what it needs; no 2FA secret fields are declared here (Identity's `IdentityUser<long>` base handles those, not customized in this file); no response DTO in `application/dto` was found projecting `GoogleCalendarRefreshToken` or `GoogleOAuthUserId` directly (the status endpoint only returns a bool).

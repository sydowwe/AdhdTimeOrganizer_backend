# User-endpoint migration to `Sydowwe.Framework`

Companion to [`../migration-prompt.md`](../migration-prompt.md). Verdict on the 26 standalone
endpoints in `AdhdTimeOrganizer/application/endpoint/user/`: **20 move, 6 stay.** (Originally 19/7 —
`UpdateUserPreferencesEndpoint` was reclassified and moved. `GoogleSignInEndpoint` was moved too and
then reverted; it stays. See [stays-portal.md](stays-portal.md).)

Analysis date: 2026-07-30. Re-verify against the code before acting — this is a snapshot.

## Tranches

| # | File | Endpoints | Seams needed | Risk |
|---|---|---|---|---|
| 1 | [tranche-1-sessions.md](tranche-1-sessions.md) | 4 | none | lowest |
| 2 | [tranche-2-email-confirmation.md](tranche-2-email-confirmation.md) | 4 | none | low |
| 3 | [tranche-3-forgot-password.md](tranche-3-forgot-password.md) | 2 | 1 (`BuildResetLink`) | low |
| 4 | [tranche-4-two-factor-settings.md](tranche-4-two-factor-settings.md) | 4 | none (DTO consolidation) | low |
| 4b | [tranche-4b-setup-2fa-subclass.md](tranche-4b-setup-2fa-subclass.md) | +1 new | none | low |
| 5 | [tranche-5-extension.md](tranche-5-extension.md) | 3 | 1 (`HasExtensionAccess`) | medium |
| 6 | [tranche-6-register-and-delete.md](tranche-6-register-and-delete.md) | 2 | 1 hook + a shared flow | highest — **done 2026-07-31** |
| 7 | [stays-portal.md](stays-portal.md) → "Reversed" | 1 | 1 (`ApplyExtraPreferences`) | low |
| — | [stays-portal.md](stays-portal.md) | 6 | — | — |

Ordering rationale: tranches 1–4 are pure lifts with no seams, and they get the DTO consolidation
done while it is still mechanical. Tranches 5 and 6 carry the actual design decisions and benefit
from that cleanup landing first.

All six tranches have landed. Tranche 6 also extracted `UserRegistrationFlow`, which the portal's
(deliberately portal-only) `GoogleSignInEndpoint` now calls instead of holding its own copy of the
create-user sequence.

## Cross-cutting rules for every tranche

- **Every endpoint added to Framework must be `abstract`.** `Program.cs` sets
  `DisableAutoDiscovery = true` and pins `o.Assemblies` to the portal + three modules. Do not widen
  `o.Assemblies`.
- **Preserve `Configure()` verbatim** — route string, `AllowAnonymous()`, `Throttle(...)` args,
  `PreProcessor<…>`, `Validator<…>`, `Roles(...)`. A dropped `AllowAnonymous()` is a silent runtime
  401, not a compile error.
- **Never create a second copy of a DTO.** Move it, reuse Framework's, or make the base generic over
  the request type.
- **No PII in logs** — `{UserId}`, never the email.
- Portal wrappers should be `Configure`-less unless they genuinely override something.

## Definition of done, per tranche

- `dotnet build AdhdTimeOrganizer.sln` clean.
- `dotnet test --filter "FullyQualifiedName~Auth"` green (26 tests as of 2026-07-30).
- No duplicated type or DTO across the two projects.
- Routes unchanged — diff the Swagger route list before/after.
- `CLAUDE.md`'s auth-bases table updated with every new base and its portal subclass.

## Open questions, answered

- **Is "extension client" a Framework concept?** Yes — see [tranche 5](tranche-5-extension.md).
- **Should `BaseSetupTwoFactorForLoginEndpoint` get its portal subclass here?** Yes, as its own
  tranche — see [4b](tranche-4b-setup-2fa-subclass.md).
- **Does anything need `IAuditService`?** It is safe to call for consistency, but auditing is not
  wired up in this solution — no moved endpoint becomes "audited". See `CLAUDE.md` → Auditing.

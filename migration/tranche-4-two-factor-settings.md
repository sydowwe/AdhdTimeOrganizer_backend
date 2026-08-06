# Tranche 4 — 2FA settings (4 endpoints)

**Verdict: move, generic over `TUser`. The real work here is one DTO consolidation.**

| Portal endpoint (type name) | Route | New Framework base |
|---|---|---|
| `GetTwoFactorAuthStatusEndpoint` | `GET /user/2fa/status` | `BaseGetTwoFactorAuthStatusEndpoint<TUser>` |
| `ToggleTwoFactorAuthEndpoint` | `POST /user/2fa/toggle` | `BaseToggleTwoFactorAuthEndpoint<TUser>` |
| `ResetTwoFactorAuthEndpoint` | `POST /user/2fa/reset` | `BaseResetTwoFactorAuthEndpoint<TUser>` |
| `RegenerateRecoveryCodesEndpoint` | `POST /user/2fa/recovery-codes/regenerate` | `BaseRegenerateRecoveryCodesEndpoint<TUser>` |

> File names and type names disagree on two of these (`ToggleTwoFactorAuthenticationEndpoint.cs` →
> `ToggleTwoFactorAuthEndpoint`, `RegenerateTwoFactorAuthRecoveryCodesEndpoint.cs` →
> `RegenerateRecoveryCodesEndpoint`). Sweep both ways for collisions.

## Why it moves

All four use only `ITwoFactorAuthService<TUser>` (`SetUpTwoFactorAuth`, `GenerateNewQrCode`,
`GenerateNewRecoveryCodes`), `UserManager<TUser>`, and Framework's `VerifyUserPreProcessor<TUser,
VerifyUserRequest>` with Framework's `VerifyUserRequest`. `TwoFactorAuthResponse` is already
Framework's and already bound by `ToggleTwoFactorAuthEndpoint`. Nothing product-specific.

## The DTO decision — do NOT move `AuthenticatorSetupResponse`

The portal has:

```csharp
// AdhdTimeOrganizer/application/dto/response/user/AuthenticatorSetupResponse.cs
public record AuthenticatorSetupResponse
{
    public required string QrCode { get; set; }
    public required List<string> RecoveryCodes { get; set; }
}
```

Framework already has a superset:

```csharp
// framework/Sydowwe.Framework/application/dto/response/user/TwoFactorAuthResponse.cs
public record TwoFactorAuthResponse : IMyResponse
{
    public required bool TwoFactorEnabled { get; init; }
    public string? QrCode { get; init; }
    public IEnumerable<string>? RecoveryCodes { get; init; }
}
```

**Reuse `TwoFactorAuthResponse` and delete `AuthenticatorSetupResponse`.** Moving it would create
exactly the kind of near-duplicate pair the reconciliation just finished eliminating.

SPA impact: `/user/2fa/reset` gains a `twoFactorEnabled` field (set `true` — the endpoint already
guards on `user.TwoFactorEnabled`) and loses nothing. `qrCode` / `recoveryCodes` keep their names, so
existing frontend code is unaffected. `RecoveryCodes` widens from `List<string>` to
`IEnumerable<string>` — identical over the wire.

`RegenerateRecoveryCodesEndpoint` returns a bare `List<string>`; leave that response shape alone,
changing it is a frontend break for no gain.

## Preserve exactly

- **None of the four are anonymous, none are throttled.**
- Three carry `PreProcessor<VerifyUserPreProcessor<User, VerifyUserRequest>>()` — Toggle, Reset,
  Regenerate. `GetTwoFactorAuthStatusEndpoint` does **not** (it is a plain read).
- The `_2fa` namespace segment in the portal (`…command.settings._2fa`) is a C# identifier
  workaround; pick a normal Framework namespace, it is not part of any contract.

## Behaviour that must survive the move

- Reset and Regenerate both 400 with "Two-factor authentication is not enabled" before doing
  anything. That guard is the reason these are safe to expose.
- `ResetTwoFactorAuthEndpoint` regenerates recovery codes **as well as** the QR — both, in that
  order, in one call.
- `ToggleTwoFactorAuthEndpoint` returns `TwoFactorAuthResponse { TwoFactorEnabled = false }` with no
  QR when disabling, and the full setup payload when enabling.

## Risk

Low. The DTO swap is the only thing that touches the SPA contract, and it is additive.

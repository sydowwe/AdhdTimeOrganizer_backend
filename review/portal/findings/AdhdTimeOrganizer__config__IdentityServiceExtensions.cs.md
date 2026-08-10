# Review: AdhdTimeOrganizer/config/IdentityServiceExtensions.cs
Role: config
Summary: JWT/Identity wiring is solid overall — no weak env-var fallback, algorithm pinned via `ValidAlgorithms`, deny-by-default extension-client policy — with a couple of minor hardening gaps worth a look.
Coverage: n/a

## Issues
- [Low][Security] AdhdTimeOrganizer/config/IdentityServiceExtensions.cs:32-39 — `TokenValidationParameters` doesn't set `ClockSkew`, so the JWT bearer handler uses the library default of 5 minutes, while `JwtService.cs:137` explicitly sets 30s for its own validation path (likely refresh-token handling).
  Why: If access tokens are meant to be short-lived, a 5-minute skew silently extends their effective validity by that much on every boundary check; the inconsistency with the 30s value elsewhere suggests it may be unintentional rather than a deliberate choice.
  Fix: Set `ClockSkew = TimeSpan.FromSeconds(30)` (or another explicit, deliberately-chosen value) on the `TokenValidationParameters` here too.
  Confidence: Low

- [Low][Security] AdhdTimeOrganizer/config/IdentityServiceExtensions.cs:109-129 — No `options.Lockout.*` configuration is set for `AddIdentityCore<User>`, relying entirely on ASP.NET Core Identity's built-in defaults (5 failed attempts, 5-minute lockout, enabled for new users).
  Why: Defaults are reasonable but silent — if the intent is a specific brute-force-protection policy (e.g., longer lockout, fewer attempts), it isn't visible or pinned here, and lockout only actually triggers if the login flow calls `SignInManager` with `lockoutOnFailure: true` (not verifiable from this file).
  Fix: Either explicitly set `options.Lockout.MaxFailedAccessAttempts` / `DefaultLockoutTimeSpan` here to make the policy visible and intentional, or add a comment noting the defaults are relied on deliberately.
  Confidence: Low

- [Nit][Security] AdhdTimeOrganizer/config/IdentityServiceExtensions.cs:111 — `options.Password.RequiredLength = 8` is on the low end of current guidance (NIST SP 800-63B suggests encouraging longer passphrases, 8 is the historical minimum).
  Why: Combined with the complexity rules it's acceptable, but a higher floor (10-12) would meaningfully raise brute-force cost with negligible UX cost.
  Fix: Consider raising `RequiredLength` to 10-12.
  Confidence: Low

No other issues found — `ValidIssuer`/`ValidAudience` are read via `Helper.GetEnvVar`, which throws (no silent weak fallback) if unset; signing key and algorithm come from `IEcdsaKeyProvider` with `ValidAlgorithms` pinned to prevent algorithm-confusion attacks; the extension-client deny-by-default policy and cookie flags (verified in `AuthCookies.cs` / `Program.cs`, both `HttpOnly`/`Secure`) are correctly out of scope for this file and handled elsewhere.

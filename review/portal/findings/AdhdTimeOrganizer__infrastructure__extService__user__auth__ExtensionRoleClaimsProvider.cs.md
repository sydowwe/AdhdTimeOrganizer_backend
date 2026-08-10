# Review: AdhdTimeOrganizer/infrastructure/extService/user/auth/ExtensionRoleClaimsProvider.cs
Role: other
Summary: Correctly gates the extension-only ActivityTracking role on ClientTypeEnum.Extension; the enum value is set server-side per-callsite in JwtService, not derived from attacker-controlled input, so a web token cannot obtain it through this path.
Coverage: n/a

## Issues
- [Low][Security] ExtensionRoleClaimsProvider.cs:22-29 — The privilege decision here trusts whatever `clientType` its single caller (`JwtService.CreateUserClaims`) passes; this file has no independent verification that the caller is honest.
  Why: If a future change to `JwtService` or a new caller ever computed `clientType` from a client-supplied header/claim instead of the two hard-coded literals (`ClientTypeEnum.Web` / `ClientTypeEnum.Extension` at token-minting call sites), this provider would silently mint the elevated role for a web token with no changes needed here — the escalation risk is fully outsourced to the caller and undocumented as an invariant enforceable in this file.
  Fix: Consider a code comment or a test in JwtService pinning that `clientType` passed to claims providers always originates from the server-chosen minting path, not from request data; no change needed in this file itself.
  Confidence: Low
- [Nit][Quality] ExtensionRoleClaimsProvider.cs:24-26 — Minor: returning `[]` for every other client type is correct but silent; no test file was reviewed alongside this to confirm the web-token-never-gets-role invariant is pinned by an integration test.
  Why: Regression here (e.g. someone flips the ternary) would only be caught by a redirected auth test if one exists; not verifiable from this file alone.
  Fix: Confirm/verify there's an integration test asserting extension-only claim issuance (out of scope for this file review).
  Confidence: Low

No blocking issues found; file matches documented CLAUDE.md intent exactly (extension-only role via IAdditionalUserClaimsProvider, gated on client_type, web tokens excluded).

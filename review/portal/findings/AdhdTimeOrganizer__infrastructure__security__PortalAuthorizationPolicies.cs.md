# Review: AdhdTimeOrganizer/infrastructure/security/PortalAuthorizationPolicies.cs
Role: config
Summary: Single-constant policy-name holder; well-documented, no logic to fault, and the claimed behavior checks out against the actual policy registration.
Coverage: n/a

## Issues
- [Nit][Convention] AdhdTimeOrganizer/infrastructure/security/PortalAuthorizationPolicies.cs:20 — The `ActivityTracking` policy name string literal is duplicated as the `ActivityTracking` role name in `ExtensionRoleClaimsProvider.ExtensionRole` and again as a Swagger tag literal in `ActivityTrackingDesktopSettingsIgnoredProcessGroup.cs`'s `AutoTagOverride("ActivityTracking")`; CLAUDE.md flags this as intentional and warns against conflating them, but nothing in code prevents someone from renaming one and not the others.
  Why: A future rename of the policy or role name would silently desync the Swagger tag or the role check, with no compiler error.
  Fix: Consider a short XML-doc note at each of the three sites cross-referencing the other two (partially done already here), or leave as-is per the documented decision — no code change needed.
  Confidence: Low

Verified: the doc comment's claim that this policy "requires the ActivityTracking role" is accurate — `IdentityServiceExtensions.cs:95-100` registers the policy with `RequireRole(ExtensionRoleClaimsProvider.ExtensionRole)` plus `RequireAuthenticatedUser()` and `ExtensionClientRequirement(true)`, matching the comment. No hard-coded role strings appear in this file itself (the constant is the policy name, not a role name).

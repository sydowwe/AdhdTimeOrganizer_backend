# TEST-10 — no Core route asserts a 401, and three endpoints declare no roles at all

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first. Finding: `CQ-3` 🟠 (second half).

**Status today:** cross-user **404** is well covered (`Archive_OtherUsersActivity_Returns404`,
`Merge_AcrossUsers_Returns404_AndTouchesNothing`, `Clone_ForeignActivityId_Returns404`,
`QuickEdit_ForeignActivityId_Returns404`, and nine `UnauthorizedStatus => NotFound` overrides in
`ActivityEndpointTests.cs`). Extension-client denial is covered once
(`ExtensionToken_CannotAccess_ActivityEndpoint_Returns403`, `:1091`). **Plain 401 is asserted nowhere in
Core**, and `TimerPresetCrudAuthMatrixTests` — the file whose name promises a matrix — contains zero
`[Fact]`s of its own.

## Why this is load-bearing rather than pedantic

Three Core endpoints never call `Roles(...)`:

- `CloneActivityEndpoint.cs:12-24`
- `QuickEditActivityEndpoint.cs:12-25`
- `BaseActivityFormSelectOptionsEndpoint.cs:41-53` (and therefore its three subclasses, in Core,
  History and Planning)

They are authorized only because `AdhdTimeOrganizer/Program.cs:573-576` installs a global configurator:

```csharp
config.Endpoints.Configurator = ep =>
{
    if (ep.AllowedRoles is null || ep.AllowedRoles.Count == 0)
        ep.Roles("User", "Admin", "Root");
    if (!ep.EndpointType.IsDefined(typeof(AllowExtensionClientsAttribute), true))
        ep.Policies(ExtensionClientPolicies.DenyExtensionClients);
};
```

That is a working design — CLAUDE.md says the default `User + Admin + Root` tier is correct for
everything in Core — but it is invisible at the call site, it is inconsistent with
`MergeActivityEndpoint:60,65` and `SetArchivedActivityEndpoint:37,42` (which do declare
`AllowedRoles()`), and **nothing asserts the configurator still exists**. Deleting those four lines
during a `Program.cs` refactor opens three endpoints to any authenticated principal, including
extension tokens, with no build error and no failing test.

## Create `Endpoints/CoreEndpointAuthMatrixTests.cs`

`[Collection("Postgres")]` + `PostgresTestBase`. Use `Fixture.UnauthenticatedFactory.CreateClient()`
for the 401 cases. For the extension-token case, copy the token setup from
`ActivityEndpointTests.cs:1091` (`ExtensionToken_CannotAccess_ActivityEndpoint_Returns403`) — it is the
only place in the suite that builds one.

All cases below should **pass today**. They are guards, not gap pins — no `KnownGap` trait.

### 1. Every Core route is 401 without a token

```csharp
[Theory(DisplayName = "CQ-3: no Core route is reachable unauthenticated")]
[InlineData("GET",   "api/activity/all")]
[InlineData("GET",   "api/activity/1")]
[InlineData("GET",   "api/activity/all-options")]
[InlineData("GET",   "api/activity/form-select-options")]
[InlineData("POST",  "api/activity/filtered-table")]
[InlineData("POST",  "api/activity")]
[InlineData("PUT",   "api/activity/1")]
[InlineData("DELETE","api/activity/1")]
[InlineData("PUT",   "api/activity/1/quick-edit")]
[InlineData("POST",  "api/activity/1/clone")]
[InlineData("PATCH", "api/activity/1/archived")]
[InlineData("POST",  "api/activity/merge")]
[InlineData("GET",   "api/activity-role/all")]
[InlineData("GET",   "api/activity-role/by-system-key/plannerTask")]
[InlineData("POST",  "api/activity-category/batch-delete")]
[InlineData("GET",   "api/timer-preset/all")]
[InlineData("GET",   "api/pomodoro-timer-preset/all")]
```

Send an empty-but-valid body for the write verbs; the auth gate runs before model binding, so the body
does not need to be meaningful. Assert **401** (not 403, not 404) on every row.

The three endpoints named above — `clone`, `quick-edit`, `form-select-options` — are the rows that
matter. Add a comment saying so, so a future reader does not "simplify" the theory by dropping them.

### 2. The extension-client denial covers the undeclared endpoints

```csharp
[Theory(DisplayName = "CQ-3: an extension token is refused on endpoints that declare no roles")]
[InlineData("api/activity/1/clone")]
[InlineData("api/activity/1/quick-edit")]
[InlineData("api/activity/form-select-options")]
```

Expect **403**. This is the half of the configurator that is genuinely deny-by-default and whose
comment (`Program.cs:576-580`) records that an `AuthorizationOptions.FallbackPolicy` "silently
protected nothing" — precisely because the `Roles()` call above it gives every endpoint authorization
metadata. That history is the reason to pin it.

### 3. A plain `User` may call the whole Core surface

Nothing in Core is admin-only. Assert `CreateUserRoleClient()` gets a non-403 on a representative
read from each family (activity, role, category, timer preset, pomodoro preset). This is the pin
against someone "hardening" Core by narrowing `AllowedRoles()` to Admin, which would make the app
unusable for every account and is exactly the failure `PortalRoleCatalog`'s docstring warns about
(`infrastructure/security/PortalRoleCatalog.cs:11-15`).

### 4. `POST /activity/merge` and `PATCH /activity/{id}/archived` accept the default roles

The two endpoints that *do* declare `AllowedRoles() => this.GetDefaultRoles()`. Assert a `User`-role
client is not 403 on either. Cheap, and it pins that `GetDefaultRoles()` still resolves to the portal
catalog rather than the framework's deny-by-default Admin tier.

## Optional: make the configurator itself testable

A stronger guard than any of the above, if you want one: a model-level test in `Modules/` that walks
FastEndpoints' registered endpoint definitions and asserts every endpoint in Core's assembly has
non-empty `AllowedRoles` **and** carries the `DenyExtensionClients` policy unless attributed. That
catches a new endpoint added without roles on the day it is written, rather than on the day someone
notices. `CoreRouteSmokeTests` already reaches the endpoint registry and is the file to copy.

## Doc reconciliation

None — though `../../docs/summary.md` → Extension playbook ("Add an endpoint") says "Default roles are
User+Admin+Root — correct for everything in Core" without saying that the default comes from the
host's configurator rather than from the base classes. One clause would make the dependency visible.

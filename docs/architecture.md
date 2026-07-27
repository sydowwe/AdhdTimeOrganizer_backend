# Project Architecture & Module Split

**Decision:** a reusable, domain-agnostic base (**`Sydowwe.Framework`**) at the bottom — reusable across
*any* .NET 10 API solution — then a product base (**`MojaDigitalnaFirma.Kernel`**) for things specific
to *this* product but shared below the modules, then **feature modules** (vertical slices), a shared
product spine (**`Core`**), thin **portal libraries**, a per-deployment composition root
(**`<Family>.Core`**), and thin **hosts**.

The two base tiers split by *reusability*: `Sydowwe.Framework` is solution-agnostic (graduates to its
own repo/NuGet); `MojaDigitalnaFirma.Kernel` is MojaDigitalnaFirma-specific but module-agnostic. Both
sit **below** the modules. `Core` sits **above** them. (That direction is exactly what distinguishes
Kernel from Core: Kernel is what the modules *stand on*; Core is what *ties them together*.)

There is **no clean-architecture layering inside a slice** — modules are vertical slices
(entity + EF config + endpoints + DTOs + services together). The layering is only *between* the
reuse tiers below.

---

## The tower

```
Sydowwe.Framework   agnostic infra + User/auth/identity/audit + base endpoints
  ▲                   → reusable as-is across solutions (own repo/NuGet later)
  │
MojaDigitalnaFirma.Kernel   product base — shared, product-specific, below the modules
  ▲   ▲               → CoreUser + cross-module contract hub (§2) + IWordTemplateService (no slice)
  │   │
  │   └─ EmployeeModule.Contracts   contracts-only sibling: the employee command/value-object/
  │         ▲                       interface surface, so modules reach Employee WITHOUT referencing
  │         │                       its impl assembly (still below the modules — §2)
  │         │
~15 feature modules — vertical slices         Employee · Attendance · Inventory · Notifications ·
  ▲                                            Scheduler · Approvals · Reminders · Partneri · Vehicles ·
  │   each module → Kernel (+ Contracts)       Majetok · Integrácie · OchranaUdajov · Registratúra ·
  │   (+ Framework); NEVER each other —        Zmluvy · Cestovné náhrady
  │   cross-module needs go through a Kernel seam (§2)
  │
Core        → Kernel + EVERY feature module today  (design: only the always-on ones — see §3 ⚠)
  ▲             abstract AppCoreDbContext (aggregates every module's DbSets) + "me"/profile/change-password
  │
AdminPortal      EmployeePortal      → Core      (portal-specific endpoints only)
  ▲                  ▲
  │                  │
<Family>.Core    → Core + one-or-both portal libs + chosen opt-in modules    (aspirational — not built
  ▲                 concrete DbContext + migrations    yet; concrete contexts still live in hosts — §6)
  │
hosts            thin: Program.cs, identity wiring, Serilog, appsettings, template assets
```

**Dependency rule: arrows point up only.** `Sydowwe.Framework` references nothing of ours;
`MojaDigitalnaFirma.Kernel` references only `Sydowwe.Framework`; modules reference `Kernel` (and
`Framework` transitively) **and never each other** (cross-module needs go through a `Kernel` seam —
§2); `Core` references `Kernel` + the feature modules (by design only the *always-on* ones, but
**today it references all of them** — see §3); portal libs reference `Core`; `<Family>.Core` references
`Core` + the portal lib(s) + its opt-in modules; a host references its `<Family>.Core`.

> **How to tell the tiers apart** — read the `<ProjectReference>` list, because direction *is* the
> identity.
> - **`Sydowwe.Framework`** — zero references to our own projects (only NuGet). The very bottom.
> - **`MojaDigitalnaFirma.Kernel`** — references only `Sydowwe.Framework`, and is referenced *by* the
>   modules. Below the modules, but product-specific.
> - a **module** — references `Kernel`, never `Core`.
> - **`Core`** — references *all* the always-on modules. The spine that ties them together.
>
> Kernel vs Core is purely direction: **Kernel is referenced by modules; Core references modules.** If
> one project ever does both — references the base *and* the modules — that's the bug that produces a
> dependency cycle.

---

## 1. `Sydowwe.Framework` — the reusable base

Everything here is **solution-agnostic** and ships unchanged into a new .NET 10 API. The name signals
"not product-specific" — it's intended to graduate to its own repo / NuGet package reused across
solutions. (Companion test base: **`Sydowwe.Framework.Testing`**.)

- **Infra:** `Result`, base request/response DTOs, filter/sort/paginate, `IProjectionResponse`, DI
  markers, `DbContextHelper`, `EntityBuilderExtensions`, `EntityWithUserBuilderExtensions`,
  partitioning, audit interceptor + `IAuditService` + `AuditLog`/`BusinessAuditLog`.
- **Base entities:** `BaseEntity`, `BaseTableEntity`, `BaseEntityWithUser`/`IEntityWithUser`,
  `ISoftDeletable`, `SelectOptionBase`, lookup bases.
- **Base endpoints:** the whole `application/endpoint/base/*` tree (generic CRUD/grid/filter/sort).
  **Agnostic** — no concrete `User` coupling; it rides on claims (`User.GetId()`). Carries the
  `AuthorizeAsync(entity)` ownership hook on write bases so user-scoped reuse is IDOR-safe.
- **Identity + auth (generic over TUser):** `BaseUser` (abstract base), `UserRole`, `RefreshToken`,
  `JwtService<TUser>` / `IJwtService<TUser>` / `IJwtService` (non-generic refresh surface),
  `TwoFactorAuthService<TUser>` / `ITwoFactorAuthService<TUser>`, `RefreshTokenService` (non-generic,
  returns `UserId`), `EcdsaKeyProvider`, recaptcha, Entra, `LoggedUserService`, all auth contracts +
  auth/user DTOs. Auth endpoint **abstract bases**: `BaseLoginEndpoint<TUser>`,
  `BaseChangePasswordEndpoint<TUser>` (exposes `AfterPasswordChangedAsync` hook),
  `BaseValidateTwoFactorAuthForLoginEndpoint<TUser>`, `BaseGetCurrentUserEndpoint<TUser>`. Concrete
  closures (one per route, closed on the solution's user type) live in `MojaDigitalnaFirma.Core`.
  `LogoutEndpoint` and `RefreshTokenEndpoint` are non-generic and stay fully in Framework.
- **`BaseDbContext<TUser>`** (`infrastructure/BaseDbContext.cs`) — abstract
  `IdentityDbContext<TUser, UserRole, long>` where `TUser : BaseUser`. Calls
  `modelBuilder.Ignore<BaseUser>()` to prevent EF from treating `BaseUser` as a TPH root. Declares
  only identity + audit DbSets (`RefreshTokens`, `AuditLogs`, `BusinessAuditLogs`). Auth services
  depend on *this* base; they stay in `Sydowwe.Framework` with no module DbSet dependency.

### Policy: generic TUser in `Sydowwe.Framework`

`BaseUser` is **abstract** — the Framework is generic over the concrete user type. Each solution
provides exactly one concrete subclass (`CoreUser : BaseUser` in `MojaDigitalnaFirma.Kernel`).
EF maps only the concrete type to the single `user` table (no TPH discriminator, no second user
table). `modelBuilder.Ignore<BaseUser>()` in `BaseDbContext<TUser>.OnModelCreating` enforces this.

**Invariant:** `Sydowwe.Framework` auth logic only touches `BaseUser`'s own
identity/auth/2FA/theme/locale columns. Solution-specific user fields (`MicrosoftAccountId`,
`MustChangePassword`) live only on `CoreUser`. The `AfterPasswordChangedAsync` hook on
`BaseChangePasswordEndpoint<TUser>` is the seam: Core overrides it to clear `MustChangePassword`.

---

## 2. `MojaDigitalnaFirma.Kernel` — the product base

The MojaDigitalnaFirma-wide base **the modules stand on**. Same position in the tower as
`Sydowwe.Framework` (below the modules), but it holds things that are specific to *this* product yet
shared by more than one module and don't belong to any single vertical slice — so they can't live in
the agnostic framework, and putting them in a module would force the other modules to depend on that
module.

References **only `Sydowwe.Framework`**. Referenced **by every module**. Holds no vertical slice, no
DbContext, and (normally) no endpoints — it's contracts, shared types, and cross-module helpers.

- **`CoreUser : BaseUser`** (`user/CoreUser.cs`) — the **one concrete user entity** for this
  solution. Adds `MicrosoftAccountId`, `MustChangePassword`, `HasMicrosoftAccountLinked`. Maps to
  the single `user` table via `CoreUserEntityConfiguration` in `MojaDigitalnaFirma.Core`. Lives in
  Kernel (not Core) so the modules — which reference Kernel — can type their `User` navigation
  properties correctly without a circular dependency.
- **Cross-cutting service contracts** consumed by multiple modules — e.g. `IWordTemplateService` (the
  interface; its `WordTemplateService` implementation, with the `TemplateEngine.Docx` dependency,
  stays higher up so the base doesn't drag in that package).
- Product-wide enums, value objects, and constants shared across modules (`BusinessClock`, etc.).
- **The cross-module contract hub — this is how modules avoid referencing each other.** Each seam
  is a small namespace under Kernel; the owning module *implements* it, consuming modules depend only
  on the contract. The arrow always points *into* the Kernel (owner → `Kernel.<seam>` ← consumer),
  never module → module:

  | Seam (`Kernel/…`) | Owning module (impl) | Consumers |
  |---|---|---|
  | `notification` (`INotificationService`, `NotificationType`) | Notifications | Reminders, any module that sends |
  | `scheduling` (`IScheduler`, `IScheduledJobHandler`, `ICronEvaluator`) | Scheduler | Reminders, Integrácie, Core registrars |
  | `reminders` (`IReminderRegistry`, `ReminderRegistration`) | Reminders | any module that registers a deadline |
  | `export` (`CanonicalExport`, `ExportKind`, `ICanonicalExportSource`) | Integrácie (transport) | producer modules (none yet) |
  | `storage` (`IDocumentStorage`) | `Integration.Microsoft` | Employee, Integrácie, Zmluvy, Majetok, … |
  | `approvals`, `gdpr`, `partneri`, `registratura` | the like-named module | their consumers |
  | `user` (`CoreUser`, `BaseEntityWithCoreUser`) | — (mapped in Core) | every module's `User` nav |
  | **employee** (`GetEmployeeSummariesCommand`, `EmployeeExistsCommand`, `AssertCanManageEmployeeCommand`, …) — lives in the sibling **`EmployeeModule.Contracts`**, not Kernel | Employee (handlers) | Attendance, CestovneNahrady, Majetok, Vehicles, Zmluvy, OchranaUdajov, Registratúra |

- **Cross-module decoupling commands** (Kernel root) — nav-less read/link seams that let one module
  reach another's data without a project reference: `GetStockItemDisplayCommand` (Inventory),
  `GetVehicleEntryPricesCommand` / `LinkVehicleToAssetCommand` (Vehicles ↔ Majetok). Employee's
  equivalents + the decoupling map live in **`MojaDigitalnaFirma.Core.EmployeeModule.Contracts`**
  (a contracts-only sibling project so consumers needn't reference the Employee module itself).

> **Framework vs Kernel** — both are below the modules; the split is *reusability*. If it would ship
> unchanged into an unrelated .NET solution, it's `Sydowwe.Framework`. If it's MojaDigitalnaFirma-
> specific but module-agnostic, it's `MojaDigitalnaFirma.Kernel`.

---

## 3. Feature modules — vertical slices

Each module is a project referencing **`MojaDigitalnaFirma.Kernel`** (and `Sydowwe.Framework`
transitively): its entities, EF configuration, DTOs, validators, services, and endpoints live
together. A module **never** references another module — cross-module needs go through a
`Kernel` seam (see §2). The full roster is in [`modules.md`](modules.md); today there are ~15:
Attendance, Employee, Inventory, Notifications, Scheduler, Approvals, Reminders, Partneri, Vehicles,
Majetok, Integrácie, OchranaUdajov (GDPR), Registratúra, Zmluvy, Cestovné náhrady.

> **⚠️ Current state vs the opt-in design.** The model below describes always-on vs opt-in modules;
> **as wired today `MojaDigitalnaFirma.Core` references *every* feature module** (including `Inventory`
> and `Integration.Microsoft`) — see its `.csproj`. So the per-customer opt-in-at-`<Family>.Core` story
> is **aspirational, not yet implemented**: there is no schema-level opt-out and `<Family>.Core` projects
> don't exist yet (the concrete contexts still live in the hosts — §6). This is the main intermodule debt
> to reconcile: either carve the opt-in modules back out of `Core`, or accept Core-as-superset and drop
> the opt-in framing. Until then, treat the rest of this section as **intent**.

- **`Employee`, `Attendance` — always-on.** Vanilla domain every deployment gets. Referenced by
  `Core`; their DbSets are aggregated by `AppCoreDbContext`.
- **`Inventory` — *intended* opt-in.** Self-contained domain a customer chooses. *Design:* referenced
  at the `<Family>.Core` level only, so a deployment that omits it gets neither the endpoints nor the
  tables. *Reality:* currently referenced by `Core` directly (see the warning above), so the per-customer
  opt-in isn't enforced yet. The `IInventoryDbContext` decouple (§6) is the prerequisite for restoring it.

> Module-vs-folder rule: a separate project is justified only when its **reference graph differs**.
> `Inventory` clearly qualifies (opt-in → different graph). `Employee`/`Attendance` have an identical
> graph (both always-on, both → `Kernel`); they are split into their own projects here for
> vertical-slice cohesion, but folders inside `Core` would be a valid lighter alternative.

---

## 4. `Core` — the shared product spine

References `MojaDigitalnaFirma.Kernel` + the always-on modules. Holds what's shared across *this
product's* portals but spans more than one module (so it can't live in a single module) and isn't a
plain below-the-modules type (so it isn't `Kernel`):

- **`AppCoreDbContext : BaseDbContext<CoreUser>`** (abstract, `infrastructure/persistence/AppCoreDbContext.cs`) —
  closes the generic `BaseDbContext<TUser>` on `CoreUser`; adds the always-on module DbSets
  (`Employees`, `JobTitles`, `EmploymentTypes`, `Leaves`, `LeaveTypes`, `LeaveBalances`,
  `AttendanceCalendars`, `PublicHolidays`, `AttendanceView`). Scans the Core assembly for
  `CoreUserEntityConfiguration`, `RefreshTokenCoreEntityConfiguration`, and the supplemental FK
  configs for `PushSubscription`/`NotificationPreference`/`Employee` → `CoreUser`. Keeps the WorkLog
  generic `AppCoreDbContext<TWorkLog>`, `GetCustomerAssemblies()`, virtual `ConfigureWorkLog()`.
- **`CoreUserEntityConfiguration`** — `ToTable("user")`; `MicrosoftAccountId.HasMaxLength(50)`;
  `Ignore(PhoneNumberConfirmed)`. Picked up from Core assembly scan.
- **`EntityWithCoreUserBuilderExtensions`** — `IsManyWithOneCoreUser`/`IsOneWithOneCoreUser` builder
  helpers. (The matching base class **`BaseEntityWithCoreUser`** with the typed `public CoreUser User`
  nav now lives in **`Kernel/user`** alongside `CoreUser`, so modules can derive from it without
  referencing Core; only the EF builder helper stays here.)
- **`UserDeactivationService`** / **`IUserDeactivationService`** — deactivates a user by `UserId`
  using `UserManager<CoreUser>`; used by `EmployeeErasureService` and `FinishTerminationEndpoint`.
- **Concrete auth endpoint closures** (`LoginEndpoint`, `ChangePasswordEndpoint`, etc.) — close
  the Framework abstract bases on `CoreUser`; `ChangePasswordEndpoint` overrides
  `AfterPasswordChangedAsync` to clear `MustChangePassword`.
- **`DefaultUsersSeeder`** — root admin seeder; uses `UserManager<CoreUser>` and `CoreUser`
  constructor directly (moved from `Sydowwe.Framework` where `new User()` is now a compile error).
- **Shared product endpoints:** "me"/profile reads, anything spanning more than one always-on module
  that both portals expose. (Pure-`User` endpoints like change-password live in `Sydowwe.Framework`.)
- Shared seeders for vanilla always-on data.

---

## 5. Portal libraries — the admin/employee axis

The portal split is enforced by the **reference graph**, not by per-host endpoint filtering. (Filtering
endpoints out of a shared assembly trades a compile-time guarantee for a runtime one and ships unused
admin DTOs/services into the employee host — avoid it for the portal boundary.)

- **`AdminPortal`** → `Core`. Admin-only endpoints: user CRUD, employee onboarding
  (contract/Bozp/SharePoint), lookups admin, admin attendance management (edit anyone's
  worklog/leave, approve leave, leave-type/leave-balance admin), admin grids/reports, admin seeders.
- **`EmployeePortal`** → `Core`. Employee self-service (`Roles(GetUserOrHigherRoles())`,
  `FilteredByUser = true`, ownership hook on writes): my-profile, my-worklog, my-leave, my-attendance.
  No entities of its own — reuses the module domain scoped to `User.GetId()`.

FastEndpoints discovers endpoints in referenced assemblies, so an admin host (which references
`AdminPortal`) physically cannot expose an employee-only endpoint, and vice versa.

---

## 6. `<Family>.Core` — per-deployment composition root

Where a deployment is actually assembled. References `Core` + the portal lib(s) it ships + the opt-in
modules it bought. **Owns the concrete `DbContext` + migrations** — so when a family runs *two* hosts
(admin + employee) on one database, there is still a single context and one migration history.

- **`Sandbox.Core`** → `Core` (+ portal libs). Vanilla concrete `SandboxDbContext : AppCoreDbContext`.
  No opt-in modules unless added for testing. The living integration bed.
- **`HBCleaning.Core`** → `Core` + portal lib(s) + **`Inventory`** + HBCleaning domain
  (complaints, apartmentBuildings, caretaker, propertyManager, cleanedCompany, quotation, `HbWorkLog`).
  Concrete `HbAppDbContext : AppCoreDbContext<HbWorkLog>, IInventoryDbContext` + migrations.

> **Current state:** these `<Family>.Core` projects don't exist yet — the concrete contexts still live
> in the hosts (`SandboxDbContext` in `AdminPortal.Sandbox`, `HbAppDbContext` in
> `HBCleaning.AdminPortal`). Carving them out is steps 3 & 7 below.

> **Opt-in mechanics.** A module opts in by (a) being referenced (→ endpoints discovered) and (b) its
> EF configs being applied by the concrete context (→ tables exist). `Inventory` ships an
> **`IInventoryDbContext`** interface exposing its DbSets; the concrete context implements it
> (`HbAppDbContext : … , IInventoryDbContext`), and inventory endpoints/services inject
> `IInventoryDbContext` instead of the concrete `HbAppDbContext`. (Today ~30 inventory classes inject
> `HbAppDbContext` directly — that decouple is the prerequisite for making `Inventory` a standalone
> opt-in module.)

---

## 7. Hosts — thin

Each host owns `Program.cs`, identity service wiring, Serilog, appsettings/secrets, template assets,
and references **one `<Family>.Core` + one portal lib**. `AdminPortal.Sandbox`,
`HBCleaning.AdminPortal` today; `*.EmployeePortal` when the employee portal exists.

---

## DbContext hierarchy (one DB per deployment)

```
BaseDbContext<TUser>           abstract — IdentityDbContext<TUser,UserRole,long>   (Sydowwe.Framework)
                                          TUser : BaseUser; Ignore<BaseUser>()
                                          identity + audit DbSets only
        ▲
AppCoreDbContext               abstract — closes TUser=CoreUser; + always-on        (MojaDigitalnaFirma.Core)
                                          module DbSets; scans Core assembly for
AppCoreDbContext<TWorkLog>               CoreUser / FK supplement configs; WorkLog
        ▲                                generic via <TWorkLog>
HbAppDbContext / SandboxDbContext   concrete — + opt-in/customer DbSets + migrations  (<Family>.Core / host today)
```

**One concrete `CoreUser` per deployment.** `BaseDbContext<TUser>.OnModelCreating` calls
`modelBuilder.Ignore<BaseUser>()` so EF maps only the concrete TUser to the `user` table — no
discriminator column, no TPH inheritance hierarchy. `CoreUserEntityConfiguration` (scanned from the
Core assembly) overrides `ToTable("user")` on `CoreUser`.

Generate migrations with the concrete-context project as `-p` and a host as startup, e.g.
`dotnet ef migrations add X -p HBCleaning.Core -s HBCleaning.AdminPortal`. (Until `<Family>.Core` is
carved out, the concrete context's current host project is both `-p` and `-s`.)

---

## Endpoint placement

| Endpoint needed by… | Lives in… |
|---|---|
| any solution (auth: login/refresh/logout/2fa, change-password) | **Sydowwe.Framework** |
| reusable base class (no concrete route) | **Sydowwe.Framework** |
| both portals of this product (me/profile) | **Core** |
| a single feature, both portals | that **module** |
| admin only | `AdminPortal` |
| employees only | `EmployeePortal` |

---

## Extending vanilla for a customer

A customer deployment (`<Family>.Core`) gets its own database, so customizations are physical,
not feature-flags. There are **four seams** — pick by *what* you're changing. Do **not**
generalize Core up front; apply a seam only to the entity/endpoint/service a customer actually touches.

| You need to… | Seam | Touches Core? |
|---|---|---|
| **Add a whole new domain** (tables + endpoints) | opt-in **module** referenced at `<Family>.Core` (e.g. `Inventory`) | no |
| **Add fields to a vanilla entity** | **derived entity + TPH** | yes, once: make the entity's context + endpoint base generic |
| **Change a vanilla endpoint's behavior** | **replace the endpoint** (subclass + `Endpoints.Filter`) | no |
| **Change a vanilla service's behavior** | **decorator** (`IDecoratorService`) | no |

Full recipes, the worked `WorkLog` → `HbWorkLog` example (TPH entity + DTO + endpoint + the
discriminator trade-off), and the service-decorator resolution rules / failure modes live in
**[`extendingVanillaForCustomers.md`](extendingVanillaForCustomers.md)**.

---

## Sequencing

1. **Split the base into `Sydowwe.Framework` (agnostic) + `MojaDigitalnaFirma.Kernel` (product base) —
   mostly done.** The agnostic base, identity/auth, audit, base endpoints, and `BaseDbContext` live in
   `Sydowwe.Framework`; auth services target `BaseDbContext`. `MojaDigitalnaFirma.Kernel` exists and
   holds `IWordTemplateService`. **Wiring left:** point every module's `<ProjectReference>` at
   `MojaDigitalnaFirma.Kernel` (today the modules reference `Sydowwe.Framework` directly and `Kernel`
   is referenced by no one), and move any remaining product-specific, cross-module contracts/types down
   into `Kernel`. The `WordTemplateService` *implementation* stays higher up — its `TemplateEngine.Docx`
   dependency must not leak into the base tiers.
2. **Carve `Core` — done.** `AppCoreDbContext : BaseDbContext` and the always-on shared domain live in
   `Core` / the `Employee` + `Attendance` module projects; `BaseDbContext` (identity+audit) is split
   from `AppCoreDbContext` (module DbSets).
3. **Move `SandboxDbContext` + migrations into a new `Sandbox.Core`** (out of the `AdminPortal.Sandbox`
   host); confirm the Sandbox host boots + migrates.
4. **Decouple `Inventory`** via `IInventoryDbContext`; lift it out of `HBCleaning.AdminPortal` into a
   vanilla `Inventory` module project referenced from `HBCleaning.Core`.
5. **Fix the auth/onboarding Critical/High findings before they become shared** (they ship into every
   solution once in `Sydowwe.Framework`).
6. **Create `EmployeePortal`** + `Sandbox.EmployeePortal` host on the user-scoped base endpoints.
7. **Carve `HBCleaning.Core`** (concrete `HbAppDbContext` + customer domain + migrations, out of the
   `HBCleaning.AdminPortal` host) so both HBCleaning hosts share one DB cleanly.

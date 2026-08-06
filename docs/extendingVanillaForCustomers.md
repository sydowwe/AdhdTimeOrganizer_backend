# Extending vanilla for customer needs

Each customer deployment (`<Family>.Core` / `<Family>.AdminPortal`) gets its **own database**, so
customizations are physical, not feature-flags. There are **four seams** — pick by *what* you're
changing. Do **not** generalize Core up front; apply a seam only to the entity / endpoint / service a
customer actually touches.

| You need to… | Seam | Touches Core? |
|---|---|---|
| **Add a whole new domain** (tables + endpoints) | opt-in **module** referenced at `<Family>.Core` (e.g. `Inventory`) | no |
| **Add fields to a vanilla entity** | **derived entity + TPH** ([§2](#2-add-fields-to-a-vanilla-entity-derived-entity--tph)) | yes, once: make the entity's context + endpoint base generic |
| **Change a vanilla endpoint's behavior** | **replace the endpoint** (subclass + `Endpoints.Filter`) ([§3](#3-change-a-vanilla-endpoints-behavior)) | no |
| **Change a vanilla service's behavior** | **decorator** ([§4](#4-change-a-vanilla-services-behavior-decorator)) | no |

The worked reference for §2–§3 is **`WorkLog` → `HbWorkLog`** (the HBCleaning attendance work log,
which adds `BeforeWorkArrival` / `AfterWorkArrival`). File paths below point at the live
implementation.

---

## 1. Add a whole new domain (module)

A self-contained domain (its own tables + endpoints) that only some customers need is an **opt-in
module** — a class library (e.g. `Core.Inventory`, `Core.Notifications`) referenced at the
`<Family>.Core` level, not by vanilla `Core`. The customer's `AppCoreDbContext` exposes its `DbSet`s
and applies its EF configurations; its endpoints are discovered by the FastEndpoints scan. Nothing in
Core depends on it, so deployments that don't reference it never ship its schema. See
[`docs/modules.md`](modules.md) and [`architecture.md`](architecture.md) for module structure.

---

## 2. Add fields to a vanilla entity (derived entity + TPH)

The house pattern is **single-table inheritance (TPH)**: the customer derives from the vanilla entity,
adds columns, and reuses the generic endpoint bases unchanged. The whole hierarchy lives in the
vanilla table plus a `discriminator` column; Core keeps querying the **base** type and sees the
derived rows polymorphically.

### Recipe

**Step 1 — Derive the entity, add only the new fields.**
`MojaDigitalnaFirma.HBCleaning.AdminPortal/domain/model/entity/attendance/HbWorkLog.cs`:

```csharp
public class HbWorkLog : WorkLog
{
    public DateTime BeforeWorkArrival { get; set; }
    public DateTime AfterWorkArrival { get; set; }
}
```

**Step 2 — Configure the derived type with ONLY the new columns. ⚠️ Never call
`BaseEntityConfigure()` / `HasKey` / `ToTable` on it.** In TPH the key / table / relationships belong
to the **root** type and are owned by the vanilla `WorkLogEntityConfiguration` (applied via the Core
assembly scan). A derived-type config that re-declares the key throws *"A key cannot be configured on
'HbWorkLog' because it is a derived type."*
`MojaDigitalnaFirma.HBCleaning.AdminPortal/infrastructure/persistence/configuration/HbWorkLogConfiguration.cs`:

```csharp
public class HbWorkLogConfiguration : IEntityTypeConfiguration<HbWorkLog>
{
    public void Configure(EntityTypeBuilder<HbWorkLog> builder)
    {
        builder.Property(e => e.BeforeWorkArrival).IsRequired();
        builder.Property(e => e.AfterWorkArrival).IsRequired();
    }
}
```

**Step 3 — Make the vanilla context substitutable (once per extensible entity).** Core ships the
context as a generic that closes a type parameter on the derived entity; the concrete customer context
binds it. `MojaDigitalnaFirma.Core/infrastructure/persistence/AppCoreDbContext.cs`:

```csharp
// Non-generic base: all vanilla DbSets + module configs.
public abstract class AppCoreDbContext(...) : BaseDbContext<CoreUser>(...)
{
    // ... vanilla DbSets, ApplyConfigurationsFromAssembly(...) for each module ...

    foreach (var assembly in GetCustomerAssemblies())
        modelBuilder.ApplyConfigurationsFromAssembly(assembly);

    // Override in the customer context to register customer EF configurations.
    protected virtual IEnumerable<Assembly> GetCustomerAssemblies() => [];
}

// Generic subclass: closes the WorkLog type parameter so a customer can substitute HbWorkLog.
public abstract class AppCoreDbContext<TWorkLog>(...) : AppCoreDbContext(...)
    where TWorkLog : WorkLog
{
    // Concrete-context callers see DbSet<HbWorkLog>; Core still queries the base via Set<WorkLog>().
    public DbSet<TWorkLog> WorkLogs { get; set; }
}
```

The customer context binds the closure and registers its configs.
`MojaDigitalnaFirma.HBCleaning.AdminPortal/infrastructure/persistence/HbAppDbContext.cs`:

```csharp
public class HbAppDbContext(...) : AppCoreDbContext<HbWorkLog>(...)
{
    // ... HBCleaning-only DbSets ...

    protected override IEnumerable<Assembly> GetCustomerAssemblies()
    {
        yield return typeof(HbAppDbContext).Assembly;   // picks up HbWorkLogConfiguration
    }
}
```

> Core code keeps querying the **base** type — `dbContext.Set<WorkLog>()` returns the derived rows
> polymorphically — so vanilla services and reads stay untouched. Checks that must target the concrete
> type pass it explicitly, e.g. `AttendanceOverlapChecker.HasOverlapAsync(_dbContext, req, typeof(TWorkLog), …)`.

**Step 4 — Derive the request DTO.** It carries the new fields and maps them in `ToEntity` /
`UpdateEntity`. `MojaDigitalnaFirma.HBCleaning.AdminPortal/application/dto/attendance/HbWorkLogRequest.cs`:

```csharp
public record HbWorkLogRequest : WorkLogRequest, IMyRequest<HbWorkLog>
{
    public required DateTime BeforeWorkArrival { get; init; }
    public required DateTime AfterWorkArrival { get; init; }

    public override HbWorkLog ToEntity => new() { /* base fields + the two new ones */ };
    public void UpdateEntity(HbWorkLog entity) { /* base fields + the two new ones */ }
}
```

**Step 5 — Make the vanilla endpoint base generic (once per extensible entity), and close it.** Core
ships an abstract generic base **and** a sealed vanilla closure.
`MojaDigitalnaFirma.Core.Attendance/application/endpoint/workLog/command/AddWorkLogEndpoint.cs`:

```csharp
public abstract class AddWorkLogEndpoint<TWorkLog, TRequest>(...) : BaseCreateEndpoint<TWorkLog, TRequest>(...)
    where TWorkLog : WorkLog
    where TRequest : WorkLogRequest, ICreateRequest<TWorkLog> { /* all the business logic */ }

public sealed class AddWorkLogEndpoint(...) : AddWorkLogEndpoint<WorkLog, WorkLogRequest>(...) { }  // vanilla
```

The customer closes it on the derived type (and may override `Route` / `AllowedRoles`).
`MojaDigitalnaFirma.HBCleaning.AdminPortal/application/endpoint/attendance/workLog/HbAddWorkLogEndpoint.cs`:

```csharp
public sealed class HbAddWorkLogEndpoint(HbAppDbContext dbContext, ...)
    : AddWorkLogEndpoint<HbWorkLog, HbWorkLogRequest>(dbContext, ...)
{
    public override string[] AllowedRoles() => this.GetUserRole();
    public override string Route => "/work-log";
}
```

**Step 6 — Replace the vanilla endpoint on the shared route** so only the customer version binds.
`MojaDigitalnaFirma.HBCleaning.AdminPortal/Program.cs` (`UseFastEndpoints`):

```csharp
config.Endpoints.Filter = ep =>
    ep.EndpointType != typeof(AddWorkLogEndpoint) &&
    ep.EndpointType != typeof(EditWorkLogEndpoint);
```

### About the `discriminator` column

TPH stores the whole hierarchy in one table plus a `discriminator` column EF uses to materialize the
right CLR type. With two-or-more mapped types it is **mandatory and cannot be dropped from the
migration** — EF reads and writes it on every query/insert, so removing it breaks the entity at
runtime. In a per-customer database it's just a constant short-string column (negligible). The only
ways to avoid it are (a) map a single type per DB — but then Core's `Set<WorkLog>()` and the generic
reads can't see derived rows without generalizing every consumer, or (b) TPT (separate derived table,
no discriminator, at the cost of a join). We deliberately accept the discriminator and keep TPH for
the zero-friction reuse of the vanilla read/write stack.

> `BaseUser` / `CoreUser` is **not** this pattern — there only one concrete type is ever mapped, so
> `Ignore<BaseUser>()` gives a single `user` table with no discriminator. Reserve full generic
> substitution for that kind of pivotal identity type; use TPH for ordinary additive customer fields.

---

## 3. Change a vanilla endpoint's behavior

When you only need to change endpoint behavior (not add entity fields), subclass the generic Core
endpoint, override the hooks (`BeforeMapping` / `AfterMapping` / `AfterSave`, `AllowedRoles`, `Route`),
and filter the vanilla one out in `Program.cs` with the same `Endpoints.Filter` as Step 6 above. This
is independent of whether you also added fields — `HbAddWorkLogEndpoint` does both: it binds the
derived type *and* overrides `Route` / widens `AllowedRoles()`.

---

## 4. Change a vanilla service's behavior (decorator)

When the behavior to change lives in a **service** (`IWorkHourCalculatorService`, …) rather than an
endpoint, wrap the Core implementation with a **decorator** instead of forking it. The customer ships a
class that wraps the Core service, adds its behavior, and delegates the rest. The Core implementation
stays in the container — it is **wrapped, not orphaned**.

If a customer needs entirely new behavior with no Core counterpart, write a normal service (no
`IDecoratorService`) — decoration is specifically for *wrapping an existing Core registration*.

### The marker: `IDecoratorService`

`framework/Sydowwe.Framework/config/dependencyInjection/IDecoratorService.cs` is an empty marker. Implementing it
is the signal that a class **wraps an existing Core service**. It changes how the class is registered:

- Implements a lifetime marker (`IScopedService` / `ISingletonService` / `ITransientService`) **but
  not** `IDecoratorService` → registered by the assembly scan as `AsImplementedInterfaces()` (a fresh
  registration).
- Implements `IDecoratorService` → **excluded** from those scans and registered via Scrutor's
  `services.Decorate(decoratedInterface, decoratorType)`; the previously-registered Core implementation
  is injected as the "inner" instance.

The wiring is `RegisterDecorators` in
`MojaDigitalnaFirma.HBCleaning.AdminPortal/config/HbCleaningServiceExtensions.cs`, called near the end
of `AddHbCleaning()` — **after** `AddCore()` and the scans, so the Core registration already exists.

### How to write a decorator

Implement the service interface, a lifetime marker, and `IDecoratorService`. Take the decorated
interface as a constructor parameter — that's the Core instance Scrutor injects. Delegate to it,
adding your behavior:

```csharp
public class HbWorkHourCalculatorService(
    IWorkHourCalculatorService inner,   // ← the Core impl, injected by Scrutor
    HbAppDbContext dbContext            // ← any other deps, in any order
) : IWorkHourCalculatorService, IScopedService, IDecoratorService
{
    public float CalculateWorkHours(DateTime start, DateTime end)
    {
        var hours = inner.CalculateWorkHours(start, end);
        // cleaning-specific adjustments…
        return hours;
    }

    // Forward every other interface member to `inner` unless you mean to change it.
}
```

You must implement the **whole** interface; for members you don't change, forward to `inner`. Leaving
one out is a compile error. Forwarding (rather than `: CoreImpl`) keeps the Core implementation
authoritative and avoids re-deriving its internals.

### How the decorated interface is resolved

`RegisterDecorators` does **not** rely on constructor-parameter order. It resolves the decorated
interface as **the one interface the decorator both implements and accepts as a constructor
parameter**:

1. Collect the interfaces the type implements, minus the meta markers (`IScopedService`,
   `ISingletonService`, `ITransientService`, `IMapperService`, `IDecoratorService`).
2. Collect the types of all its constructor parameters.
3. The decorated interface is the single item in the intersection.

This is robust against the things that quietly broke the old "first ctor parameter" rule:

- **Parameter order** is irrelevant — `(ILogger log, IWorkHourCalculatorService inner)` resolves
  correctly; `ILogger` is a ctor param but not an implemented service interface.
- **Non-service dependencies** (`DbContext`, `ILogger`, options, …) can appear anywhere.
- **Base interfaces** the contract extends (`IWorkHourCalculatorService : ISomeBase`) don't confuse it
  — only the interface actually injected counts.

### Failure modes (all at startup)

`RegisterDecorators` throws `InvalidOperationException` naming the decorator when the intersection isn't
exactly one, i.e. you:

- **forgot to inject the inner instance** (implements the interface but no matching ctor param) → the
  Core impl would be orphaned;
- **implement zero decoratable interfaces** (only markers);
- **implement two** service interfaces that are both ctor params (ambiguous).

Fail-fast at composition time, never a silent mis-wire at runtime.

### Lifetime

Scrutor's `Decorate()` **preserves the inner registration's lifetime**. The `IScopedService` (etc.)
marker on the decorator only excludes it from the scans — it does **not** set the lifetime. Keep the
marker for consistency, but if you need a *different* lifetime than the Core service, that's a sign this
shouldn't be a decorator.

### Known limitation: multiple decorators on one interface

If two decorators ever wrap the same interface, the nesting order follows reflection order, which is
**not deterministic**. There are no such cases today; if one arises, add an explicit ordering mechanism
in the `foreach` in `RegisterDecorators` rather than relying on discovery order.

### Checklist

- [ ] Class implements the service interface, a lifetime marker, and `IDecoratorService`.
- [ ] The service interface is one of the constructor parameters (the inner instance).
- [ ] Every interface member is implemented — unchanged ones forward to `inner`.
- [ ] It lives in an assembly scanned by `AddHbCleaning()`.
- [ ] Build runs — a mis-shaped decorator fails at startup with a named exception.

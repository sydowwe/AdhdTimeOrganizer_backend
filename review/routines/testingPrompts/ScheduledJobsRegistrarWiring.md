# TEST-14 — The scheduled-jobs registrar is actually wired into the host

Read `_SHARED-CONTEXT.md` first. `Modules/ModuleWiringTests.cs` and `Modules/SeamWiringTests.cs` are
the existing models for this class of test — this belongs alongside them, not in `Routines/`.

## Why this is the highest-value cheap test in the backlog

`summary.md:122-131` enumerates **five** registration points for this slice, none of which break the
build if dropped. Four are covered somewhere in the suite. The fifth is not:

> The two scheduled jobs are a **fifth** registration point: the handlers themselves come from the
> ordinary `IScopedService` scan, but their *schedules* only exist if
> `AddHostedService<RoutinesScheduledJobsRegistrar>()` is in `Program.cs`. Dropping that line builds
> fine — the jobs simply never fire.

The existing job tests (`RoutineTodoListResetJobHandlerTests`, `RoutinePeriodNudgeJobTests`) construct
the handler directly with `ActivatorUtilities` and invoke `ExecuteAsync` by hand — deliberately, so
they test the work rather than the substrate. The consequence is that **every routine job test would
still pass with the registrar deleted from `Program.cs`**, and routine resets would silently stop
happening in production. This is precisely the shape CLAUDE.md's "Silent failures" section is about:
assert on rows, not on routes.

## What to write

Add to `Modules/ModuleWiringTests.cs` (or a new `Modules/ScheduledJobWiringTests.cs` if that file is
already crowded).

### 1. The registrar is registered as a hosted service

```csharp
[Fact]
public void RoutinesScheduledJobsRegistrar_IsRegisteredAsAHostedService()
```

Resolve `IEnumerable<IHostedService>` from `Fixture.AdminAndUserFactory.Services` and assert one is a
`RoutinesScheduledJobsRegistrar`. If the test host trims hosted services, fall back to inspecting the
`IServiceCollection` — check how `ModuleWiringTests` reaches the composition root and reuse that.

### 2. Both handlers resolve by their keys

```csharp
[Theory]
[InlineData(RoutineTodoListResetJobHandler.HandlerKey)]   // "Routines.TodoListReset"
[InlineData(RoutinePeriodNudgeJobHandler.HandlerKey)]     // "Routines.PeriodNudge"
```

Resolve `IEnumerable<IScheduledJobHandler>` from a scope and assert exactly one has each `Key`.
**Exactly one** matters: `summary.md:124-127` warns that being in both `ModuleAssemblies` and the
`AddDependencyInjection` `AppDomain` sweep silently doubles every `IEnumerable<T>` registration, so a
duplicate here would mean every reset runs twice. A `.Should().ContainSingle(...)` catches that.

### 3. Both registrations reach the scheduler

The registrar exposes its two `RecurringJobRegistration`s as static properties
(`RoutinesScheduledJobsRegistrar.ResetRegistration` / `.NudgeRegistration`), which makes this easy —
assert on them directly:

- `JobKey == HandlerKey` for both (the registrar sets them equal; a typo in either would leave the
  schedule pointing at nothing).
- `Schedule` is the expected cron — `"0 0 2 * * ?"` for the reset, `"0 0 9 * * ?"` for the nudge. The
  reset's cron is load-bearing history: it was carried over verbatim from a host-side Quartz trigger
  "so the extraction does not move anyone's reset instant" (`:26-27`). Pin it.
- `DisallowConcurrent == true` on both — it is no longer an attribute on the handler class, so nothing
  else records this.
- `OwnerModule == "Routines"`.

### 4. The registrar survives a scheduler failure

`StartAsync` wraps each registration in its own try/catch specifically so "one job's cadence problem
must not keep the other off the schedule" (`:51-65`), and never throws so host startup cannot be
blocked. Build a factory with a throwing `IScheduler` (see
`RoutineTodoListResetJobHandlerTests.cs:349-353` for the service-swap pattern), call `StartAsync`, and
assert it completes without throwing — and, ideally, that the **second** registration was still
attempted after the first threw. That second half is the actual guarantee the loop structure provides
and it is currently unasserted.

## Note

⚠ `CQ-1` means the 02:00 cron is currently irrelevant for monthly and yearly periods — they never reset
regardless of when the job fires. Fixing the wiring test does not fix that; see
`ComputeNextResetCatchUp.md`. Write both.

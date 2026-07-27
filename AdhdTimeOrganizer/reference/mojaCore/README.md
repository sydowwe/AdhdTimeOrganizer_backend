# Reference: MojaDigitalnaFirma.Core composition root

These files are **not compiled** (`<Compile Remove="reference\**" />` in the csproj). They are the composition-root wiring from the solution the Notifications / Reminders / Scheduler modules came
from, kept as a worked example while that wiring gets rebuilt here.

`MojaDigitalnaFirma.Core` itself was deleted: it referenced 17 module projects that do not exist in this solution, and its job — DbContext, auth endpoints, user seeding, job registration — is the job
`AdhdTimeOrganizer` now does.

They will not compile as-is. Every file references either `CoreUser` (being replaced by this project's
`User`) or one of the absent Moja domain modules. Port the *pattern*, not the code.

Delete this folder once the modules are wired up.

| File                                                                                               | What to take from it                                                                                                                                                                                                                                                                                                                     |
|----------------------------------------------------------------------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `AppCoreDbContext.cs`                                                                              | How the three modules' `IEntityTypeConfiguration`s get discovered and folded into one `DbContext`, and where the module `DbSet`s are declared. ~90% of it is Moja domain (Attendance, Zmluvy, Registratura…) — ignore that; read the `ApplyConfigurationsFromAssembly` / module-registration structure.                                  |
| `CoreScheduledJobsRegistrar.cs`                                                                    | The owner-side `IHostedService` pattern for recurring jobs: each module pushes its own `RecurringJobRegistration` list to `Kernel.scheduling.IScheduler` on boot, idempotent by `JobKey`. Note the `ISchedulerFactory is null` guard that lets a host skip Quartz cleanly. This is the shape `AdhdTimeOrganizer` needs for its own jobs. |
| `NotificationPreferenceCoreEntityConfiguration.cs`<br>`PushSubscriptionCoreEntityConfiguration.cs` | The host-side seam that binds a module entity's `UserId` to the *concrete* user type. Six lines each, and exactly what has to be written against `AdhdTimeOrganizer.domain.model.entity.user.User` instead of `CoreUser`. The modules deliberately do not know the user type — this is where it gets supplied.                           |
| `EntityWithCoreUserBuilderExtensions.cs`                                                           | How `CoreUser` was plugged into the framework's generic `BaseEntityWithUser<TUser>` / `IsManyWithOneUser`. Relevant to reconciling this project's non-generic `BaseEntityWithUser` with the framework's generic one.                                                                                                                     |
| `ReminderPersonalDataProvider.cs`                                                                  | Weakest of the six — it is an employee GDPR-export adapter and this app has no Employee concept. Kept only as an example of implementing a Kernel contract *in the composition root* to avoid giving a module a domain dependency, and for its PII discipline notes on reminder projections.                                             |

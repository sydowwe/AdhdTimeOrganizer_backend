# TEST-13 — Integration tests for `RoutinePeriodNudgeJob`

## Context you need (do not rediscover)

**Repo:** `C:/Users/jakub/RiderProjects/AdhdTimeOrganizer`
**Under test:** `AdhdTimeOrganizer/infrastructure/jobs/RoutinePeriodNudgeJob.cs` — Quartz job,
scheduled 09:00 daily in `Program.cs`.
**Collaborators:** `domain/service/RoutineResetService.cs` (`EvaluateEndingSoon`,
`ShouldWarnGraceExpiring` — pure predicates) and
`application/service/routine/RoutinePeriodNotificationService.cs`
(`IRoutinePeriodNotificationService`, the adapter onto the Notifications module).

**Test project:** `AdhdTimeOrganizer.IntegrationTests`. Read `Infrastructure/AppDbContextFixture.cs`
and `Routines/RoutineNotificationTests.cs` first — the latter already exercises the notification
path and is the file to extend or mirror. xunit v3, FluentAssertions, `[Collection("Postgres")]`,
real `Program` over `Testcontainers.PostgreSql`. `CreateDbContext()` for seeding/asserting;
override `SeedAsync(db)`.

Resolve the job from the host's provider and call `Execute(context)` directly with a stub
`IJobExecutionContext`; don't drive the Quartz scheduler.

You will need to **substitute `IRoutinePeriodNotificationService` with a controllable test double**
(a recording stub, and a variant that throws on the Nth call). Check how
`RoutineNotificationTests.cs` already does this and reuse that mechanism rather than inventing a
second one.

## What the job does (from `docs/domain-map.md`)

Two independent notifications per period, both idempotently marked:

- **Lead-time nudge** — fires `ReminderLeadDays` before the reset, **only while something is still
  unfinished**, with a ceiling-days-left count. Marked by `EndingSoonNotifiedFor`.
- **Grace-expiry warning** — fires 1 day before `StreakGraceUntil`. Marked by `GraceNotifiedFor`.

A period that is **fully done is skipped without marking**, deliberately — so un-ticking an item
tomorrow still earns its nudge. Hidden periods (`IsHidden`) are skipped entirely.
`ReminderLeadDays` is NULL or `1..LengthInDays-1`, so a one-day period can never have a lead nudge.

## Scenarios to write

### A. `CQ-5` — a mid-loop notifier failure must not lose earlier markers (should FAIL today)

This is the point of the whole file. All marker mutations are persisted by a **single**
`SaveChangesAsync` after the loop, with no try/catch around the notify calls. One throw aborts the
loop, so every period already notified in that run loses its marker and is re-notified tomorrow,
and every later period is skipped.

1. Seed **three** periods for three different users, all due a lead-time nudge today.
2. Install a notification double that succeeds for the first period and **throws** for the second.
3. Execute the job (expect it to either swallow or propagate — assert on state, not on the throw).
4. Assert from a fresh context:
   - period 1 has `EndingSoonNotifiedFor` set (its successful notification is not lost),
   - period 3 was still processed (the loop did not abandon the remainder),
   - period 2 is left unmarked so it retries next run.

Then assert the **user-visible** consequence directly: run the job a second time with a
now-healthy notifier and assert period 1 is **not** notified again.

### B. Idempotent marking

1. Seed a period due a lead nudge. Execute the job twice.
2. Assert the notification double recorded exactly **one** send.
3. Same for the grace-expiry warning path.

### C. Fully-done periods are skipped *without* marking

1. Seed a period inside its lead window with **all** items done.
2. Execute — assert no notification and `EndingSoonNotifiedFor` **unchanged/unset**.
3. Untick one item, execute again — assert the nudge now fires. This pins the deliberate
   "skip without marking" behavior, which is easy to break by marking on the skip path.

### D. Hidden periods

Seed an `IsHidden` period that would otherwise qualify for both notifications; assert neither fires.

### E. Boundary conditions

- `ReminderLeadDays == null` → no lead nudge ever.
- A one-day period → no lead nudge (the range constraint makes a lead impossible).
- Days-left uses a **ceiling**: a period 1.2 days out should report 2, not 1. Assert on the payload
  the double received, not just on the fact that something was sent.
- Grace warning fires exactly 1 day before `StreakGraceUntil` — test the day before (no), the day of
  (yes), and after expiry (no; `CheckGrace` in the reset job owns that transition).

### F. Cross-user scope

The job runs unauthenticated and sweeps all users by design (the global `IEntityWithUser` filter is a
no-op without an ambient user). Seed periods for two users, assert both are notified. Pin this;
don't "fix" it.

### G. Timezone (exploratory — may be a real finding)

`now` is captured once as `DateTime.UtcNow` and applied to every user's period regardless of the
user's timezone, while the job is documented as a 09:00 wall-clock sweep. `User.Timezone` exists and
is required. Write a test with two users in materially different offsets whose periods sit either
side of a local-day boundary and assert what you believe correct. **If it fails, do not silently
adjust the assertion** — record it as a new finding; the reviewing agent flagged this Low-confidence
precisely because the evaluation logic lives in `RoutineResetService` and was not verified.

## Conventions

- AAA, one behavior per test, fresh `CreateDbContext()` for post-execute assertions.
- Scenario **A** is expected to fail against current `main`. Tag `[Trait("Status","KnownGap")]` with
  a reference to `CQ-5` if you write it before the fix; remove when fixed.
- Assert on notification **payloads** where the content matters (the "3 of 8 done" body is the
  documented reason this is a sweep rather than a registered reminder).
- Never assert on or log user emails/names — use period ids and user ids.

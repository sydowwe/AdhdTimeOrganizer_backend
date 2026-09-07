# TEST-4 — `ActivityHistoryTimeAttributionSink` (Core's `IActivityTimeAttributionSink`)

> Read `_SHARED-CONTEXT.md` first. Findings: `CQ-5` (day bounding, three ways) in
> `review/history/02-findings.md`.

**Priority: highest in the slice.** Zero tests today, on a live write path, with a suspected hard
failure.

## What you are testing

`AdhdTimeOrganizer.History/application/seam/ActivityHistoryTimeAttributionSink.cs` — History's
implementation of `AdhdTimeOrganizer.Core/application/seam/IActivityTimeAttributionSink`. Two methods:

```csharp
Task AttributeAsync(DbContext db, long userId, long activityId, DateTime windowStart, int activeSeconds, CancellationToken ct);
Task<int> TotalSecondsOnDayAsync(DbContext db, long userId, long activityId, DateOnly day, CancellationToken ct);
```

Both are called by `AdhdTimeOrganizer.Tracking`'s `DesktopActivityHeartbeatEndpoint` (lines 90 and 119).
`SeamWiringTests` proves the seam *resolves* from DI exactly once; nothing proves it *computes*.

Two rules from `AdhdTimeOrganizer.History/docs/summary.md` are load-bearing:

- **It mutates the `DbContext` handed in and must never call `SaveChanges`.** The caller's own save is
  the transaction; that is what keeps a tracked entry atomic with its attribution.
- **The contiguous-window extension rule:** a window whose start equals an existing row's
  `EndTimestamp` (same user, same activity) *extends* that row — `EndTimestamp` moves and `Length` grows
  — rather than inserting a second row. This is a property of the ledger's shape, which is why it lives
  in History and not in the ingest endpoint.

## How to test it

Resolve the seam from DI rather than newing it up, so the test also covers registration:

```csharp
await using var db = CreateDbContext();
using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
var sink = scope.ServiceProvider.GetRequiredService<IActivityTimeAttributionSink>();
```

New file: `AdhdTimeOrganizer.IntegrationTests/Endpoints/ActivityTimeAttributionSinkTests.cs`.
(There is no HTTP route for this — it is a seam, so these are service-level tests over a real
`DbContext`, not `HttpClient` calls.)

## Scenarios

### Happy path

1. **`AttributeAsync` inserts one row when nothing is contiguous.**
   Seed a user + activity. Call with `windowStart = 2026-03-01T10:00:00Z`, `activeSeconds = 300`.
   Assert **before** saving that `db.ChangeTracker.Entries<ActivityHistory>()` has one `Added` entry and
   that **nothing is in the database yet** (query with a *second* `DbContext`) — this is the "must not
   call SaveChanges" rule, and it is the one that silently breaks atomicity if someone "helpfully" adds a
   save. Then `await db.SaveChangesAsync(ct)` and assert the row: `StartTimestamp` 10:00,
   `EndTimestamp` 10:05, `Length.TotalSeconds` 300.

2. **`AttributeAsync` extends a contiguous window instead of inserting.**
   Seed a row 10:00→10:05 (300s). Call with `windowStart = 10:05:00Z`, `activeSeconds = 180`. Save.
   Assert there is still exactly **one** row for that (user, activity), with `EndTimestamp` 10:08 and
   `Length.TotalSeconds` 480. This is the rule the whole class exists for.

3. **A gap inserts a second row.** Same seed, but `windowStart = 10:06:00Z` (one minute after the
   previous end). Assert **two** rows. The rule is equality on the instant, not proximity.

4. **`TotalSecondsOnDayAsync` sums the day.** Seed three rows for one activity on one UTC day
   (300 + 180 + 60) plus one row for the same activity on the *next* day and one row on the same day for a
   *different* activity. Assert the result is `540` — proving both the day filter and the activity filter.

### Edges — these are the ones that matter

5. **⚠️ `TotalSecondsOnDayAsync` must not throw (`CQ-5`, item 1).**
   `ActivityHistoryTimeAttributionSink.cs:55-56` builds its bounds with
   `day.ToDateTime(TimeOnly.MinValue)` — no `DateTimeKind`, so `Unspecified`.
   `activity_history.start_timestamp` is `timestamp with time zone` and the solution does not enable
   `Npgsql.EnableLegacyTimestampBehavior`, so Npgsql is expected to refuse the parameter with
   *"Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone', only UTC
   is supported."*
   **Scenario 4 above is the test that settles this.** If it throws, the finding is confirmed: fix the
   source to `day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)` (copy
   `TodoListItemLoggedTimeSource.cs:22`, one file away, which gets it right) and keep the test.
   If it does *not* throw, say so in the PR — `CQ-5` item 1 is then void and only items 2 and 3 stand.

6. **The last tick of the day (`CQ-5`, item 2).** The current predicate is closed on
   `TimeOnly.MaxValue` (`<= dayEnd`), which drops anything in the final tick.
   `TodoListItemLoggedTimeSource`'s own comment states the correct rule: a half-open range
   `>= from && < to`. Seed a row at `day 23:59:59.9999999Z` and assert it **is** counted. Expect this to
   fail today; fix the source to half-open rather than pinning the bug.

7. **Zero and negative `activeSeconds`.** `AttributeAsync` does no guarding. Call with `0` and assert
   whatever it does is deliberate (a zero-length row is legitimate — `time-of-day` explicitly supports
   one). Call with a negative value and assert it does **not** produce an `EndTimestamp` before
   `StartTimestamp`; if it does, that is a new finding — file it, pin it with `[Trait("Status","KnownGap")]`.

8. **The unique index.** `(UserId, ActivityId, StartTimestamp)` is unique. Call `AttributeAsync` twice
   with the *same* `windowStart` (not contiguous — identical) and save once. Assert the outcome is a
   clean `DbUpdateException` rather than a silent second row, and document which it is.

### Auth / isolation

9. **Cross-user isolation.** Seed the same activity id... you cannot — an activity belongs to one user.
   Instead seed user A and user B each with their own activity, attribute 300s for A, then call
   `TotalSecondsOnDayAsync(db, userB.Id, activityB.Id, day, ct)` and assert `0`. Then attribute for B and
   re-assert A's total is unchanged. The sink filters `UserId` explicitly even though the global filter
   also applies (its remarks say why: the caller passes the id it authenticated, and this must not
   attribute to whoever happens to be ambient) — this test is what pins that.

10. **The ambient-user trap.** These calls run under whatever principal the test scope has. Assert that
    `TotalSecondsOnDayAsync` for user A returns A's total when the *ambient* user is B (or none). If the
    global query filter is doing the scoping rather than the explicit predicate, this catches it.

## Do not

- Do not add a `SaveChangesAsync` to the sink to make a test easier — scenario 1 exists to prevent that.
- Do not widen `TotalSecondsOnDayAsync` to match on anything but `(UserId, ActivityId)`.
- Do not test the desktop heartbeat endpoint here; that is Tracking's surface. If you want end-to-end
  coverage of `PublishAttributionAsync`, that is a separate suite in Tracking's area.

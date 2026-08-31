# TEST-5 — `GET /api/todo-list-item/dashboard-widget`

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

**Status today: zero tests.** Not even the route smoke test covers it
(`TodoListsRouteSmokeTests` hits `/todo-list-item`, `/todo-list`, `/task-priority`).

## The endpoint

- **Route:** `GET /api/todo-list-item/dashboard-widget`
- **Source:** `AdhdTimeOrganizer.TodoLists/application/endpoint/todoListItem/query/GetDashboardTodoListItemEndpoint.cs`
- **Base:** `BaseGetAllEndpoint<TodoListItem, TodoListItemResponse>` with
  `RouteParam => "dashboard-widget"`.
- **Response:** `List<TodoListItemResponse>`.

The whole implementation is two overrides:

```csharp
protected override IQueryable<TodoListItem> Filter(IQueryable<TodoListItem> query) =>
    query.Where(e => !e.IsDone && e.DueDate.HasValue
                  && e.DueDate.Value <= DateOnly.FromDateTime(DateTime.Today.AddDays(3)));

protected override IQueryable<TodoListItem> Sort(IQueryable<TodoListItem> query) =>
    query.OrderBy(e => e.DueDate);
```

## Why this needs tests

`DateTime.Today` is the **server's local date** (`CQ-18`). Every other dashboard in this solution
resolves the user's zone — `HistoryDashboardUserZoneTests`, `TrackingDashboardUserZoneTests` and
`QuietHoursUserZoneAndLateDeliveryTests` all exist to pin exactly that — and the daily recap in this
very slice bounds its day as an explicit half-open UTC range with a comment explaining why
(`GetDailyRecapTodoListItemEndpoint.cs:48-51`). This endpoint does neither. For a user east of the
server it drops a task on its due date for up to a day; west of it, it shows a task a day early.

The `3` is also an unexplained literal where `TodoListSettings` is the obvious home.

## Create `TodoListItemDashboardWidgetTests.cs`

Read `HistoryDashboardUserZoneTests.cs` before starting — it establishes how this suite sets a user's
timezone and how it asserts zone-dependent boundaries. Copy that mechanism rather than inventing one;
if the widget cannot currently take a zone (it cannot), the zone tests below are the KnownGap pins that
say so.

Seed with a fixed `TaskPriority` rank band (e.g. 360) and one activity per item (trap 1).

### 1. The window — inclusive at +3 days, exclusive at +4

Seed five not-done items due today, +1, +3, +4 and one with `DueDate = null`.

Assert the response contains exactly the items due today, +1 and +3. `+4` and the null-due item must be
absent. The `<=` at the boundary is the assertion that catches an off-by-one, and the null case pins
`DueDate.HasValue` — an item with no due date is not "due soon".

Compute the expected dates from `DateTime.UtcNow` in the test, not from a literal.

### 2. Overdue items are included

Seed an item due 30 days ago, not done. `DueDate <= today+3` is true for it, so it appears. That is
almost certainly intended for a "what needs attention" widget, but nothing states it — pin it, and say
in the `DisplayName` that the widget is "due soon **or overdue**", so a future filter change has to
argue with a test.

### 3. Done items are excluded regardless of due date

Seed an item due today with `IsDone = true`. Must be absent. Note the interceptor will stamp
`CompletedTimestamp` on it automatically (see the shared context) — that is fine and irrelevant here.

### 4. Ordering is by `DueDate` ascending

Seed items due +3, today, +1 in that creation order; assert the response order is today, +1, +3.
`Sort` overrides the base, so this pins that the override is actually applied.

### 5. User scoping

Seed a due-today item owned by `ReminderSeedHelper.OtherUserId`. It must not appear. `BaseGetAllEndpoint`
relies on the global `IEntityWithUser` filter here — this is the test that proves it holds for the
widget's filtered query, which is the shape most likely to be rewritten later.

### 6. Auth

`401` unauthenticated; `200` for a plain `User`-role client (`CreateUserRoleClient()`). The endpoint
declares no `Roles(...)`, so it inherits `User + Admin + Root` from the global configurator at
`Program.cs:573-577` — worth one test since that inheritance is invisible at the call site.

### 7. **Timezone — `CQ-18`**

Two pins, both `[Trait("Status","KnownGap")]`.

**East of the server.** Set the test user's zone to something well ahead of the host (e.g.
`Pacific/Kiritimati`, UTC+14) using whatever mechanism `HistoryDashboardUserZoneTests` uses. Seed an item
due on the user's "today + 3", which is a *different calendar date* from the server's today + 3.

Correct behaviour: it appears. Today's behaviour depends on where the CI host is, which is itself the
problem — assert against the user's zone and let it fail where it fails.

**West of the server.** Mirror with a zone well behind (e.g. `Pacific/Niue`, UTC−11) and an item due on
the server's today + 3 but the user's today + 4. Correct behaviour: absent.

```csharp
[Fact(DisplayName = "CQ-18: the 3-day window is bounded in the user's zone, not the server's local date")]
[Trait("Status", "KnownGap")]
```

⚠ These two tests are **host-timezone dependent** as written, which is exactly the defect. Do not
"stabilise" them by pinning the host's zone — that would hide the bug. If the suite cannot run them
deterministically, keep them and say so in the class comment; a test that only fails on some machines is
still better than no record of the boundary.

### 8. Projection completeness

One assertion that the response carries `TaskPriority`, `Steps` and `PairedLeisureActivityId` — the
widget shares `TodoListItemResponse.Projection` with the list read, and `TodoListItemPairingTests:18-19`
notes that a field dropped from that projection reads back as null and is indistinguishable from "not
set".

## If you are fixing

Take the user's zone the way `HistoryDashboardUserZoneTests`' target endpoints do, and move the `3` into
`TodoListSettings` alongside `MaxItems` / `DisplayOrderGap` (`infrastructure/settings/TodoListSettings.cs`).
Adding a bound member there means updating `appsettings.json:11-13` and `DOC-1`'s doc row in the same
pass.

## Related findings

`CQ-18`, `DOC-1`, and the missing partial index `(user_id, is_done, due_date) WHERE is_done = false`
in `02-findings.md` § "Indexes that should exist" — this endpoint's only query is the sole reader of it.

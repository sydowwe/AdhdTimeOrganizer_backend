# AdhdTimeOrganizer.TodoLists — 02 Findings

Point-in-time snapshot, 2026-08-31. Severity: 🔴 blocker · 🟠 important · 🟡 polish.
Every finding carries a `Doc impact:` line naming what the fixer must reconcile in the living docs.

**Previously known / not re-flagged:**

- Auditing is not wired anywhere in this solution (`CLAUDE.md`). Nothing in this slice writes an audit
  trail; that is a solution-level gap, not a TodoLists one. See `AUDIT-1` in
  [03-risks-rollout.md](03-risks-rollout.md) for the one slice-specific consequence.
- The pinned FK constraint name `fk_planner_task_todo_list_items_todolist_item_id` is documented as
  load-bearing in `docs/summary.md` and is correct as-is. Tracked as a rollout risk (`MIG-4`), not a
  finding.
- `Steps` and `TotalCount` are two independent tracks and neither constrains the other.
  `TodoListStepsCrudTests` already pins this deliberately (three tests, with the reasoning in their
  `DisplayName`s). Not re-flagged.
- Custom endpoints in this slice call no `Roles(...)`. That is safe: `Program.cs:573-577` installs a
  global `Endpoints.Configurator` that applies `Roles("User","Admin","Root")` to any endpoint whose
  `AllowedRoles` is empty. Verified, not a finding.
- No `ExecuteUpdateAsync` / `ExecuteDeleteAsync` anywhere in the slice — the ChangeTracker-based
  `CompletedTimestamp` stamp is safe from the bypass its own XML doc warns about.

---

## 5. Security & authorization

### SEC-1 🟠 `CreateTodoListItemEndpoint` accepts another user's `TodoListId` and `TaskPriorityId`

`AdhdTimeOrganizer.TodoLists/application/endpoint/todoListItem/command/CreateTodoListItemEndpoint.cs:27`
(`entity.TodoListId = req.TodoListId;`), with the FK ids arriving unchecked from
`CreateTodoListItemRequest.ToEntity` (`application/dto/request/CreateTodoListItemRequest.cs:18-30`).
`CreateTodoListItemValidator` checks only `GreaterThan(0L)` on both
(`application/validator/CreateTodoListItemValidator.cs:11-15`).

**Why it matters.** `MoveTodoListItemEndpoint:54` explicitly verifies the destination list exists
*through the user-filtered context*, and its comment (`:51-53`) states that this "doubles as the IDOR
guard `MoveToListTodoListItemValidator` doesn't provide". Create has no equivalent. The global
`IEntityWithUser` query filter does not help here — it filters *reads*, and this is a write of a raw
`long` into an FK column whose target row genuinely exists at the database level. The result is a row
owned by the attacker but attached to a victim's list and priority. It then breaks on the way back out:
`TodoListItemResponse.Projection` (`application/dto/response/TodoListItemResponse.cs:60-66`) projects
`e.TaskPriority.Text` / `.Color` through a **required** navigation, and that navigation is subject to
the same global filter — so the join finds nothing and the item either materialises with a null
required member or disappears from the list read. Either way the user's own to-do list is corrupted by
a value they were allowed to send.

**Fix.** Mirror the Move endpoint: in `AfterMapping`, `AnyAsync` both ids through
`_dbContext.Set<TodoList>()` / `Set<TaskPriority>()` (the global filter scopes them) and return a 404
on either miss. `AfterMapping` already returns `bool`, so the plumbing exists — the false path is what
it is for.

`Doc impact: domain-map.md → Invariants (2 — restate as "the filter scopes reads; FK ids on write must be verified explicitly")`

### SEC-2 🟠 `ChangePriorityTodoListItemEndpoint` writes an unverified `TaskPriorityId`

`application/endpoint/todoListItem/command/ChangePriorityTodoListItemEndpoint.cs:30`
(`entity.TaskPriorityId = req.PriorityId;`), validator is `GreaterThan(0)` only
(`application/validator/priority/ChangePriorityTodoListItemValidator.cs:11`).

Same shape as SEC-1 and same consequence, reachable on an existing item rather than a new one. Called
out separately because the fix lands in a different method (`Mapping` is synchronous here, so the check
belongs in an `OnBeforeHandleAsync`/overridden handler or the validator gains a DB-backed rule).

**Fix.** Verify ownership before assigning; 404 on miss. If `BasePatchEndpoint` grows an async
`ValidateAsync` hook, this and SEC-1 should both use it rather than each hand-rolling.

`Doc impact: none`

### SEC-3 🟠 `MoveTodoListItemEndpoint` swallows cancellation and bypasses the base's concurrency handling

`application/endpoint/todoListItem/command/MoveTodoListItemEndpoint.cs:70-78`.

Two problems in one block. First, `catch (Exception ex)` catches `OperationCanceledException` too, so a
client disconnect or a shutdown is reported to the pipeline as an application error via
`DbUtils.HandleException` rather than propagating — the standard `catch (Exception ex) when (ex is not
OperationCanceledException)` guard is missing. Second, the endpoint derives from
`BasePatchEndpoint<TodoListItem, MoveToListTodoListItemRequest>` but overrides `HandleAsync` wholesale
and leaves `Mapping` empty (`:81-83`), so the base contributes nothing — including whatever
`row_version` / concurrency handling it performs on the normal patch path. The comment at `:72-74`
acknowledges half of this ("bypassed here because this endpoint overrides `HandleAsync` directly") but
treats it as a fact to work around rather than a smell.

**Fix.** Exclude `OperationCanceledException` from the catch. Then either move the destination-list
check into an async pre-handle hook and let the base run (preferred — see CQ-7), or stop deriving from
`BasePatchEndpoint` and re-implement the concurrency check explicitly.

`Doc impact: none`

### SEC-4 🟡 The reorder base loads the moved item without the `userId` predicate its own helpers use

`application/endpoint/BaseChangeDisplayOrderTodoListEndpoint.cs:49-50`:

```csharp
var itemToMove = await _dbSet.FirstOrDefaultAsync(x => x.Id == req.MovedItemId, ct);
```

`userId` is fetched on the line above (`:47`) and passed to every *sibling* lookup in the same request —
`GetDisplayOrderById` (`:102`, `:111`, `:119`, `:123`) and `GetGroupIdById` (`:145`) — whose own comment
in `infrastructure/persistence/TodoListExtensions.cs:26-28` says the explicit `userId` is there "so the
filter isn't the only thing standing between this method and a cross-user lookup under
`IgnoreQueryFilters()`". The moved item is the one row in the request that does not get that treatment.

Not currently exploitable — the global filter covers it, and
`ChangeDisplayOrderTodoListItemIdorTests` proves the *sibling* path 404s — but this base is subclassed
from Routines, and the whole point of the defense-in-depth argument is that it should not depend on a
filter that a future `IgnoreQueryFilters()` could lift.

**Fix.** `x => x.Id == req.MovedItemId && x.UserId == userId`.

`Doc impact: none`

### SEC-5 🟡 `UpdateTodoListItemRequest` lets the client set `DisplayOrder` and `DoneCount` directly

`application/dto/request/base/BaseUpdateTodoListRequest.cs:8-11`, applied at
`application/dto/request/UpdateTodoListItemRequest.cs:19-20`.

A PUT can place an item at any `DisplayOrder` — including one that collides with a sibling, which the
whole reorder endpoint exists to avoid — and can set `DoneCount` to any value the validator's
`DoneCount <= TotalCount` rule permits, independently of how many steps are actually ticked. Within one
user's own data, so this is integrity rather than authorization, but it means the ordering invariant is
client-enforced.

**Fix.** Drop `DisplayOrder` from the update DTO (the reorder endpoint owns that column) and derive
`DoneCount` rather than accepting it. If the frontend genuinely round-trips them, note that in the DTO
rather than leaving it implicit.

`Doc impact: domain-map.md → Invariants (new: DisplayOrder is owned by the reorder endpoints)`

---

## 6. Code quality

### CQ-1 🟠 Merging two activities that both have an item on one list violates the unique index

`application/seam/TodoListActivityReferenceSource.cs:42-72`.

`RepointAsync` sets `item.ActivityId = survivorId` for every item referencing any merged id, with no
awareness of `ToDoListItemConfiguration.cs:47-48`:

```csharp
builder.HasIndex(t => new { t.UserId, t.ActivityId, t.TodoListId }).IsUnique();
```

**Failure scenario.** A user has "Reading" and "reading" as two activities — which is the *only* reason
the merge feature exists — and a to-do item for each, both on their "Books" list. Merging the two
repoints both items to the survivor, producing two rows with identical `(UserId, ActivityId,
TodoListId)`. The index is checked immediately (as `TaskPriorityReorderTests` establishes for
`TaskPriority`), so `SaveChanges` throws and the entire merge transaction rolls back. The user cannot
merge those activities at all, and the error surfaces as whatever `ActivityMergeEndpoint` maps a
`DbUpdateException` to.

`ActivityMergeTests:196` covers the adjacent case — *one* row referencing *two* merged activities
through its two columns — and correctly asserts the collapse. It does not cover *two* rows collapsing
onto one key. `ActivityProfiles` has an explicit resolution for the equivalent problem
(`ActivityMergeTests:229`, "the survivor's own profile wins and the merged one is deleted"); TodoLists
has none.

**Fix.** Detect the collision before saving: group the affected items by `(UserId, TodoListId)` after
repointing, keep one per key (the survivor's own item if it exists, else the oldest) and delete the
rest, matching the profile seam's precedent. Whatever is deleted must still be counted in the returned
`repointed` total so the snackbar count stays honest.

`Doc impact: domain-map.md → Invariants (new: the (UserId, ActivityId, TodoListId) uniqueness is what the merge seam must collapse onto)`

### CQ-2 🟠 `GetNextDisplayOrder`'s `!= 0` sentinel conflates "empty group" with "an item at order 0"

`infrastructure/persistence/TodoListExtensions.cs:17-18`:

```csharp
var lastOrder = await query.MinAsync(e => (int?)e.DisplayOrder, ct) ?? 0;
return lastOrder != 0 ? lastOrder - settings.DisplayOrderGap : settings.DisplayOrderStart;
```

`MinAsync` over `int?` already returns `null` for an empty group — that is the correct signal, and the
`?? 0` throws it away in favour of a magic number that is a legal `DisplayOrder`.

**Failure scenario, and it is reachable by construction.** `appsettings.json:11-13` sets
`MaxItems: 100`, `DisplayOrderGap: 1000000`, so `DisplayOrderStart` is `100_000_000` and each new item
in a priority group sits one gap below the last. After exactly **100** items the minimum is `0`, the
sentinel misfires, and item 101 is handed `100_000_000` — back at the very top of the group, colliding
with item 1's original order. Every subsequent item then repeats the cycle. `MaxItems` is literally the
number at which this breaks; nothing enforces it as a cap.

**Fix.**

```csharp
var lastOrder = await query.MinAsync(e => (long?)e.DisplayOrder, ct);
return lastOrder.HasValue ? lastOrder.Value - settings.DisplayOrderGap : settings.DisplayOrderStart;
```

Same change is needed in the three inlined copies in the dev seeder (CQ-13).

`Doc impact: none`

### CQ-3 🟠 The same method casts a `bigint` column to `int` in SQL

`infrastructure/persistence/TodoListExtensions.cs:17` — `MinAsync(e => (int?)e.DisplayOrder, ct)` on
`BaseTodoListItem.DisplayOrder`, which is `long` (`domain/model/entity/BaseTodoListItem.cs:11`).

EF translates this to `CAST(display_order AS integer)`. Once any row in the group holds a value outside
`int` range — which the descending-by-gap scheme reaches after roughly 2,200 items per group with the
configured gap, or immediately if `DisplayOrderGap` is ever raised — Postgres raises
`integer out of range` and item creation fails for that whole priority group. The projection also
defeats any index on `display_order`, since the cast is not sargable.

**Fix.** `(long?)`. Folded into CQ-2's fix; called out separately because the `int` cast is also
present in the dev seeder and would survive a partial fix.

`Doc impact: none`

### CQ-4 🟠 The rebalance path builds an untranslatable LINQ expression and throws at runtime

`application/endpoint/BaseChangeDisplayOrderTodoListEndpoint.cs:152`:

```csharp
var items = await query.Where(e => GroupFilterExpression.Compile()(e) == groupId)
```

`GroupFilterExpression` is an `Expression<Func<TEntity, long>>`. Calling `.Compile()` inside a lambda
passed to `Where` on an `IQueryable` puts a `MethodCallExpression` for `Compile` and an
`InvocationExpression` into the tree. EF Core cannot translate either; it raises
`InvalidOperationException: The LINQ expression ... could not be translated`. The correct construction
is to compose the expression tree (or, since the shape is fixed, to pass the group id through as a
plain predicate).

**Why it survived.** `RebalanceDisplayOrdersAsync` is only reached from `CalculateNewOrderAsync:130-134`,
when the arithmetic midpoint between two neighbours equals one of them — i.e. when the gap between two
adjacent items is exhausted. With `DisplayOrderGap: 1000000` that takes ~20 successive reorders between
the same pair, so it does not show up in casual use. **No test has ever executed this branch**
(`01-testing.md` §8) — the slice's only reorder test asserts a 404.

Nothing is corrupted (the throw precedes any mutation, inside the transaction opened at `:60`), but the
user gets a 500 and reordering that pair becomes permanently impossible until someone edits the rows by
hand.

**Fix.**

```csharp
var items = await query.Where(GroupFilterExpressionEquals(groupId.Value))
    .OrderByDescending(e => e.DisplayOrder).ToListAsync(ct);
```

building the predicate with `Expression.Lambda<Func<TEntity,bool>>(Expression.Equal(GroupFilterExpression.Body,
Expression.Constant(groupId.Value)), GroupFilterExpression.Parameters)`. Cache it — it is fixed per
closed generic type. `TEST-3` must cover the branch.

`Doc impact: none`

### CQ-5 🟠 `CalculateNewOrderAsync` recurses after a rebalance with no depth guard

`application/endpoint/BaseChangeDisplayOrderTodoListEndpoint.cs:129-135`. The comment at `:62` says
"we'll rebalance and retry **once**"; the code recurses unconditionally. If the rebalance does not
create a gap — which it silently will not when `GetGroupIdById` returns null and
`RebalanceDisplayOrdersAsync` returns early at `:146-147` — the method re-enters with identical
arguments and recurses until the stack overflows, taking the process down rather than returning a 409.
The `Summary` block even advertises a 409 for this case (`:36`) that the code never produces.

Currently masked by CQ-4 (the rebalance throws first). Fixing CQ-4 without this one converts a 500 into
a process crash.

**Fix.** Thread a `depth` parameter (or a `bool alreadyRebalanced`) and return
`Result<long>.Error(ResultErrorType.Conflict, …)` on the second failure, which is the 409 the docs
already promise.

`Doc impact: none`

### CQ-6 🟠 Changing an item's priority moves it between `DisplayOrder` groups without reordering it

`application/endpoint/todoListItem/command/ChangePriorityTodoListItemEndpoint.cs:28-31` sets
`TaskPriorityId` and nothing else. But `DisplayOrder` is scoped *by* `TaskPriorityId` — see
`ChangeDisplayOrderTodoListItemEndpoint.cs:10` (`GroupFilterExpression => e.TaskPriorityId`) and
`TodoListExtensions.GetNextDisplayOrder(dbSet, settings, userId, taskPriorityId, ct)`.

**Failure scenario.** An item sits at `DisplayOrder = 97_000_000` in "This week". The user drags it to
"Today", whose items occupy `100_000_000`, `99_000_000`, `98_000_000`. The item lands in the middle of
Today's list rather than at the position the drag implied, and its old neighbours in "This week" keep a
hole. `MoveTodoListItemEndpoint:63-64` does exactly the right thing for the analogous list move
(recomputes `DisplayOrder` for the destination group); this endpoint does not.

**Fix.** Recompute in `Mapping`'s async counterpart:
`entity.DisplayOrder = await dbSet.GetNextDisplayOrder(_settings, User.GetId(), req.PriorityId, ct)`.
`MoveTodoListItemEndpoint` is the template.

`Doc impact: domain-map.md → Navigation index (note that TaskPriorityId is the DisplayOrder grouping key, so the two columns move together)`

### CQ-7 🟠 `MoveTodoListItemEndpoint` derives from a base it then fully replaces

`application/endpoint/todoListItem/command/MoveTodoListItemEndpoint.cs:15-16` declares
`BasePatchEndpoint<TodoListItem, MoveToListTodoListItemRequest>`, then overrides `HandleAsync` (`:37`)
and leaves `Mapping` empty (`:81-83`). The base supplies the route-id fetch, the Result-pattern save,
the exception mapping and the role gate — and the override re-implements the first three by hand
(`FindAsync` at `:41`, raw `SaveChangesAsync` at `:67`, a copied `DbUtils.HandleException` block at
`:75`, with a comment at `:72-74` explaining it is a copy). Only the role gate still comes from the
base.

Per `CLAUDE.md`, "check `framework/Sydowwe.Framework/application/endpoint/base/` before writing a
custom one" — and the custom-ness here is not justified by a transaction or by balance logic, only by
the need for one extra existence check before the mapping runs.

**Fix.** The clean shape is an async pre-mapping hook on `BasePatchEndpoint` (mirroring
`BaseCreateEndpoint.AfterMapping`, which already returns `bool` for exactly this purpose). Add it once
in the framework, then this endpoint becomes ~15 lines and SEC-1/SEC-2/SEC-3 all get their fix for free.
That is one framework change paying for four findings — note that `framework/` is a submodule, so it is
a two-repo commit.

`Doc impact: none`

### CQ-8 🟠 `GetFilterSortTodoListCategoryEndpoint` hand-rolls what its sibling gets from a base

`application/endpoint/todoListCategory/query/GetFilterSortTodoListCategoryEndpoint.cs:11-56` is a raw
`Endpoint<BaseFilterSortRequest<…>, List<…>>` doing manual user scoping, manual filter application and
manual sorting. Its direct sibling `FilterSortTodoListEndpoint.cs:8-10` derives from
`BaseFilterSortEndpoint<TodoList, TodoListResponse, TodoListFilterRequest>` and overrides only
`ApplyCustomFiltering` — twelve lines against fifty-six.

The one thing the base cannot do is append the synthetic `Other` row (`Id = -1`, `:46-53`), which is a
sentinel that also appears in `TodoListFilterRequest.CategoryId` (`dto/filter/ToDoListFilterRequest.cs:9`,
"`-1` = uncategorized"). Two DTOs and two endpoints encode the same magic number independently.

**Fix.** Derive from `BaseFilterSortEndpoint`, put the `HideEmpty` clause in `ApplyCustomFiltering`, and
override whatever post-projection hook the base offers to append the sentinel row (add one if it has
none). Promote `-1` to a named constant shared by the filter DTO and the response.

`Doc impact: domain-map.md → Glossary (add the -1 "uncategorized" sentinel; it is currently only in a DTO comment)`

### CQ-9 🟡 `IsDoneLogic` compares `DoneCount` to `TotalCount` with `==` in one arm and `>=` in another

`application/endpoint/BaseToggleIsDoneTodoListEndpoint.cs:94` (`if (entity.DoneCount >= entity.TotalCount)`)
versus `:113` (`if (entity.DoneCount == entity.TotalCount)`).

The no-force arm's `==` means an item whose `DoneCount` has somehow overshot `TotalCount` — reachable
today by a PUT, since SEC-5 lets the client set `DoneCount` directly, or by lowering `TotalCount` on an
item that is already partly done — can never be ticked to `IsDone`. It just keeps incrementing.
`ApplyStepToggle` (`BaseToggleStepIsDoneEndpoint.cs:89`) uses `>=`, so two of the three comparison sites
agree and one does not.

**Fix.** `>=` at `:113`. The invariant fuzz test at `TodoListToggleAndDoneCountTests:171` should be
extended to seed an already-overshot row.

`Doc impact: none`

### CQ-10 🟡 The step endpoints call `SaveChangesAsync` directly instead of the Result helpers

`application/endpoint/steps/BaseCreateStepEndpoint.cs:46`,
`BaseUpdateStepEndpoint.cs:45`, `BaseDeleteStepEndpoint.cs:42`, plus
`BaseToggleIsDoneTodoListEndpoint.cs:44` and `BaseToggleStepIsDoneEndpoint.cs:50`.

`CLAUDE.md` calls for "CRUD via the `DbContextHelper` Result-returning helpers, not raw
`SaveChangesAsync`". `BaseChangeDisplayOrderTodoListEndpoint:73` in the same folder does use
`dbContext.UpdateEntityAsync(...)` and maps the failure properly, so the slice is internally
inconsistent. Practical consequence: a `DbUpdateException` from any of these five sites escapes to the
pipeline as a 500 instead of the mapped 409 the rest of the API returns — the exact difference
`MoveTodoListItemEndpoint:72-74` went out of its way to preserve.

**Fix.** Route all five through the helper and map `result.ErrorType` via `EndpointHelper.ToStatusCode`.

`Doc impact: none`

### CQ-11 🟡 Dead code in `BaseToggleStepIsDoneEndpoint`

`application/endpoint/BaseToggleStepIsDoneEndpoint.cs:99-100` is a commented-out call to
`OnItemCompleted`; `:108-110` is the `protected virtual OnItemCompleted` it would have called, which no
subclass overrides. With them gone, the `DateTime now` parameter threaded through `HandleAsync:45`,
`BeforeToggle`, `ApplyStepToggle` and `OnItemCompleted` has no reader either — `CompletedTimestamp` is
the interceptor's job, which is precisely why the call was commented out.

**Fix.** Delete both, then drop the `now` parameter from `ApplyStepToggle` and `OnItemCompleted`.
`BeforeToggle` keeps its `now` — Routines' period-reset override uses it.

`Doc impact: none`

### CQ-12 🟡 `TodoListGroupedByCategoryResponse` is dead

`application/dto/response/TodoListGroupedByCategoryResponse.cs` — zero references across the whole
solution (verified by search, excluding `bin`/`obj`). No endpoint returns it and no test names it.

**Fix.** Delete it. If grouped-by-category reads are planned, the shape belongs with the endpoint that
introduces them.

`Doc impact: none`

### CQ-13 🟡 The dev seeder's name, its comment and its arithmetic are all wrong

`infrastructure/persistence/seeder/dev/TodoListSeeder.cs`. Three separate things:

1. **The class is `TodoListItemSeeder`** (`:12`) in a file called `TodoListSeeder.cs`, and
   `docs/domain-map.md:79` calls it `dev/TodoListSeeder`. Per `CLAUDE.md`, files are assigned by what
   the type *is* — but here the type and the file disagree with each other. Rename the file.
2. **`:28` says "TruncateTable already removed existing data for this user".** It did not:
   `TruncateTableCascadeAsync<TodoListItem>()` (`:22`) truncates the table for *every* user. Harmless in
   dev, but the comment will mislead whoever next reads it for the per-user seeder contract.
3. **`:80-93` inlines three copies of `GetNextDisplayOrder`** rather than calling the extension six
   lines away in the same project — carrying both CQ-2's `!= 0` sentinel and CQ-3's `(int?)` cast into a
   third, fourth and fifth location. A fix to the extension will silently not reach them.

**Fix.** Rename the file; correct the comment; replace `:80-93` with three calls to
`dbContext.Set<TodoListItem>().GetNextDisplayOrder(settings.Value, userId, priorityId, ct)`.

`Doc impact: domain-map.md → Seeding (the dev seeder's class name); summary.md → Gotchas (same)`

### CQ-14 🟡 `TaskPriorityRequest.Priority` is `short` where everything around it is `int`

`application/dto/request/taskPriority/TaskPriorityRequest.cs:9` declares `short Priority`, assigned at
`:16` to `TaskPriority.Priority`, which is `int` (`domain/model/entity/TaskPriority.cs:10`), and
projected back as `int` (`dto/response/TaskPriorityResponse.cs:9`). `TaskPriorityValidator:11-12`
enforces a floor of 1 and no ceiling, so the effective range is `1..32767` on the way in and `int` on
the way out — asymmetric, and the asymmetry is only visible by reading three files.

`TaskPriorityReorderTests:69` has to write `Priority = (short)9999` with a cast to make the round-trip
compile, which is the smell surfacing in a test.

**Fix.** Make the DTO `int` and give the validator an explicit `InclusiveBetween(1, …)` matching whatever
the product cap actually is.

`Doc impact: none`

### CQ-15 🟡 `TaskPriorityConfiguration` skips `IsManyWithOneUser()` that its siblings call

`infrastructure/persistence/configuration/TaskPriorityConfiguration.cs:11-12` calls
`BaseTextColorEntityConfigure()` and then goes straight to indexes.
`TodoListConfiguration.cs:13` and `TodoListCategoryConfiguration.cs:13` both call `IsManyWithOneUser()`
immediately after their base configure, and `ToDoListItemConfiguration.cs:22` does too. `TaskPriority`
is a `BaseEntityWithUser` like all three, so its user relationship and delete behaviour are currently
whatever EF's conventions pick rather than what the Core helper declares.

Per `CLAUDE.md` the delete behaviour of the `Activity` FK family is deliberate and helper-driven; an
entity opting out of the helper by omission is exactly the kind of thing that compiles and diverges
silently.

**Fix.** Add `builder.IsManyWithOneUser();`. Generate a migration and read it — if it is empty, the
conventions happened to agree and the call is now a guard; if it is not, that diff is the finding.

`Doc impact: none`

### CQ-16 🟡 `UpdateTodoListItemRequest.UpdateEntity` bypasses `SetDone`

`application/dto/request/UpdateTodoListItemRequest.cs:18-20` writes `IsDone`, `DoneCount` and the
`Steps` collection as three independent assignments.
`BaseTodoListItem.SetDone` (`domain/model/entity/BaseTodoListItem.cs:20-27`) exists specifically to
"snap `IsDone`, `DoneCount` … and every step's `IsDone` together, so the three representations of
'done' can't drift apart the way they did when callers set them separately" — and this is a caller
setting them separately. A PUT can produce `IsDone = true` with every step unticked.

The interceptor still stamps `CompletedTimestamp` correctly (it watches only `IsDone`), so the recap is
unaffected; the damage is confined to the item's own display.

**Fix.** Have `UpdateEntity` assign the non-done fields, then call `e.SetDone(IsDone)` last, after the
steps have been replaced.

`Doc impact: none`

### CQ-17 🟡 Validators are attached inconsistently

`Validator<T>()` is called explicitly in `Configure()` for the create/update/patch endpoints
(`CreateTodoListItemEndpoint.cs:21`, `MoveTodoListItemEndpoint.cs:25`, and six others) but never for the
three step bases, which rely on FastEndpoints resolving `CreateStepValidator` /
`UpdateStepValidator` / `StepItemAndStepIdValidator` by request type. Both work; having both patterns
in one slice means a reader cannot tell from the endpoint whether validation runs.
`TodoListItemPairingTests:22-23` explicitly flags hand-attached validators as a silent-failure source
("an unattached one is not a build error").

**Fix.** Attach all of them explicitly in `Configure()`, matching the majority pattern.

`Doc impact: none`

### CQ-18 🟠 `GetDashboardTodoListItemEndpoint` uses server-local `DateTime.Today`

`application/endpoint/todoListItem/query/GetDashboardTodoListItemEndpoint.cs:13`:

```csharp
query.Where(e => !e.IsDone && e.DueDate.HasValue && e.DueDate.Value <= DateOnly.FromDateTime(DateTime.Today.AddDays(3)))
```

`DateTime.Today` is the *server's* local date. Every other dashboard in this solution resolves the
user's zone — `HistoryDashboardUserZoneTests`, `TrackingDashboardUserZoneTests` and
`QuietHoursUserZoneAndLateDeliveryTests` exist precisely to pin that — and the daily recap in this very
slice bounds its day as an explicit half-open UTC range with a comment explaining why
(`GetDailyRecapTodoListItemEndpoint.cs:48-51`). This endpoint does neither.

For a user east of the server the widget drops a task on its due date for up to a day; for one west of
it, it shows a task a day early. The `3` is also an unexplained literal where `TodoListSettings` is the
obvious home.

**Fix.** Take the user's zone the way the History dashboards do, and move the horizon to
`TodoListSettings`.

`Doc impact: domain-map.md → Navigation index (dashboard-widget row should note the user-zone dependency)`

---

## 9. Performance hotspots

| Where | Issue | Impact | Mitigation | ID |
|---|---|---|---|---|
| `GetAllTodoListItemEndpoint.cs:33-38` | Unpaged read of every item the user owns, projecting the full `Activity` → `Role` → `Category` graph plus the owned `Steps` JSON per row | Payload and query cost grow without bound; this is the route the main to-do view renders from | Page it, or at minimum require `TodoListId`; project `Role`/`Category` as ids and let the client join against the lookup it already holds | `PERF-1` |
| `BaseChangeDisplayOrderTodoListEndpoint.cs:151-162` | Rebalance loads the whole priority group, mutates in memory, `UpdateRange`s it → one `UPDATE` per row, on the request thread, inside the open transaction | O(n) round trips while holding row locks; with 100 items/group that is 100 statements a user waits on | Single `ExecuteUpdateAsync` with a computed `row_number()` — **but** see the ChangeTracker caveat below | `PERF-2` |
| `TodoListResponse.Projection:23-24` | Two correlated subqueries per list (`Count()` and `Count(i => i.IsDone)`) | Two extra scans per row on every list read and every `filter-sort` page | One grouped join, or a `FILTER (WHERE is_done)` aggregate; needs the index below | `PERF-3` |
| `TodoList.cs:17-18` | `ItemCount` / `CompletedCount` are unmapped computed properties over `TodoListItemColl` — a second, in-memory copy of the projection above | Nothing reads them today, but any caller that does silently triggers a full collection load per list | Delete them; the projection is the only correct source | `PERF-3` |
| `GetFilterSortTodoListCategoryEndpoint.cs:35`, `:42-43` | The `HideEmpty` clause is a correlated `EXISTS` over `todo_list`, and a second `AnyAsync` round trip fires unconditionally on every call | Two queries where one would do; the `EXISTS` has no supporting index | Fold the uncategorized check into the same query as a `bool` column; add the index below | `PERF-4` |
| `TaskPriorityConfiguration.cs:22` | `HasIndex(t => t.Priority)` — a non-leading-`UserId` index on a strictly user-scoped lookup | No query in the slice can use it (every read is filtered on `user_id` first); pure write and storage cost on a table with ~4 rows per user | Drop it; the composite at `:19` already serves every access path | `PERF-5` |
| `TodoListExtensions.cs:17` | `CAST(display_order AS integer)` in the `MIN` projection | Not sargable — defeats any index on `(user_id, task_priority_id, display_order)`, so every item creation scans the group | Fixed by CQ-3 | `PERF-6` |

### Indexes that should exist

| Table | Index | Serves |
|---|---|---|
| `todo.todo_list` | `(user_id, category_id)` | `GetFilterSortTodoListCategoryEndpoint`'s `HideEmpty` `EXISTS` and its uncategorized probe (`PERF-4`); `FilterSortTodoListEndpoint`'s `CategoryId` arm |
| `todo.todo_list_item` | `(user_id, task_priority_id, display_order)` | The `MIN(display_order)` in `GetNextDisplayOrder` and the rebalance's `ORDER BY display_order DESC`. The existing `(UserId, TaskPriorityId)` index (`ToDoListItemConfiguration.cs:50`) covers the filter but not the sort — extend it rather than adding a third |
| `todo.todo_list_item` | `(user_id, todo_list_id, display_order)` | `GetAllTodoListItemEndpoint`'s filter + `OrderBy(DisplayOrder)`, and `TodoListResponse`'s two count subqueries (`PERF-1`, `PERF-3`) |
| `todo.todo_list_item` | `(user_id, is_done, due_date) WHERE is_done = false` | `GetDashboardTodoListItemEndpoint`'s only query (`CQ-18`); partial, because the widget never reads done items |

`(user_id, completed_timestamp)` for the daily recap already exists and is correctly justified in
`ToDoListItemConfiguration.cs:52-55`. `(paired_leisure_activity_id)` exists for the `SetNull` and is
likewise justified (`:44-45`). Both are good precedent for the four above.

**Caveat on `PERF-2`.** The obvious `ExecuteUpdateAsync` fix for the rebalance would bypass the
ChangeTracker — which is safe *here*, because the rebalance touches only `DisplayOrder` and
`TodoListItemCompletionInterceptor` watches only `IsDone`. Say so in a comment if you do it, because
`docs/summary.md` and the interceptor's own XML doc both state the general rule the other way round.

---

## Code/doc drift

### DOC-1 🟡 `TodoListSettings`' documented members do not exist

`docs/domain-map.md:49` — "`settings/TodoListSettings.cs` | `DisplayOrderStart` / `DisplayOrderGap`."
The record (`infrastructure/settings/TodoListSettings.cs:3-9`) has `MaxItems` and `DisplayOrderGap` as
bound members; `DisplayOrderStart` is a *derived* `MaxItems * DisplayOrderGap`. Anyone trying to set
`DisplayOrderStart` in `appsettings.json` gets a silently-ignored key
(`appsettings.json:11-13` correctly sets the other two).

**Which side is wrong:** the doc. **Fix:** `` `MaxItems` / `DisplayOrderGap`, with `DisplayOrderStart` derived from both ``.
Worth saying explicitly, because `MaxItems` reads like a cap and is not enforced as one — it is only a
multiplier, which is the confusion at the heart of CQ-2.

`Doc impact: domain-map.md → Configuration table (TodoListSettings row)`

### DOC-2 🟡 The dev seeder is documented under a name no type has

`docs/domain-map.md:79` names `dev/TodoListSeeder`; the class is `TodoListItemSeeder`. See CQ-13 —
here the code is the side to change (rename the file to match the class), and the doc follows.

**Which side is wrong:** both. **Fix:** rename `TodoListSeeder.cs` → `TodoListItemSeeder.cs`, then update
the doc to `dev/TodoListItemSeeder`.

`Doc impact: domain-map.md → Seeding; summary.md → Bounded context ("the TaskPrioritySeeder / TodoListSeeder pair")`

### DOC-3 🟡 The navigation index's paths are namespaces, not folders

`docs/domain-map.md:28`, `:43-50` point at `domain/model/entity/todoList/`,
`infrastructure/persistence/configuration/todoList/…`, `configuration/extensions/…`, `extensions/…`.
On disk the entities are in `domain/model/entity/` (flat), the configurations in
`infrastructure/persistence/configuration/` (flat), and both extension files sit directly in
`infrastructure/persistence/`. The *namespaces* match the doc — the folders do not.

That matters more than usual here, because the index's stated job is "open only what you need": a
reader following `infrastructure/persistence/configuration/extensions/TodoListEntityConfigurationExtensions.cs`
finds nothing. `docs/summary.md:60` has the same issue
(`infrastructure/persistence/interceptor/` is correct; the configuration paths around it are not).

**Which side is wrong:** the doc, unless someone wants to move the files to match the namespaces (which
would be the tidier outcome and is a no-op for the build).

`Doc impact: domain-map.md → Configuration table + Entities heading (paths)`

### DOC-4 🟡 The interceptor's five documented `IsDone` writers are not the five that exist

`domain/model/entity/TodoListItem.cs:31-33`, `infrastructure/persistence/interceptor/TodoListItemCompletionInterceptor.cs:13-16`
and `docs/domain-map.md:93-95` all list the same five: the two toggle bases, the step toggle,
`BaseTodoListItem.SetDone`, `UpdateTodoListItemRequest`, and the host's `ActivityTimeRecordedEventHandler`.

`SetDone` is not a peer of the others — it is the shared helper the host's fan-out handlers call
(`CompletionFanOutTests:82`), not an independent write site, and no endpoint in this slice calls it
(the toggle bases use `IsDoneLogic`, which duplicates it — see CQ-16). Listing it alongside the
endpoints implies this slice's toggles go through it, which is the opposite of the truth and is exactly
the misreading that lets CQ-16 look harmless.

**Which side is wrong:** the doc (three copies of it). **Fix:** list the actual write sites and mark
`SetDone` as the helper the *host's* handlers use.

`Doc impact: domain-map.md → Invariants (5); summary.md → Gotchas (the interceptor bullet); TodoListItem.cs + TodoListItemCompletionInterceptor.cs XML docs`

### DOC-5 🟠 Invariant 2 asserts a scoping rule the slice does not follow in either direction

`docs/domain-map.md:84-87`: "**Per-user scoping is the DbContext's job**, via the global filter on
`IEntityWithUser` — not the endpoints (`ApplyUserScoping` is a no-op virtual)."

The code disagrees both ways:

- **Scopes by hand anyway** (correctly): `TodoListExtensions.cs:26-34` (with a comment arguing the
  invariant is not sufficient), `GetDailyRecapTodoListItemEndpoint.cs:53-59` (likewise, at length),
  `GetAllTodoListItemEndpoint.cs:34`, `GetFilterSortTodoListCategoryEndpoint.cs:32`, all three step
  endpoints' `GetParentQuery`, and `MoveTodoListItemEndpoint.cs:54`.
- **Relies on the filter alone**: `BaseChangeDisplayOrderTodoListEndpoint.cs:49` (SEC-4),
  `ToggleIsDoneTodoListItemEndpoint.cs:13-16`, `ToggleStepIsDoneTodoListItemEndpoint.cs:14-17`, and the
  four `GetById*` endpoints (which at least say so in a comment).

So the invariant is neither what the code does nor what the code's own comments argue for. As written
it also actively licenses SEC-1 and SEC-2 — "scoping is the DbContext's job" is precisely the reasoning
under which an unchecked FK id on a write looks fine.

**Which side is wrong:** the doc. **Fix:** restate as the two-part rule the code actually follows —
*(a)* the global filter is the floor and covers reads of `IEntityWithUser` entities; *(b)* any query
whose rows the response is built from, and **any FK id accepted on a write**, must be scoped or
verified explicitly, because the filter can be lifted with `IgnoreQueryFilters` and does not apply to
writes at all. Then fix SEC-4 so the exceptions list is empty.

`Doc impact: domain-map.md → Invariants (2 — rewrite)`

---

## 🟡 Nits appendix

- `infrastructure/persistence/configuration/ToDoListItemConfiguration.cs` — the file name says
  `ToDoList`, the class says `TodoListItem`. `domain-map.md:44` already notes this; renaming the file is
  a free fix now that the slice has moved once.
- `BaseChangeDisplayOrderTodoListEndpoint.cs:100` returns `standardGap` for an empty list, where
  creation uses `DisplayOrderStart` — two different "first item" values for the same column.
- `BaseToggleIsDoneTodoListEndpoint.cs:43` calls `UpdateRange` on entities that are already tracked and
  modified, forcing every column into the `UPDATE`. Same at `BaseToggleStepIsDoneEndpoint.cs:49` and
  `BaseChangeDisplayOrderTodoListEndpoint.cs:161`.
- `BaseToggleIsDoneTodoListEndpoint.cs:30-35` returns 404 only when *no* requested id resolved; a batch
  with one valid and one foreign id silently succeeds on the subset.
- `GetSelectOptionsTaskPriorityEndpoint.cs:8` and `GetSelectOptionsTodoListEndpoint.cs:7` name their
  constructor parameter `appDbContext` — the one name `docs/summary.md:34` says never appears in this
  project.
- `GridTaskPriorityEndpoint.cs:15` and `FilterSortTodoListEndpoint.cs:14` use case-sensitive
  `Contains` for name search; `GetFilterSortTodoListCategoryEndpoint.cs:38` does the same. Postgres
  `LIKE` is case-sensitive, so all three name filters miss on capitalisation.
- `TodoListSeeder.cs:116-118` seeds a "Bug fixing" item with `TotalCount = 3`, `DoneCount = 0` and one
  step already `IsDone = true` — an internally inconsistent fixture that no toggle path can produce.
- `TodoListStepsCrudTests.cs:34` uses `Random.Shared.Next(1000, 999_000)` for a column with a unique
  index in a shared-database suite. Low-probability flake; a monotonic counter would be free.

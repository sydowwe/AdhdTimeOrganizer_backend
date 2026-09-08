# AdhdTimeOrganizer.TodoLists — 02 Findings

Point-in-time snapshot, 2026-08-31. Severity: 🔴 blocker · 🟠 important · 🟡 polish.
Every finding carries a `Doc impact:` line naming what the fixer must reconcile in the living docs.

## Recheck — pass 2, 2026-08-31

Every finding below was re-read against the source, not carried forward on trust.

**Nothing has been fixed in code.** The slice's last code commit is `e27158d` (2026-08-19); today's
`6079f93` touched `AdhdTimeOrganizer.TodoLists/docs/` only. So **every `SEC-`, `CQ-` and `PERF-`
finding is confirmed at the file and line stated**, with the corrections listed here.

> **Later than this pass — 2026-09-07:** `CQ-11` has been **corrected and fixed**. Its premise ("no
> subclass overrides `OnItemCompleted`") was wrong, and its recommended fix — deleting the hook — would
> have silently removed the routine item streak. Restoring the commented-out call was the actual fix. The
> line numbers below `BaseToggleStepIsDoneEndpoint.cs:99` have shifted accordingly.
>
> **Also 2026-09-07:** `CQ-10` is **fixed**, all five sites. Line numbers in all five files have shifted.
> The sentence above still holds for every other finding in this file.
>
> **2026-09-09:** `CQ-7`, `SEC-1`, `SEC-2`, `SEC-3`, `CQ-6` and `DOC-5` are **fixed as one batch** —
> `CQ-7`'s framework hook is what made the other five cheap, exactly as it predicted. `SEC-1` was then
> extended to the PUT surface, which the finding does not name but which has the same hole (see its
> note). `SEC-4` and `SEC-5` are untouched. Line numbers in `MoveTodoListItemEndpoint.cs`,
> `ChangePriorityTodoListItemEndpoint.cs`, `CreateTodoListItemEndpoint.cs` and
> `UpdateTodoListItemEndpoint.cs` are all stale below; the four files were rewritten.

| Change in this pass | Where |
|---|---|
| `DOC-1` **resolved** by `6079f93` — the settings row now reads `MaxItems` / `DisplayOrderGap` with `DisplayOrderStart` derived | `domain-map.md:53` |
| `DOC-3` **resolved** by `6079f93` — both docs now give flat, real paths and say so explicitly | `domain-map.md:32`, `:46-54` |
| `DOC-2` **half resolved** — the doc now names both the class and the file; the file rename is still open, and stays inside `CQ-13` | `domain-map.md:142` |
| `DOC-4` **narrowed** — `summary.md` no longer enumerates the five writers, so only the domain map and two XML docs still do | see `DOC-4` |
| `DOC-5` **renumbered** — the invariant it contradicts is now **3**, at `domain-map.md:154-157`, not 2 at `:84` | see `DOC-5` |
| `CQ-2` **off-by-one corrected** — the 101st item is fine; the **102nd** is the collision | see `CQ-2` |
| `CQ-16` **rewritten** — the original mis-stated where `Steps` is written; the defect is real but lives one file over | see `CQ-16` |
| `CQ-19` **new** 🟠 — a PUT regenerates every step's `Guid`, invalidating the step routes | see `CQ-19` |
| `DOC-6` **new** 🟡 — `summary.md` claims the API cannot produce `DoneCount` with a null `TotalCount`; two endpoints do | see `DOC-6` |
| Stale doc line refs repaired throughout — `summary.md` was rewritten today, so every `summary.md:NN` in the original pass pointed at the old file | this file, `03-risks-rollout.md` |

---

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

### SEC-1 🟠 ✅ FIXED 2026-09-09 · `CreateTodoListItemEndpoint` accepts another user's `TodoListId` and `TaskPriorityId`

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

**Fixed 2026-09-09** in `BeforeMapping` rather than `AfterMapping` — the hole is in the ids, not in the
mapped entity, and checking before `req.ToEntity` runs keeps the rejected request from constructing
anything. Core's `CallerVisibleReferenceExtensions.AllVisibleAsync` is the check, not a hand-rolled
`AnyAsync`: the finding's fix and that helper are the same query, and there is one copy of it.

Two departures from the finding as written:

- **A third id, `PairedLeisureActivityId`.** The same hole in the same expression, one line away, and
  `CreateTodoListItemValidator:29-32` says outright that ownership is not its job. Checked through
  `AllActivitiesVisibleAsync`, which lifts the archived filter — pairing with an activity the user
  archived is legitimate, 404ing it would not be. (`ActivityId` was already covered by
  `EnsureOwnsActivityAsync`; that call stays, first.)
- **The PUT, on a follow-up.** `UpdateTodoListItemEndpoint` rewrites `TaskPriorityId` and
  `PairedLeisureActivityId` with only the activity guard on them, so the same repoint was reachable on
  an existing item — and worse, since it re-points a row that already exists rather than creating a new
  one, so there is nothing new for anybody to notice. Out of this finding's scope as stated (it names
  the create endpoint), so it was flagged rather than silently absorbed, then fixed on request in the
  same batch. It carries no `TodoListId`, so it is two ids, not three.

With four write paths and six ids between them, the check and its 404 answer are wrapped once in
`application/endpoint/extensions/TodoListReferenceOwnershipExtensions.cs` — an `IEndpoint` extension, so
the patch bases (`Endpoint<TRequest>`) and the create/update bases (`Endpoint<TRequest, long>`) share it.
It reports on the offending **field**, the way `ActivityIdOwnershipExtensions` does, rather than a bare
`AddError`.

Pinned by `TodoListReferencedIdOwnershipTests` (`AdhdTimeOrganizer.IntegrationTests/Endpoints/`), which
asserts on rows as well as status codes — the corrupted state this finding describes is a row, and a
404 alone would not prove nothing was written.

`Doc impact: domain-map.md → Invariants (3 — restate as "the filter scopes reads; FK ids on write must be verified explicitly")` — **done**, see `DOC-5`

### SEC-2 🟠 ✅ FIXED 2026-09-09 · `ChangePriorityTodoListItemEndpoint` writes an unverified `TaskPriorityId`

`application/endpoint/todoListItem/command/ChangePriorityTodoListItemEndpoint.cs:30`
(`entity.TaskPriorityId = req.PriorityId;`), validator is `GreaterThan(0)` only
(`application/validator/priority/ChangePriorityTodoListItemValidator.cs:11`).

Same shape as SEC-1 and same consequence, reachable on an existing item rather than a new one. Called
out separately because the fix lands in a different method (`Mapping` is synchronous here, so the check
belongs in an `OnBeforeHandleAsync`/overridden handler or the validator gains a DB-backed rule).

**Fix.** Verify ownership before assigning; 404 on miss. If `BasePatchEndpoint` grows an async
`ValidateAsync` hook, this and SEC-1 should both use it rather than each hand-rolling.

**Fixed 2026-09-09**, and the conditional in that last sentence is what happened: `BasePatchEndpoint`
grew the hook under `CQ-7`, so this is a `BeforeMapping` override calling the same
`AllVisibleAsync` the create path uses. `Mapping` stays synchronous and untouched. `CQ-6` lands in the
same file's `AfterMapping`, which is why the two were fixed together.

`Doc impact: none`

### SEC-3 🟠 ✅ FIXED 2026-09-09 · `MoveTodoListItemEndpoint` swallows cancellation and bypasses the base's concurrency handling

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

**Fixed 2026-09-09** the preferred way, so the endpoint's own catch is gone rather than corrected. The
`when (ex is not OperationCanceledException)` guard therefore had to land on **`BasePatchEndpoint`'s**
catch instead — where it was missing too, and where it covers every patch endpoint in the solution
rather than this one. That is the half of the finding that would have been silently lost by fixing only
the copy: deleting a bad catch that shadows an identically bad one fixes nothing.

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
in a priority group sits one gap below the last: item *n* lands on `100_000_000 - (n-1) × 1_000_000`.
The **101st** item therefore lands on exactly `0` — still correct. It is the **102nd** that breaks: the
group's minimum is now `0`, the sentinel misfires, and it is handed `100_000_000` — back at the very top
of the group, colliding with item 1's original order. Every subsequent item repeats the cycle, so from
there on the group's order is scrambled rather than merely dense. `MaxItems` is one off the count at
which this breaks; nothing enforces it as a cap either way.

*(Recheck note: the first pass said "after exactly 100 items … item 101". Off by one — it is item 102
that resets. The minimal reproduction is unaffected and is the one the test prompt prefers: seed a
single item at `DisplayOrder = 0` and create one through the API.)*

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

### CQ-6 🟠 ✅ FIXED 2026-09-09 · Changing an item's priority moves it between `DisplayOrder` groups without reordering it

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

**Fixed 2026-09-09** in `AfterMapping` — "`Mapping`'s async counterpart" is now a real hook rather than
a wish (`CQ-7`), and it has to run *after* `Mapping`, since the new order is computed for the priority
`Mapping` just assigned. The template held: the two endpoints are now the same three-hook shape.

The test asserts the moved item's order is **below the destination group's minimum**, not a literal —
`DisplayOrderGap` and the derived `DisplayOrderStart` are configuration, and a test pinning
`99_000_000` would fail on an `appsettings` change rather than on a regression. It also asserts the two
items start on the *same* order, which is what makes the case meaningful: without that, an item that
happened to sort correctly anyway would pass.

`Doc impact: domain-map.md → Navigation index (note that TaskPriorityId is the DisplayOrder grouping key, so the two columns move together)` — **done**

### CQ-7 🟠 ✅ FIXED 2026-09-09 · `MoveTodoListItemEndpoint` derives from a base it then fully replaces

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

**Fixed 2026-09-09**, and it did pay for four. `BasePatchEndpoint` gained `BeforeMapping` and
`AfterMapping` — both `Task<bool>`, both with `BaseCreateEndpoint`'s contract that `false` means the
response is already written — plus the `DbContext` property that base already exposes and the
cancellation guard from `SEC-3`. `MoveTodoListItemEndpoint` is 68 lines to the old 84 and owns nothing
but its three hooks; `HandleAsync`, `FindAsync`, the save and the `DbUtils.HandleException` copy are
all gone.

Ordering is the part worth keeping: `AuthorizeAsync` (the routed row, 403) → `BeforeMapping`
(caller-supplied ids, 404) → `Mapping` → `AfterMapping` (anything derived from what `Mapping` wrote) →
save → `AfterSave`. `CQ-6` needs the last hook specifically, and `SEC-1`/`SEC-2` need the first one
specifically; a single combined hook would have served one of them badly.

The 409 the old copied catch existed to preserve is now inherited, and
`TodoListReferencedIdOwnershipTests` pins it directly (move an item onto a list already holding that
activity) — the finding's claim that only the role gate came from the base is the reason that assertion
is worth having rather than assuming.

`Doc impact: none` *(in the slice's own docs. The solution-level `docs/claude/endpoints.md` row for
`BasePatchEndpoint` listed only `AfterSave` and now lists all four hooks; `summary.md:148-150` told the
next reader to copy the `DbUtils.HandleException` block, which is the advice that produced this
finding, and now says the opposite.)*

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

### CQ-10 🟡 ✅ FIXED 2026-09-07 · The step endpoints call `SaveChangesAsync` directly instead of the Result helpers

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

**Fixed 2026-09-07, all five sites**, as prescribed — `UpdateRangeAsync` for the bulk toggle,
`UpdateEntityAsync` for the other four, each mapping `result.ErrorType` through
`EndpointHelper.ToStatusCode` in the shape `BaseChangeDisplayOrderTodoListEndpoint:68` already used.

Two things the fix had to get right beyond the status code, neither of them in the finding:

- **The failure gate has to sit ahead of the side effects.** Both toggle bases do more after their save
  than answer: `AfterSave` announces the end-of-period summary and `PublishEvent` raises
  `RoutineTodoListIsDoneChangedEvent`, the only channel into Planning. Left below a bare save they would
  fire on a save that did not commit — a notification and a cross-slice fan-out for a toggle that did not
  happen. Both now return before reaching them.
- **`BaseCreateStepEndpoint` returned the step id before persisting it.** `Send.ResponseAsync(step.Id, 201)`
  followed the save with nothing between, so a failed save handed the client a 201 and a `Guid` that names
  no row — and three step routes take that id in the path. Now sent only past the gate.

Blast radius is both slices: these five bases serve `TodoListItem` and `RoutineTodoList` alike. Verified by
the full suite — 2101 tests, no new failures.

`Doc impact: none`

### CQ-11 🟡 ⚠️ PREMISE WRONG · INVERTED AND FIXED 2026-09-07 — `OnItemCompleted` in `BaseToggleStepIsDoneEndpoint` was dormant, not dead

**The original finding, kept on the record because acting on it would have caused the defect it
described as harmless:**

> `application/endpoint/BaseToggleStepIsDoneEndpoint.cs:99-100` is a commented-out call to
> `OnItemCompleted`; `:108-110` is the `protected virtual OnItemCompleted` it would have called, which no
> subclass overrides. With them gone, the `DateTime now` parameter threaded through `HandleAsync:45`,
> `BeforeToggle`, `ApplyStepToggle` and `OnItemCompleted` has no reader either — `CompletedTimestamp` is
> the interceptor's job, which is precisely why the call was commented out.
>
> **Fix.** Delete both, then drop the `now` parameter from `ApplyStepToggle` and `OnItemCompleted`.
> `BeforeToggle` keeps its `now` — Routines' period-reset override uses it.

**Correction, 2026-09-07.** *"which no subclass overrides"* is false.
`AdhdTimeOrganizer.Routines/application/endpoint/routineTodoList/command/ToggleStepIsDoneRoutineTodoListEndpoint.cs:40-43`
overrides it, and calls `RoutineResetService.UpdateItemStreak`. The override reads as unreferenced only
because a search scoped to `AdhdTimeOrganizer.TodoLists` cannot see it — the same cross-slice blind spot
`MIG-6` in [03-risks-rollout.md](03-risks-rollout.md) warns about, arriving here from the opposite
direction: not "a fix in this base changes Routines' behaviour" but "a Routines file is the only reason
this base's member is live at all".

So the commented-out call was not tidy-up awaiting deletion — it was a **live defect in Routines**.
Completing a routine item by ticking its last step advanced no streak, while completing the same item
through `PATCH /api/routine-todo-list/toggle-is-done` advanced it normally. Silent, as usual for this
solution: the toggle answered 204, the item showed as done, and only the streak column stayed at 0. Had
the fix above been applied, the hook and its override would both have been deleted and the routine item
streak would have become permanently unreachable from the step route, with nothing going red.

**Fixed** by restoring the two commented-out lines — the inverse of the recommendation. `now` therefore
keeps its reader and stays on `ApplyStepToggle` / `OnItemCompleted`; nothing about the signature changes.
`TodoListItem` has no per-item streak, so the default no-op remains correct for the TodoLists side and
that behaviour is unchanged.

Pinned by `RoutineToggleIsDoneTests.StepToggle_CompletingTheLastStep_UpdatesTheItemStreak`
(`AdhdTimeOrganizer.IntegrationTests/Routines/`), which found the defect and now guards it, including the
un-tick/re-tick case that confirms `UpdateItemStreak`'s once-per-cycle guard holds on this path too.

`Doc impact: none in the living docs. The rule now lives as an XML summary on OnItemCompleted itself,
naming the override and the test — deliberately at the declaration, since that is where the next reader
will conclude it is dead.`

### CQ-12 🟡 `TodoListGroupedByCategoryResponse` is dead

`application/dto/response/TodoListGroupedByCategoryResponse.cs` — zero references across the whole
solution (verified by search, excluding `bin`/`obj`). No endpoint returns it and no test names it.

**Fix.** Delete it. If grouped-by-category reads are planned, the shape belongs with the endpoint that
introduces them.

`Doc impact: none`

### CQ-13 🟡 The dev seeder's name, its comment and its arithmetic are all wrong

`infrastructure/persistence/seeder/dev/TodoListSeeder.cs`. Three separate things:

1. **The class is `TodoListItemSeeder`** (`:12`) in a file called `TodoListSeeder.cs`. Per `CLAUDE.md`,
   files are assigned by what the type *is* — but here the type and the file disagree with each other.
   Rename the file. *(Recheck: `domain-map.md:142` now documents the mismatch instead of getting it
   wrong — see DOC-2 — so this is the only side still open. Call it `CQ-13.1` when scheduling it.)*
2. **`:28` says "TruncateTable already removed existing data for this user".** It did not:
   `TruncateTableCascadeAsync<TodoListItem>()` (`:22`) truncates the table for *every* user. Harmless in
   dev, but the comment will mislead whoever next reads it for the per-user seeder contract.
3. **`:80-93` inlines three copies of `GetNextDisplayOrder`** rather than calling the extension six
   lines away in the same project — carrying both CQ-2's `!= 0` sentinel and CQ-3's `(int?)` cast into a
   third, fourth and fifth location. A fix to the extension will silently not reach them.

**Fix.** Rename the file; correct the comment; replace `:80-93` with three calls to
`dbContext.Set<TodoListItem>().GetNextDisplayOrder(settings.Value, userId, priorityId, ct)`.

`Doc impact: domain-map.md → Seeding (drop the "the file is named TodoListSeeder.cs" clause once renamed)`

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

### CQ-16 🟡 The update path bypasses `SetDone`, and it is split across two files

*Rewritten in the recheck.* The original text said `UpdateEntity` writes the `Steps` collection. It does
not — `UpdateTodoListItemRequest.UpdateEntity` (`application/dto/request/UpdateTodoListItemRequest.cs:15-28`)
never touches `Steps`. The defect is real, but the writes are split:

- `UpdateEntity:18` and `:20` set `IsDone` and `DoneCount` as two independent assignments;
- `UpdateTodoListItemEndpoint.AfterMapping` (`application/endpoint/todoListItem/command/UpdateTodoListItemEndpoint.cs:19-29`)
  then replaces the whole `Steps` collection and separately defaults `DoneCount` to 0.

So the three representations of "done" are set in two files, in three places, none of which consults
the others. `BaseTodoListItem.SetDone` (`domain/model/entity/BaseTodoListItem.cs:20-27`) exists
specifically to "snap `IsDone`, `DoneCount` … and every step's `IsDone` together, so the three
representations of 'done' can't drift apart the way they did when callers set them separately" — and
nothing on this path calls it. A PUT can produce `IsDone = true` with every step unticked, or
`DoneCount = 3` on an item whose steps are all open. `UpdateTodoListItemValidator:22-26` bounds
`DoneCount <= TotalCount` and nothing else.

The interceptor still stamps `CompletedTimestamp` correctly (it watches only `IsDone`), so the recap is
unaffected; the damage is confined to the item's own display.

**Fix.** Leave `UpdateEntity` to the non-done fields, and end `AfterMapping` — after the steps have been
replaced — with `entity.SetDone(req.IsDone)`. That is the one ordering in which the helper can do its job.

`Doc impact: none`

*(`CQ-19`, immediately below, is the other half of this endpoint's problem and shares its one-line fix.
It is numbered out of sequence because it was found in the second pass; `CQ-17` and `CQ-18` follow it.)*

### CQ-19 🟠 A PUT regenerates every step's `Guid`, breaking the step routes *(new in the recheck)*

`application/endpoint/todoListItem/command/UpdateTodoListItemEndpoint.cs:20-26` rebuilds the collection
with `new TodoListStep { Name = …, Order = …, Note = …, IsDone = … }` — and never assigns `Id`.
`TodoListStep.Id` defaults to `Guid.NewGuid()` (`domain/model/entity/TodoListStep.cs:5`) and the column
is `ValueGeneratedNever()` (`infrastructure/persistence/TodoListEntityConfigurationExtensions.cs:25`),
so the value written is whatever the constructor minted — a **fresh id for every step, on every update**.
The incoming `StepRequest.Id` (`application/dto/request/step/StepRequest.cs:7`) is read at `:25` only to
carry `IsDone` across, then discarded.

**Why it matters.** The step id is a public key: three routes take it in the path —
`PATCH /todo-list-item/{itemId}/steps/{stepId}/toggle`, `PUT …/steps/{stepId}`,
`DELETE …/steps/{stepId}`. After any PUT of the parent item, every id the client is holding is gone, and
all three return 404 against a step that is visibly on screen. The user-visible shape is "edit the task,
then ticking a checklist line does nothing until you reload". It also defeats the `IsDone` carry-over
one line above it: the lookup matches on the id the client echoed back, so it works exactly once — on
the next PUT the client is echoing ids that no longer exist and every step silently reverts to
`IsDone = false`.

Steps are `ToJson`-owned, so this is not a row churn issue; it is purely identity loss. Nothing in the
suite pins it — `TodoListStepsCrudTests` exercises the step routes and `TodoListItemPairingTests:123`
exercises the PUT, but no test does a PUT *and then* uses a step id.

**Fix.** `Id = s.Id ?? Guid.NewGuid()`, and match `IsDone` off the same id. Better still, reconcile in
place — update the steps that came back with an id, add the ones without, remove the ones the request
dropped — so a step's identity survives an edit the way its parent's does.

`Doc impact: domain-map.md → Entities (TodoListStep row: say the client-supplied Guid is a stable public key, since three routes take it)`

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

### DOC-1 ✅ Resolved by `6079f93` — `TodoListSettings`' documented members

The first pass found `domain-map.md:49` advertising a configurable `DisplayOrderStart`, which does not
exist as a bound member. Today's doc commit rewrote that row: `domain-map.md:53` now reads "`MaxItems`,
`DisplayOrderGap`; `DisplayOrderStart` is **derived** (`MaxItems * DisplayOrderGap`), not configured",
matching `infrastructure/settings/TodoListSettings.cs:5-8` exactly. No action.

One thing the original fix note asked for did **not** land, and is worth adding when `CQ-2` is fixed:
`MaxItems` reads like a cap and is not enforced as one — it is only a multiplier. That confusion is the
heart of `CQ-2`, and the row is the natural place to say so.

`Doc impact: domain-map.md → Configuration table (one clause: MaxItems is a multiplier, not a cap)`

### DOC-2 🟡 The dev seeder's file name — doc side fixed, code side open

The first pass found `domain-map.md:79` naming a `dev/TodoListSeeder` type that does not exist.
`6079f93` fixed the doc the honest way: `domain-map.md:142` now names the class `TodoListItemSeeder` and
states in bold that the file is `TodoListSeeder.cs`. `summary.md:12` likewise says "the
`TaskPrioritySeeder` / dev `TodoListItemSeeder` pair".

What remains is the code side — the file still disagrees with the class it holds. That is `CQ-13.1`;
this finding stays open only as its doc follow-up, and shrinks to a one-line edit once the file is
renamed.

**Which side is wrong:** now the code only. **Fix:** rename `TodoListSeeder.cs` → `TodoListItemSeeder.cs`,
then drop the "the file is named…" clause from the doc, which exists only to describe the mismatch.

`Doc impact: domain-map.md → Seeding (remove the mismatch clause after the rename)`

### DOC-3 ✅ Resolved by `6079f93` — navigation-index paths

The first pass found the index pointing at `domain/model/entity/todoList/`,
`configuration/extensions/…` and similar — namespaces rendered as folders, so following them found
nothing. `6079f93` rewrote both files against the real tree: `domain-map.md:32` now says outright
"Files are flat in that folder; the namespace is …", the configuration table (`:46-54`) gives real flat
paths, and `summary.md:104-109` calls out the namespace/folder divergence as a gotcha in its own right.
Spot-checked every path in the table against disk — all resolve. No action.

`Doc impact: none`

### DOC-4 🟡 The interceptor's five documented `IsDone` writers are not the five that exist *(narrowed)*

Two copies remain, not three. `summary.md` was rewritten today and its interceptor bullet
(`summary.md:63-72`) no longer enumerates the writers at all, which is the right call. Still listing
them: `domain/model/entity/TodoListItem.cs:29-34` ("five-odd places … the two toggle bases, the step
toggle, the update request and the host's `ActivityTimeRecordedEventHandler`"),
`infrastructure/persistence/interceptor/TodoListItemCompletionInterceptor.cs:13-16` (the same five, but
with `BaseTodoListItem.SetDone` substituted in), and `domain-map.md:167-171` (invariant 8, "five places
that write `IsDone`", unenumerated).

The two enumerations disagree with each other, and the interceptor's is the wrong one. `SetDone` is not
a peer of the endpoints — it is the shared helper the *host's* fan-out handlers call
(`CompletionFanOutTests:82`), and nothing in this slice calls it: the toggle bases use `IsDoneLogic`
(`BaseToggleIsDoneTodoListEndpoint.cs:73`), which duplicates it, and the update path sets the fields by
hand (CQ-16). Listing it beside the endpoints implies this slice's toggles go through it, which is the
opposite of the truth and is exactly the misreading that lets CQ-16 look harmless.

**Which side is wrong:** the doc (two copies now). **Fix:** make both enumerations the same four
in-slice write sites — `BaseToggleIsDoneTodoListEndpoint.IsDoneLogic`,
`BaseToggleStepIsDoneEndpoint.ApplyStepToggle`, `UpdateTodoListItemRequest.UpdateEntity`, and the host's
`ActivityTimeRecordedEventHandler` — and name `SetDone` separately as the helper the host's handlers use.

`Doc impact: domain-map.md → Invariants (8); TodoListItem.cs + TodoListItemCompletionInterceptor.cs XML docs`

### DOC-5 🟠 ✅ FIXED 2026-09-09 · Invariant 3 asserts a scoping rule the slice does not follow in either direction

*Renumbered in the recheck: this was invariant 2 at `domain-map.md:84` before `6079f93`; it is now
invariant **3**, at `domain-map.md:154-157`. The finding itself is unchanged — the doc rewrite appended
"The seams and `TodoListExtensions` deliberately pass an explicit `userId` on top of it", which
acknowledges half of the contradiction without resolving it.*

`docs/domain-map.md:154-157`: "**Per-user scoping is the DbContext's job**, via the named global filter
on `IEntityWithUser` — not the endpoints (`ApplyUserScoping` is a no-op virtual)."

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

**Fixed 2026-09-09**, rewritten as the two-part rule and deliberately **after** the code it describes —
as written the invariant was the reasoning that licensed `SEC-1`/`SEC-2`, so restating it first would
have produced a doc claiming a guard nobody had written. Part *(b)* now names the helper
(`CallerVisibleReferenceExtensions`), the hook it hangs off, the 404-not-403 rule, the three endpoints
covered and the test that pins them.

The exceptions list is **not** empty and the rewrite says so: `SEC-4` is still open, and the PUT
surface is named as the remaining unguarded write. An invariant that lists its own exceptions is worth
more than one that is aspirationally clean — the previous version was the latter, which is how it came
to license the finding.

`Doc impact: domain-map.md → Invariants (3 — rewrite)` — **done**

### DOC-6 🟡 `summary.md` says the API cannot produce `DoneCount` with a null `TotalCount`; two endpoints do *(new in the recheck)*

`docs/summary.md:90-96`, on the check-constraint hole: "`CK_{Entity}_DoneCount_LessOrEqual_TotalCount`
is `done_count IS NULL OR total_count IS NULL OR …`, so Postgres accepts `DoneCount` set with
`TotalCount` NULL. **The API surface never produces it** (the create validator rejects the combination);
direct EF writes can."

The second sentence is false in both halves. Neither validator has a rule tying `DoneCount` to a present
`TotalCount` — `CreateTodoListItemValidator` (`application/validator/CreateTodoListItemValidator.cs:9-50`)
never mentions `DoneCount` at all, and `UpdateTodoListItemValidator:22-26` bounds the pair only
`.When(x => x.DoneCount.HasValue && x.TotalCount.HasValue)`. And two ordinary endpoints write the state
directly:

- `CreateTodoListItemEndpoint.cs:29-33` — creating an item **with steps and no `TotalCount`** sets
  `entity.DoneCount = 0` inside the `req.Steps is { Count: > 0 }` arm, before the `TotalCount` arm is
  even considered.
- `BaseCreateStepEndpoint.cs:43-44` — adding a step to any item does `if (parent.DoneCount is null)
  parent.DoneCount = 0`, regardless of whether that item is step-counted.

So every plain checkbox item that has ever had a step attached carries `DoneCount = 0` with
`TotalCount = null`. The value is harmless — `IsDoneLogic` branches on `TotalCount.HasValue`, not on
`DoneCount`, so nothing misreads it — but the doc is a load-bearing claim about what the constraint hole
can and cannot be reached by, and `TodoListToggleAndDoneCountTests.CheckConstraint_BypassedWhenTotalCountIsNull`
is cited right beside it as the pin. A future reader will use that sentence to decide the hole is
theoretical.

**Which side is wrong:** the doc. **Fix:** say that the API *does* produce `DoneCount = 0` with a null
`TotalCount` (via the two sites above), that it is benign because the state machine keys off
`TotalCount`, and that what remains unreachable is a **non-zero** `DoneCount` without a `TotalCount`. If
that is not the intent, the fix is in the code instead: leave `DoneCount` null unless `TotalCount` is set.

`Doc impact: summary.md → Gotchas (the "DoneCount is step-counted" bullet)`

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
  constructor parameter `appDbContext` — the one name `docs/summary.md:39-43` says never appears in this
  project. *(Line ref repaired in the recheck; `summary.md` was rewritten by `6079f93`.)*
- `GridTaskPriorityEndpoint.cs:15` and `FilterSortTodoListEndpoint.cs:14` use case-sensitive
  `Contains` for name search; `GetFilterSortTodoListCategoryEndpoint.cs:38` does the same. Postgres
  `LIKE` is case-sensitive, so all three name filters miss on capitalisation.
- `TodoListSeeder.cs:116-118` seeds a "Bug fixing" item with `TotalCount = 3`, `DoneCount = 0` and one
  step already `IsDone = true` — an internally inconsistent fixture that no toggle path can produce.
- `TodoListStepsCrudTests.cs:34` uses `Random.Shared.Next(1000, 999_000)` for a column with a unique
  index in a shared-database suite. Low-probability flake; a monotonic counter would be free.

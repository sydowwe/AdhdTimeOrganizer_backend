# TEST-9 — Activity merge collides with `todo_list_item`'s unique index

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

**Status today:** `ActivityMergeTests.cs:196`
(`Merge_OneRowReferencingTwoMergedActivities_CollapsesToOneReference`) covers the *adjacent* case — one
row referencing two merged activities through its two columns — and correctly asserts the collapse.
The case below, two **rows** collapsing onto one key, is uncovered.

## The mechanism

`AdhdTimeOrganizer.TodoLists/application/seam/TodoListActivityReferenceSource.cs:42-72` implements
`IActivityReferenceSource.RepointAsync`. For every item referencing any merged activity it sets
`item.ActivityId = survivorId`, then drops the pairing if it collapsed onto the same activity.

It has no awareness of `infrastructure/persistence/configuration/ToDoListItemConfiguration.cs:47-48`:

```csharp
builder.HasIndex(t => new { t.UserId, t.ActivityId, t.TodoListId }).IsUnique();
```

## The failure scenario

A user has "Reading" and "reading" as two activities — which is the only reason the merge feature
exists — and a to-do item for each, **both on the same list**. Merging repoints both to the survivor,
producing two rows with identical `(UserId, ActivityId, TodoListId)`. The index is checked immediately
(as `TaskPriorityReorderTests` establishes for `TaskPriority`'s equivalent), so `SaveChanges` throws and
**the entire merge transaction rolls back**. The user cannot merge those two activities at all, and the
error surfaces as whatever `ActivityMergeEndpoint` maps a `DbUpdateException` to.

`ActivityProfiles` has an explicit resolution for the same class of problem — `ActivityMergeTests:229`,
"the survivor's own profile wins and the merged one is deleted". TodoLists has none.

## Where the test goes

**Add to the existing `AdhdTimeOrganizer.IntegrationTests/Endpoints/ActivityMergeTests.cs`,** under its
`// ---- the collision cases ----` heading at `:188`, immediately after
`Merge_OneRowReferencingTwoMergedActivities_CollapsesToOneReference`. That file already has the
fixtures, the `MergeAsync(survivorId, params long[] duplicateIds)` helper, `SeedActivityAsync`, and the
class-level narrative that explains what a merge is. Do not start a new file.

## The test

```csharp
/// <summary>
/// Two to-do items on ONE list, each pointing at a different merged activity, must end up as one row
/// on that list — not as a unique-index violation that rolls the whole merge back. This is the ordinary
/// shape of a duplicate activity ("Reading" / "reading"), not an exotic one: if the user kept a task
/// for each, the merge they are trying to perform is precisely the thing that collides.
/// </summary>
[Fact(DisplayName = "CQ-1: two items on one list repointing to the same survivor collapse to one row, not a unique-index violation")]
[Trait("Status", "KnownGap")]
public async Task Merge_TwoItemsOnOneList_CollapseToOneRow()
```

Arrange:

```
survivorId       = SeedActivityAsync(db, "Reading Survivor")
duplicateAId     = SeedActivityAsync(db, "Reading Duplicate")
duplicateBId     = SeedActivityAsync(db, "reading duplicate")

todoListId       = TodoListTestSeedHelper.SeedTodoListAsync(db, ...)
priorityId       = TodoListTestSeedHelper.SeedTaskPriorityAsync(db, <fixed rank, band 350>, ...)

itemA            = SeedTodoListItemAsync(db, duplicateAId, priorityId, todoListId)
itemB            = SeedTodoListItemAsync(db, duplicateBId, priorityId, todoListId)
```

Both items are on **the same list** with **different activities**, so seeding does not collide. That is
the whole setup — the collision only appears once the merge repoints them.

Act: `await MergeAsync(survivorId, duplicateAId, duplicateBId)`.

Assert (correct behaviour):

- `response.StatusCode.Should().Be(HttpStatusCode.OK)` — today this fails; capture the body in the
  message so the reader sees the unique-violation text rather than a bare status mismatch.
- On a fresh context, `Set<TodoListItem>().Where(i => i.TodoListId == todoListId)` returns **exactly
  one** row, with `ActivityId == survivorId`.
- Both merged activities are gone
  (`Set<Activity>().IgnoreQueryFilters().AnyAsync(a => a.Id == duplicateAId)` → false), matching
  `:164-165`'s existing assertion that merged activities are deleted rather than archived.
- The `MergeResult.RepointedCount` still counts **both** rows. `:185` establishes that the count is
  "rows moved" and the snackbar shows it; a collapse that silently reports 1 understates what happened.
  Read `TodoListActivityReferenceSource.cs:64-68`'s comment — the existing collapse case explicitly
  argues that a dropped pairing "still counts as a repoint" — and keep the same rule here.

## Two variations worth one test each

**Different lists do not collide.** Same arrange, but `itemB` on a second list. The merge must succeed
today (nothing collides) and produce two rows, one per list. This is the positive control — without it
a fix that deletes too eagerly passes the main test.

**The survivor already has an item on that list.** Seed a third item for `survivorId` on `todoListId`
before merging. Now three rows collapse onto one key. The `ActivityProfiles` precedent says the
survivor's own row wins; assert that the surviving row is the survivor's original item id, not one of
the repointed ones. This is the case that decides the fix's tie-breaking rule, so pin it explicitly.

## If you are fixing

After repointing, group the affected items by `(UserId, TodoListId)`, keep one per key — the survivor's
pre-existing item if there is one, else the lowest `Id` — and delete the rest, still counting them in
`repointed`. Do it in `RepointAsync` before returning, while the entities are already loaded and
tracked; nothing there touches `IsDone`, so `TodoListItemCompletionInterceptor` stays uninvolved (the
XML doc at `:26-29` says so and that must remain true).

Note that deleting a `TodoListItem` here has downstream effects the seam does not own:
`ActivityHistory.TodoListItemId` is `SetNull` and `PlannerTask.TodolistItemId` points at it from the
Planning side. Losing one of two duplicate items therefore orphans whatever history was recorded against
it — `TodoListItemDailyRecapTests:316` pins that recorded time survives item deletion, so that test
should stay green. Confirm it does.

## Related findings

`CQ-1` (rank 1 in `03-risks-rollout.md`). See also `MIG-6` — nothing in Routines is affected here
(`RoutineTodoList` has no equivalent unique index on activity), but the seam's sibling
`TodoListActivityMembershipSource` is covered only by `SeamWiringTests` and has no behavioural test at
all.

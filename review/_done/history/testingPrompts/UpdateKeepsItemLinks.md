# TEST-8 — an ordinary edit must not unlink a recording from its task

> Read `_SHARED-CONTEXT.md` first. No open finding — this pins an intentional omission that currently
> has nothing but a comment defending it.

## What you are pinning

`application/dto/request/ActivityHistoryRequest.cs:40-50`:

```csharp
public void UpdateEntity(ActivityHistory e)
{
    e.ActivityId = ActivityId;
    e.StartTimestamp = StartTimestamp;
    e.Length = Length;

    // The two item links are deliberately NOT written here. …
}
```

`TodoListItemId` and `RoutineTodoListId` record **which task a recording was saved from** — stamped when
the user accepts the save-to-history prompt on completing an item (a step's prompt sends the *parent
item's* id). The edit form neither shows nor carries them, so mapping them would post `null` on every
ordinary edit and silently unlink the row from its task, dropping it out of that day's recap.

This is defended by comments in three places (the DTO, the entity's XML, `domain-map.md`) and by **no
test**. A routine "map all the fields" cleanup, or a code generator, deletes it and nothing fails.
The symptom is a task that quietly reports zero logged seconds — a plausible wrong number, which is
exactly the failure class this slice's docs say to test against.

## Endpoint

`PUT api/activity-history/{id}` — `application/endpoint/command/UpdateActivityHistoryEndpoint.cs`.

## Scenarios

### 1. The link survives an edit — the whole point

1. Seed a `TodoListItem` (from `AdhdTimeOrganizer.TodoLists`) for the signed-in user, and an
   `ActivityHistory` row carrying its id in `TodoListItemId`.
2. `PUT` the row with a payload that changes `length` (and only fields the edit form actually carries) —
   `{ activityId, startTimestamp, length }`, with `todoListItemId` **absent from the JSON entirely**,
   which is how the SPA sends it.
3. Read the row back from the database and assert `TodoListItemId` is **unchanged**.
4. Assert the new `Length` did land, so the test cannot pass by the update having done nothing.

Repeat for `RoutineTodoListId` with a `RoutineTodoList` from `AdhdTimeOrganizer.Routines`.

### 2. The link survives an edit that explicitly sends null

Same as above but with `"todoListItemId": null` present in the body. This is the shape a client
round-tripping a response would send, and it must also be a no-op — the omission is about `UpdateEntity`
not writing the field at all, not about JSON absence.

### 3. …and the recap still finds it

The reason this matters end to end: after the edit, call whatever surface reads
`ITodoListItemLoggedTimeSource` (see `TodoListItemDailyRecapTests` in the same test project for the
route and body) and assert the item still reports its seconds — now the *edited* number. This is the
assertion that ties the pinned omission to the user-visible consequence, and it is worth more than the
field-level one on its own.

### 4. Create still writes the links

Guard the other direction, so a future "just don't map them anywhere" over-correction is caught: `POST`
a recording with `todoListItemId` set and assert the stored row carries it. (`ToEntity` does write both;
only `UpdateEntity` omits them.)

## Where to put it

Add to `AdhdTimeOrganizer.IntegrationTests/Endpoints/TodoListItemDailyRecapTests.cs` — it already owns
the two item-link guards (`TimeIsAttributedByItem_NotByActivity` and
`DeletingTheItem_KeepsTheRecording_AndUnlinksIt`), already seeds to-do items alongside history, and this
is the third rule in the same family. Then add a row for it to
`AdhdTimeOrganizer.History/docs/testing.md` § *The guards — do not delete these*.

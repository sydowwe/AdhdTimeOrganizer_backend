# TEST-6 — `ActivityHistory` write rules: validator, uniqueness, ownership

> Read `_SHARED-CONTEXT.md` first. Findings: `SEC-4` (no ownership check on the three ids), and the
> `EndTimestamp` staleness nit in `02-findings.md`.

## Endpoints

| Route | File |
|---|---|
| `POST api/activity-history` | `application/endpoint/command/CreateActivityHistoryEndpoint.cs` (`BaseCreateEndpoint`) |
| `PUT api/activity-history/{id}` | `application/endpoint/command/UpdateActivityHistoryEndpoint.cs` (`BaseUpdateEndpoint`) |

Both declare `Validator<ActivityHistoryValidator>()`.

## Request

```jsonc
// ActivityHistoryRequest : ActivityIdRequest
{
  "activityId": 12,
  "startTimestamp": "2026-03-10T09:00:00Z",
  "length": { "hours": 0, "minutes": 30 },
  "todoListItemId": null,
  "routineTodoListId": null
}
```

`EndTimestamp` is **not** on the wire — `ToEntity` computes it as `StartTimestamp + Length`.

## Existing coverage — do not duplicate

`HistoryCrudAuthMatrixTests` covers the happy path and the full auth matrix for create/update/delete/
get-by-id (cross-user ids answer **404**, not 403, because `ActivityHistory` is `IEntityWithUser` and the
global query filter hides the row). Everything below is a rule with no test.

## Scenarios

### 1. The mutual-exclusion rule

`ActivityHistoryValidator.cs:33-36`: a recording cannot carry both `todoListItemId` and
`routineTodoListId`. The comment explains why — one recording is raised by completing one item, and both
set would double-count the row across two recaps.

- both null → 200
- only `todoListItemId` → 200
- only `routineTodoListId` → 200
- **both set → 400**, with the error keyed on `todoListItemId` (the rule sets
  `.WithName(nameof(ActivityHistoryRequest.TodoListItemId))` so the client can put the message beside a
  field — assert the key, not just the status)

Seed real ids for the two links: a `TodoListItem` from `AdhdTimeOrganizer.TodoLists` and a
`RoutineTodoList` from `AdhdTimeOrganizer.Routines`. (The test project references both; the *slice*
does not, which is the whole point of the host-side FK declaration.)

### 2. `length` rules

- `{ "hours": 0, "minutes": 0 }` → 200 (a zero-length recording is legitimate; `time-of-day` explicitly
  supports one, crediting its start hour with one entry and no seconds)
- a negative total → 400 ("Length must be non-negative")
- `null` → 400
- `activityId: 0` → 400 (`NotEmpty`)
- `todoListItemId: 0` / negative → 400

### 3. ⚠️ Ownership of the three ids (`SEC-4`)

Nothing anywhere checks that `activityId`, `todoListItemId` or `routineTodoListId` belong to the caller —
`ActivityIdRequest` is a bare id holder, `BaseCreateEndpoint` performs no check, and the validator only
tests the sign. The `Activity` FK enforces existence, not ownership.

Test: create a second user with an activity; as the signed-in user, `POST` a recording carrying **that**
activity id.

- Assert **400**. Today the insert succeeds.
- Then, if it does succeed, follow through and assert what happens on read: `GET
  api/activity-history/{id}` and `POST api/activity-history/gird`. `Activity` is `IEntityWithUser`, so
  the global filter applies to the `Include` as well — the join yields null and
  `ActivityHistoryResponse.Projection`'s non-nullable `Id = e.Activity.Id` materialises from a null.
  Record precisely what comes back (a 500? a null activity? an exception message via `SEC-2`?). That
  observation is the finding's real severity, so write it into the PR.

Do the same for `todoListItemId` pointing at another user's to-do item. Expected fallout is milder — the
daily recap filters on `UserId` **and** the item ids, so the victim's recap is unaffected — but the link
should still be rejected.

Before adding a DB-level constraint, note `MIG-4`: ownership is not expressible as an FK, so this stays
app-enforced, and a read-only audit for pre-existing bad rows is:

```sql
SELECT h.id, h.user_id, a.user_id AS activity_user_id
FROM history.activity_history h
JOIN <activity schema>.activity a ON a.id = h.activity_id
WHERE a.user_id <> h.user_id;
```

(Fill in the activity table's schema from `ModuleSchemas.cs` — **hand-written SQL is not qualified for
you**, and an unqualified name resolves against whatever is on the `search_path`.)

### 4. The unique index

`(UserId, ActivityId, StartTimestamp)` is unique — "one row per activity per start instant". `POST` the
same `(activityId, startTimestamp)` twice.

Assert the second call's status. If it is a raw 500 from a `DbUpdateException`, that is worth a finding of
its own: a duplicate is a client mistake and deserves 409 or 400. Either fix it or pin it with
`[Trait("Status","KnownGap")]` and say which.

### 5. `EndTimestamp` after an update (the nit)

`ToEntity` computes `EndTimestamp = StartTimestamp + Length`. `UpdateEntity` writes `ActivityId`,
`StartTimestamp` and `Length` and **leaves `EndTimestamp` alone**.

Create a 60-minute recording, then `PUT` it down to 10 minutes. Read the row from the database and assert
`EndTimestamp == StartTimestamp + Length`. It will still be an hour out.

This matters beyond tidiness: `ActivityHistoryTimeAttributionSink`'s contiguous-window probe matches on
`EndTimestamp == windowStart`, so a stale value silently changes which heartbeat windows extend which
rows. Fix `UpdateEntity` to recompute it (or make `EndTimestamp` computed) and keep the test.

### 6. Concurrency

`BaseTableEntity` carries a `row_version` token. Check whether `BaseUpdateEndpointTests` already covers a
stale-token update; if not, add one here: read a row, update it twice, assert the second update with the
stale token answers a conflict rather than silently winning.

## Where to put it

New file `AdhdTimeOrganizer.IntegrationTests/Endpoints/ActivityHistoryWriteRuleTests.cs`. Leave
`HistoryCrudAuthMatrixTests` as the auth-matrix file — it says so in its own header.

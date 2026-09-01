# TEST-2 — the three profile `batch-delete` routes: ownership and all-or-nothing

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

Pins the ownership loop in the three hand-written batch-delete endpoints, which nothing exercises
today. Also the regression net for **CQ-1** (migrating these endpoints onto
`BaseBatchDeleteEndpoint`) and **PERF-2** (the per-id query loop) — write these before touching
either.

## The endpoints

| Route | File |
|---|---|
| `POST /api/activity-backlog-profile/batch-delete` | `application/endpoint/backlog/command/BatchDeleteActivityBacklogProfileEndpoint.cs` |
| `POST /api/activity-bucket-list-profile/batch-delete` | `application/endpoint/bucketList/command/BatchDeleteActivityBucketListProfileEndpoint.cs` |
| `POST /api/activity-project-profile/batch-delete` | `application/endpoint/project/command/BatchDeleteActivityProjectProfileEndpoint.cs` |

Request: `Sydowwe.Framework.application.dto.request.generic.IdListRequest` — `{ "ids": [1, 2, 3] }`.
Responses: 204 on success, 404 if **any** id is missing or not the caller's, 400 on a DB error.

The ownership check is `entity.Activity.UserId != userId` inside a `foreach` over `req.Ids`
(`BatchDeleteActivityBacklogProfileEndpoint.cs:31-45`) — one query per id, and the endpoint returns
404 on the first miss **after** having already collected the earlier entities (nothing is saved, so
the refusal is genuinely atomic; that is what scenario 3 pins).

## Scenarios (write them once, `[Theory]`-style over the three families if you can share the seed)

1. **Happy path.** Seed two of the caller's own profiles, `POST` both ids → 204, and **both rows are
   gone** in a fresh context.
2. **A foreign id refuses the whole batch.** Seed one profile for the caller and one for a second
   user. `POST` both ids → 404, and **the caller's row still exists**. This is the assertion that
   matters: a naive rewrite that deletes what it can and 404s afterwards passes a status-code-only
   test.
3. **A non-existent id refuses the whole batch.** `POST [callersId, 999999]` → 404, caller's row
   intact.
4. **Empty list.** `POST { "ids": [] }` → today the loop does nothing and answers **204**;
   `BaseBatchDeleteEndpoint:35-40` answers **400** ("No ids were provided."). Pin whichever you
   choose *explicitly*, because `CQ-1`'s migration silently changes it. Recommended: assert 400 and
   fix the custom endpoints to match the base, so the migration is a no-op.
5. **Auth.** Unauthenticated → 401.

## Cascade note

Deleting a profile does **not** delete its `Activity` (the FK runs the other way —
`ActivityBacklogProfileConfiguration.cs:14-23`, cascade from activity to profile). Assert the
activity survives; `DeletingActivity_CascadesToItsProfile_NoOrphanSurvives`
(`ActivityProfileGridTests:684`) already pins the other direction.

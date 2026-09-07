# TEST-3 — `all-options` scoping on the bucket-list and project profiles

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

The backlog's select-options route is pinned
(`ActivityProfileGridTests.Backlog_SelectOptions_OnlyReturnsCallersOwnRows:233`). Its two siblings
are byte-identical code with nothing watching them, and they are hand-written rather than derived
from a base — so a copy-paste edit that drops the `Where` from one of them is silent.

## The endpoints

| Route | File |
|---|---|
| `GET /api/activity-bucket-list-profile/all-options` | `application/endpoint/bucketList/query/GetSelectOptionsActivityBucketListProfileEndpoint.cs` |
| `GET /api/activity-project-profile/all-options` | `application/endpoint/project/query/GetSelectOptionsActivityProjectProfileEndpoint.cs` |

Both are `EndpointWithoutRequest<List<SelectOptionResponse>>` and do:

```csharp
dbContext.Set<T>().AsNoTracking()
    .Where(p => p.Activity.UserId == userId)     // the only thing keeping other users out
    .OrderBy(p => p.Activity.Name)
    .Select(p => new SelectOptionResponse(p.Id, p.Activity.Name))
```

Response: `[{ "id": 12, "text": "Skydive" }]` — the **profile** id with the **activity** name.

## Scenarios (per route)

1. **Only the caller's rows.** Seed one profile for the caller and one for a second user; the
   returned id set contains the first and not the second. Copy the shape of
   `Backlog_SelectOptions_OnlyReturnsCallersOwnRows:233`.
2. **`text` is the activity name, `id` is the profile id.** Easy to get backwards in a refactor, and
   the client renders the text into a picker while sending the id back as a profile reference.
3. **Ordered by activity name.** Seed "Zeppelin ride" before "Abseiling" and assert the returned
   order. `OrderBy(p => p.Activity.Name)` is the only sort and nothing pins it.
4. **Empty is `[]`, not 404.**
5. **Auth.** Unauthenticated → 401; `CreateUserRoleClient()` → 200.

Keep them in `ActivityProfileGridTests` next to the backlog one, so all three sit together and the
next person adding a fourth profile family copies a complete block.

# TEST-5 — a profile must not accept another user's lookup ids

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

Pins **SEC-2 🟠**. Same defect class as `review/todo-lists/testingPrompts/CreateTodoListItemForeignKeyOwnership.md`
— read that one too; the shape of the test is the same and consistency between the two is worth more
than originality.

## The endpoints

| Route | File | Unvalidated FK ids |
|---|---|---|
| `POST /api/activity-backlog-profile` | `application/endpoint/backlog/command/CreateActivityBacklogProfileEndpoint.cs` | `locationTypeId`, `weatherDependencyId`, `expectedCostTierId` |
| `PUT /api/activity-backlog-profile/{id}` | `.../UpdateActivityBacklogProfileEndpoint.cs` | same three |
| `POST /api/activity-bucket-list-profile` | `.../bucketList/command/CreateActivityBucketListProfileEndpoint.cs` | `experienceTypeId` |
| `PUT /api/activity-bucket-list-profile/{id}` | `.../UpdateActivityBucketListProfileEndpoint.cs` | same |

Validators: `CreateActivityBacklogProfileValidator.cs:15-44` (activity ownership + "one profile per
activity", nothing about the lookups) and `UpdateActivityBacklogProfileValidator.cs:9-14` (three
range rules, no DB access at all). Same for the bucket-list pair.

Request body — backlog create:

```json
{ "activityId": 7, "locationTypeId": 3, "weatherDependencyId": 4, "expectedCostTierId": 2,
  "energyLevel": "Low", "effortType": "Mental", "minParticipants": 1, "maxParticipants": null,
  "durationMinutes": 45, "isRepeatable": false }
```

Responses: 201 with the new id (create), 204 (update), 400 on a validation or DB failure, 404 when
the *profile* is not the caller's.

## Scenarios

1. **Create with a foreign `expectedCostTierId` is refused.** Seed a second user through
   `UserManager` (their per-user default seeders give them a full lookup set — grab the id of their
   "Expensive" tier from the DB). `POST` a backlog profile for the caller's own activity using that
   id. Expect **400**, and assert **no row exists** for the activity in a fresh context.
2. **Same for `locationTypeId` and `weatherDependencyId`** — `[Theory]` over the three, since they
   are three separate `HasOne(...).OnDelete(Restrict)` FKs and a fix that covers one may miss the
   others.
3. **Same for the bucket list's `experienceTypeId`.**
4. **Update is covered too.** Create a valid profile, then `PUT` it with a foreign
   `expectedCostTierId` → 400, and the row's `ExpectedCostTierId` unchanged. The update validators do
   no DB work today, so this scenario is the one most likely to be left behind by a partial fix.
5. **The consequence, if you want the finding's teeth in a test.** With a foreign lookup id
   persisted (seed it directly through the DbContext — bypass the API so this test survives the fix),
   assert that the second user can no longer `POST /api/activity-expected-cost-tier/batch-delete`
   their own tier: the `Restrict` FK refuses. This is the "victim pays" half of `SEC-2` and it is
   worth one test, marked `[Trait("Status","KnownGap")]` if the cleanup of existing rows is out of
   scope.
6. **The happy path still works** — the caller's own lookup ids → 201 / 204 and the right column
   values.

## The fix

The create validators already open a scope and a `DbContext` inside `CustomAsync`. Extend the
existing single projection:

```csharp
.Select(a => new
{
    HasBacklogProfile = db.Set<ActivityBacklogProfile>().Any(p => p.ActivityId == a.Id),
    LocationOk = db.Set<ActivityLocationType>().Any(l => l.Id == req.LocationTypeId && l.UserId == userId),
    WeatherOk  = db.Set<ActivityWeatherDependency>().Any(l => l.Id == req.WeatherDependencyId && l.UserId == userId),
    CostOk     = db.Set<ActivityExpectedCostTier>().Any(l => l.Id == req.ExpectedCostTierId && l.UserId == userId)
})
```

still one round trip. The update validators need the same block — that is what they are separate
classes for. Filter on `UserId` explicitly rather than relying on the global filter: the validator
resolves its own scope and the ambient user is not guaranteed to be set there.

# TEST-7 — a lost update on a profile must answer 409, not 400

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first.

Pins **CQ-2 🟠**. The three profile `PUT` endpoints are hand-written and end in:

```csharp
catch (Exception ex)
{
    var result = DbUtils.HandleException(ex, nameof(HandleAsync));
    AddError(result.ErrorMessage!);
    await Send.ErrorsAsync(400, ct);      // hard-coded
}
```

(`application/endpoint/backlog/command/UpdateActivityBacklogProfileEndpoint.cs:50-55`, and the same
block in all 12 custom command endpoints of this slice). Every framework base instead does
`Send.ErrorsAsync(EndpointHelper.ToStatusCode(result.ErrorType), ct)`
(`framework/.../BaseUpdateEndpoint.cs:106`), which is what turns a `DbUpdateConcurrencyException`
into a **409**. `BaseEntityConfigure()` puts a `row_version` token on all three profiles, so the
exception is genuinely thrown — it is only the status that is wrong.

## The endpoints

`PUT /api/activity-backlog-profile/{id}`, `PUT /api/activity-bucket-list-profile/{id}`,
`PUT /api/activity-project-profile/{id}` — bodies are the three `Activity*ProfileRequest` records,
response 204.

## How to force the conflict

The DTOs do **not** implement `IUpdateRequestWithRowVersion<T>`, so a client cannot send a stale
token — the only reachable conflict is the in-flight one the base's class comment describes: two
overlapping loads, two saves. Reproduce it at the DbContext level rather than over HTTP:

1. Seed a profile.
2. Open context A, load the entity (tracked).
3. Open context B, update the same row and save — `row_version` moves.
4. Mutate the entity in context A and `SaveChangesAsync` → `DbUpdateConcurrencyException`.

That proves the token is live but not the endpoint's status mapping. For the HTTP half, make the
conflict deterministic by bumping `row_version` from a second context **between** the endpoint's read
and its save. The practical way in this suite: give the test a hook-free race by issuing the two
`PUT`s from two clients concurrently (`Task.WhenAll`) against the same id and asserting that if
either response is not 204, it is **409 and never 400**. Flaky-by-nature races are not welcome here,
so prefer the shape below:

- **Deterministic test (recommended):** assert the *mapping*, not the race. Call
  `EndpointHelper.ToStatusCode` on a `DbUtils.HandleException(new DbUpdateConcurrencyException(...))`
  result and assert it is 409 — then assert the three endpoints call it, by grepping? No. Instead:
  make the endpoints use the base's mapping (the `CQ-2` fix, a one-line change each), and write the
  HTTP test against a *different* mapped error that is easy to trigger deterministically: `PUT` a
  backlog profile with a `locationTypeId` that does not exist at all. Today that is a 400 from the
  hard-coded literal; after the fix it is whatever `EndpointHelper.ToStatusCode` maps an FK violation
  to. Assert that code explicitly, and put the concurrency case beside it as the
  `Task.WhenAll` test with the weaker "never 400" assertion.

## Scenarios

1. **Two concurrent `PUT`s to the same profile**: exactly one 204; any other response is 409 and not
   400. Mark it clearly in the `DisplayName` as a race with a weak assertion.
2. **An FK violation maps through `EndpointHelper.ToStatusCode`**, not to a literal 400 — the
   deterministic half.
3. **A normal `PUT` is still 204** with the new column values in a fresh context (this one is also
   the missing happy-path row in the coverage matrix — no test currently asserts a successful profile
   update at all).

Do all three families, or one `[Theory]` with the route and payload parameterised.

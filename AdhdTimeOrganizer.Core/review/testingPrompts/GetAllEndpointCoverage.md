# TEST-11 / TEST-12 — four routed endpoints with no test at all

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first. Findings: none for `TEST-11` (a plain hole);
`CQ-7` 🟡 for `TEST-12`.

**Status today:** `BaseGetAllEndpointTests` is subclassed exactly twice in the whole solution —
`TimerPresetCrudAuthMatrixTests.cs:93` and `:161`, both for presets. `BaseGetByFieldEndpointTests` is
subclassed **zero** times. So:

| Route | Endpoint | Tests |
|---|---|---|
| `GET api/activity/all` | `GetAllActivityEndpoint.cs` | none |
| `GET api/activity-role/all` | `GetAllActivityRoleEndpoint.cs` | none |
| `GET api/activity-category/all` | `GetAllActivityCategoryEndpoint.cs` | none |
| ~~`GET api/activity-role/by-Name/{value}`~~ | ~~`GetByNameActivityRoleEndpoint.cs`~~ | **route deleted 2026-09-01 — see Part B** |

The three remaining ones are one-line subclasses of Framework bases, so the risk is low per route — but "returns every
row this user owns" is the shape where a missing user filter is invisible until it isn't, and these are
the only Core reads with no assertion of any kind behind them.

## Part A — `TEST-11`, the three GetAll routes — ✅ CLOSED 2026-09-08

> Written as `Endpoints/CoreGetAllEndpointTests.cs`: `BaseGetAllEndpointTests` subclasses for Activity
> and ActivityRole (the ActivityCategory one already existed in `Endpoints/BaseGetAllEndpointTests.cs`),
> plus `CoreGetAllScopingTests` — a cross-user scoping fact per entity and the archived-exclusion fact.
> **Route correction:** the routes are `GET api/activity`, `api/activity-role`,
> `api/activity-category` — *not* `…/all`. `BaseGetAllEndpoint.Configure()` registers
> `/{entity.Kebaberize()}` plus an optional `RouteParam` suffix and none of the three overrides it; the
> `/all` spelling in the table above and in `01-testing.md` was wrong. The analysis below is the
> original prompt, kept as written.

The cheapest correct fix is three `BaseGetAllEndpointTests` subclasses, matching the existing pattern.
Add them to a new `Endpoints/CoreGetAllEndpointTests.cs` (**not** `ActivityEndpointTests.cs`, which is
already ~1100 lines). Copy the shape from `TimerPresetCrudAuthMatrixTests.cs:93`:

```csharp
[Collection("Postgres")]
public class ActivityGetAllTests(AppDbContextFixture fixture) : BaseGetAllEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity/all";
    // + the seed hooks the base requires, mirroring TimerPresetGetAllTests
}
```

Then add the one thing the base does not check, once per entity:

### A1. `GET …/all` returns only the caller's rows

```csharp
[Fact(DisplayName = "TEST-11: /all is scoped to the caller by the global query filter")]
```

Seed two activities for the default caller and two for a second user (`UserManager` snippet in the
shared context). `GET api/activity/all` as the default caller; assert the second user's ids are absent
and both of the caller's are present. Repeat for role and category.

This mirrors `TimerPresetCrudTests.GetAll_ReturnsOnlyCallersOwnPresets:163`, which is exactly the
assertion the preset side has and the activity side does not.

### A2. `GET api/activity/all` and archived rows

`GetAllActivityEndpoint` is a bare `BaseGetAllEndpoint` — it does **not** lift the archival filter and
does not exclude archived rows by hand, so archived activities are excluded by the global filter.
Assert that, because it is currently true by accident rather than by decision:

```csharp
[Fact(DisplayName = "TEST-11: /activity/all excludes archived activities")]
```

Seed one active and one archived; assert only the active one comes back. If the intended answer is
different, that is a product decision worth making explicitly — the "only pickers exclude archived
activities" rule in `../../docs/domain-map.md` does not say which side of the line `/all` is on, and
this test is what forces the question.

## Part B — `TEST-12`, `GET /activity-role/by-Name/{value}` — ✅ CLOSED 2026-09-01, no test written

> **Resolved by the first branch below.** The SPA sweep found no caller: the only `by-name` string
> left in `AntiProcrastinationApp-frontend/src` is the historical note in
> `core/activity/dto/enum/SystemActivityRole.ts:6`, and quick-create already calls
> `/activity-role/by-system-key/{key}` (`core/activity/api/activityOptionsApi.ts:82`). The endpoint is
> deleted and `docs/domain-map.md`'s Navigation index no longer lists `GetByName`. No test needed —
> the untested surface is gone. The analysis below is the original prompt, kept as written.

`GetByNameActivityRoleEndpoint.cs:8-16` was a `BaseGetByFieldEndpoint` matching on `Name ==`. It is the
surface `SystemActivityRole` was introduced to replace: looking a role up by its **user-editable**
display name is what broke quick-create for anyone who renamed one, which is the whole story in
`domain/model/enum/SystemActivityRole.cs:9-12`. The replacement
(`GET api/activity-role/by-system-key/{key}`) has nine tests; the endpoint it replaced has none and is
still routed.

**Decide before writing tests.** Check the SPA for a remaining caller of `/activity-role/by-Name/`.

- **No caller → delete the endpoint**, and delete its row from
  `../../docs/domain-map.md` → Navigation index. No test needed; `CoreRouteSmokeTests` will not miss
  it. This is the recommended outcome.
- **A caller remains → keep it and pin it**, with a note in the docs naming that caller:

```csharp
[Fact(DisplayName = "TEST-12: by-Name finds the caller's role and not another user's")]
[Fact(DisplayName = "TEST-12: by-Name 404s after the role is renamed — which is why by-system-key exists")]
```

The second test is the valuable one. It documents, as an executable assertion, precisely the failure
mode that motivated the system-key work, so anyone reaching for `by-Name` in new code meets it.

Note the route casing: the base interpolates `by-{FieldName}` verbatim and `FieldName` here is
`nameof(ActivityRole.Name)` → `by-Name`, capital N. `GetBySystemKeyActivityRoleEndpoint:31` overrides
`FieldName` to the literal `"system-key"` specifically to avoid that trap, and explains why in its
docstring.

## Doc reconciliation

`../../docs/domain-map.md` → Navigation index: the Role reads row lists `GetByName` alongside
`GetBySystemKey` as peers. If the endpoint goes, so does the mention; if it stays, the row should say
it is retained for a named caller. See `CQ-7`.

# TEST-6 — `TimeDtoValidator` never runs, and one request type is genuinely unvalidated

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first. Finding: `CQ-2` 🟠.

## Why the validator is dead

FastEndpoints resolves validators by the endpoint's **request** type. No endpoint anywhere takes a bare
`TimeDto` as its request, so `Core/application/validator/TimeDtoValidator.cs:7-13` is never
instantiated. It looks like coverage and is not.

`DateRangeDto` documents this exact trap two files away
(`Core/application/dto/dto/DateRangeDto.cs:89-101`): *"FastEndpoints resolves validators by exact
request type, so an abstract or open-generic base is never discovered on its own and a request type
without its own concrete validator is silently unvalidated. That is what these rules were until now:
they were written and never called from anywhere."* The same mistake was then made again for `TimeDto`.

Planning works around it by re-implementing `t.Hours is >= 0 and <= 23 && t.Minutes is >= 0 and <= 59`
inline in seven validators (`ApplyTemplateToTaskPlannerValidator.cs:25,29`,
`CalendarBatchValidators.cs:49,53`, `CalendarRequestValidator.cs:12,16`,
`PatchPlannerTaskStatusValidator.cs:14,19`, `PlannerTaskChangeSpanValidator.cs:12`). **History does
not.**

## The live failure

`AdhdTimeOrganizer.History/application/dto/request/dashboard/summary/HistorySummaryStackedBarsRequest.cs:8-9`

```csharp
public required TimeDto WindowStartTime { get; init; }
public required TimeDto WindowEndTime { get; init; }
```

Its validator is `HistorySummaryDateRangeValidators.cs:24-25` —
`DateRangeRequestValidator<HistorySummaryStackedBarsRequest>`, which validates the date range only.
`TimeDto.ToTimeOnly()` is `new TimeOnly(Hours, Minutes)`
(`framework/Sydowwe.Framework/application/dto/dto/TimeDto.cs:17`), which throws
`ArgumentOutOfRangeException` on 99. So an out-of-range window is an unhandled **500**, not a 400.

## Create `Endpoints/TimeDtoRangeValidationTests.cs`

`[Collection("Postgres")]` + `PostgresTestBase`. Find the exact stacked-bars route and its full payload
shape in `Endpoints/HistorySummaryCustomRangeTests.cs`, which already posts to it — copy the working
request and vary only the two `TimeDto` fields.

### 1. Out-of-range hours are a 400, not a 500 — `CQ-2`, **KnownGap**

```csharp
[Theory(DisplayName = "CQ-2: an out-of-range TimeDto is a 400 naming the field, not a 500")]
[Trait("Status", "KnownGap")]
[InlineData(24, 0)]
[InlineData(99, 0)]
[InlineData(-1, 0)]
[InlineData(0, 60)]
[InlineData(0, -1)]
```

Post a valid stacked-bars request with `windowStartTime = { hours, minutes }` from the theory data.

Correct: **400**, body naming `windowStartTime`.
Today: **500** (`ArgumentOutOfRangeException` out of `ToTimeOnly`).

Assert the status **and** that the body is problem-details rather than an unhandled exception page —
a bare `Should().NotBe(HttpStatusCode.OK)` would pass on both and prove nothing.

### 2. The boundaries are accepted — passes today, keep it green

`{23, 59}` and `{0, 0}` must both succeed. Pins the fix at the right numbers.

### 3. `windowEndTime` is validated too — `CQ-2`, **KnownGap**

Same as #1 for the second property. Two properties, one rule, and a fix applied to one of them is the
most likely half-fix.

### 4. Planning's inline checks still hold — passes today, keep it green

One case against a Planning route that already hand-checks the range — `PATCH` planner-task status
(`PatchPlannerTaskStatusValidator.cs:14`) is the cheapest; find the payload in
`Endpoints/PlannerTaskStatusAndSpanTests.cs`. Assert `{99, 0}` is a **400**.

This is the regression pin for the refactor: when the seven inline checks are replaced by a shared
`TimeDto.ApplyValidationRules`, this test is what proves the replacement kept the behaviour rather than
dropping it.

## The fix, for reference

Delete `TimeDtoValidator` and add a static
`TimeDto.ApplyValidationRules<T>(AbstractValidator<T> validator, Func<T, TimeDto> selector)` in the
exact shape `DateRangeDto.ApplyValidationRules` already uses — including the `OverridePropertyName`
calls, which exist because FluentValidation cannot infer a name from a lambda *invocation* and
otherwise produces errors keyed on `""`. Then call it from the seven Planning validators and from
`HistorySummaryStackedBarsRequestValidator`.

`TimeDto` lives in the **submodule** (`framework/Sydowwe.Framework/application/dto/dto/TimeDto.cs`), so
if the helper goes there it is a two-repo commit — push `framework/` first, then the gitlink. Putting
the helper in Core instead is a legitimate alternative and avoids that.

## Doc reconciliation

`../../docs/domain-map.md` → the `application/validator/` paragraph lists `TimeDtoValidator` among "the
three shared ones" as though it were live. See `DOC-5`.

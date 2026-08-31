# TEST-5 — `icon` passes validation at 255 chars and fails in Postgres at 51

Read [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md) first. Finding: `CQ-1` 🟠.

**Status today:** validation coverage in Core is three tests, all on timer presets
(`TimerPresetValidationTests.cs`). No test compares a validator's limit against the column it writes
into.

## The mismatch

| Field | Validator | Column |
|---|---|---|
| `Icon` | `MaximumLength(255)` — `Core/application/validator/NameTextColorIconValidator.cs:21-22` | `HasMaxLength(50)` — `framework/Sydowwe.Framework/infrastructure/persistence/configuration/extensions/NameTextColorEntityConfigurationExtension.cs:11,18` |
| `Name` | `MaximumLength(50)` | `HasMaxLength(100)` — safe direction |
| `Text` | `MaximumLength(500)` | `HasMaxLength(1000)` — safe direction |
| `Color` | `MaximumLength(7)` + `^#(?:[0-9a-fA-F]{3}){1,2}$` | `HasMaxLength(7)` — matches |

Only `Icon` is inverted. An icon of 51–255 characters clears FluentValidation, reaches Postgres, and
raises error 22001 (`value too long for type character varying(50)`), which
`DbUtils.HandleException` turns into a generic failure rather than a field-level message the form can
attach to `icon`.

Affected: `POST/PUT api/activity-role` and `POST/PUT api/activity-category` — both use
`NameTextColorIconValidator` via `ActivityRoleValidator` / `ActivityCategoryValidator`.

## Create `Endpoints/NameTextColorIconValidationTests.cs`

`[Collection("Postgres")]` + `PostgresTestBase`. Payloads are minimal:

```csharp
new { Name = $"Role {Guid.NewGuid():N}", Color = "#abcdef", Icon = new string('x', 60) }
```

### 1. A 60-character icon is rejected as a validation error — `CQ-1`, **KnownGap**

```csharp
[Theory(DisplayName = "CQ-1: an icon longer than the column is a 400 naming the field")]
[Trait("Status", "KnownGap")]
[InlineData("api/activity-role")]
[InlineData("api/activity-category")]
```

`POST` with a 60-char icon. Correct: **400**, and the problem-details body names `icon` — assert on the
error key, not just the status, because that is the whole point: the user needs to know which field to
shorten.

Today: the status is *also* a 4xx (via `DbUtils.HandleException`), so a status-only assertion would
pass and hide the bug. **Assert on the error key.** If the body carries no per-field errors, that is
the failure.

### 2. A 50-character icon is accepted — passes today, keep it green

Boundary pin on the other side, so the fix (`MaximumLength(50)`) cannot be over-tightened to 49 or to
some arbitrary smaller value. `POST` with exactly 50 chars → **201**, and the row's `Icon` is 50 chars
in the database.

### 3. A 51-character icon is the first rejection — `CQ-1`, **KnownGap**

The exact boundary. Once #1 is fixed this is the test that pins *where* the limit is, and it is the one
that will fail if someone later changes the column without changing the validator.

### 4. `PUT` behaves the same as `POST` — `CQ-1`, **KnownGap**

Create a valid role, then `PUT api/activity-role/{id}` with a 60-char icon. Same expectation. Worth its
own case because create and update use the same validator by different routes and a fix applied to one
`Configure()` override and not the other is a plausible half-fix.

## The fix, for reference

`MaximumLength(50)` on `Icon` in `NameTextColorIconValidator`. Better, if you are willing to touch the
submodule: expose the column lengths as constants from
`NameTextColorEntityConfigurationExtension` and have the validator read them, so the two layers cannot
drift again — but note that is a `framework/` change and therefore a two-repo commit (see `MIG-9` in
[`../03-risks-rollout.md`](../03-risks-rollout.md)).

## Doc reconciliation

None.

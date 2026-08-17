# TEST-10 — Timer CRUD (business-logic layer, not auth)

## Context

Same infra as the other portal integration tests: `PostgresTestBase`, `CreateUserRoleClient()`. Covers
`TimerPreset` and `PomodoroTimerPreset` in `AdhdTimeOrganizer.Core`,
`application/endpoint/timer/{timerPreset,pomodoroTimerPreset}/**`.

**This prompt is business-logic/correctness coverage. If you're also picking up [[TimerPresetAuth]]
(`TEST-1`), do that one first or in the same PR — they touch the same endpoints and should share the
`Seed` helper rather than duplicating it.**

## What exists today

`Endpoints/TimerPresetValidationTests.cs` covers input validation (happy path + edge cases like invalid
duration ranges, presumably). What's NOT covered: the actual CRUD round-trip semantics beyond
validation — e.g. does creating a `PomodoroTimerPreset` correctly link to its constituent
`TimerPreset`(s) if that's how the domain models it; does deleting a `TimerPreset` that's referenced by
a `PomodoroTimerPreset` cascade, restrict, or orphan (check the EF configuration for the FK's delete
behavior before writing the assertion — don't guess); does `GetAll` return only the caller's presets in
the right order; does `Update` correctly persist partial updates without clobbering unset fields.

## What to write

Add `Endpoints/TimerPresetCrudTests.cs` (or fold into the auth-matrix file from `TEST-1` if doing both
together) covering:
- Create → GetById round-trip returns exactly what was created.
- Update persists changes and doesn't reset unrelated fields (a common FastEndpoints pin-worthy bug
  class in this codebase per `CLAUDE.md`'s mapping conventions).
- Delete of a `TimerPreset` still referenced by a `PomodoroTimerPreset` — assert the actual configured
  delete behavior (check `infrastructure/persistence/configuration/` for the FK config first).
- `GetAll` scoping and ordering for a user with multiple presets.

## Out of scope

Don't re-test validation edge cases (already in `TimerPresetValidationTests.cs`) or auth (that's
`TEST-1` / [[TimerPresetAuth]]).

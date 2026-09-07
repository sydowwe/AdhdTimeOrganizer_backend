# TEST-5 — timer-alarm input validation and cross-user safety

> Read `_SHARED-CONTEXT.md` first. Findings: `SEC-1` (foreign `activityId` leaks a name into the push),
> `CQ-12`, and the `ScheduleTimerAlarmsValidator:49` nit.

## Endpoints

| Route | File |
|---|---|
| `POST api/activity-history/timer-alarm/schedule` | `application/endpoint/command/ScheduleTimerAlarmsEndpoint.cs` |
| `POST api/activity-history/timer-alarm/cancel` | `application/endpoint/command/CancelTimerAlarmsEndpoint.cs` |

## Existing coverage — do not duplicate

`TimerBoundaryAlarmTests.cs` has nine tests covering the **lifecycle**: one row per future boundary with
elapsed ones dropped, idempotency by session, cancel removes the session, cancel of an unknown session is
a no-op, the handler fires then self-drains, a cancelled alarm produces silence, the push envelope
carries `tag`/`url` at the top level (and omits `tag` for kinds supplying none), and `TimerBoundary` is
exempt from quiet-hours deferral. It also contains a `CapturingWebPushSender` — **reuse it**, don't write
a second one. Add to that file; it is cohesive and this is the same feature.

Untested: the validator, and anything involving a second user.

## Request shape

```jsonc
// ScheduleTimerAlarmsRequest
{
  "sessionId": "9f2b…",           // Guid, client-issued, required
  "activityId": 12,               // nullable
  "url": "/activity-history/pomodoro",   // nullable, must be relative
  "boundaries": [
    { "boundaryAt": "2026-03-10T12:25:00Z", "phase": 0,
      "phaseIndex": 1, "phaseTotal": 4, "cycleIndex": 1, "cycleTotal": 2 }
  ]
}
// response: { "registered": 1 }

// CancelTimerAlarmsRequest
{ "sessionId": "9f2b…" }   // 204 No Content
```

`phase` is `TimerBoundaryPhase` from `Sydowwe.Framework.Contracts.notification.payload`.

## Scenarios

### 1. ⚠️ A foreign `activityId` must not reach the push (`SEC-1`) — highest value here

`ScheduleTimerAlarmsValidator` checks only `GreaterThan(0)`. `TimerBoundaryAlarm.ActivityId` carries **no
FK** (deliberate — the rows live minutes), so nothing else checks it either. And
`TimerBoundaryAlarmJobHandler.cs:52` resolves the name with no `UserId` predicate, in a background
context where the global filter (`ScopeUserId == null || …`) lets every row through.

Test: create a second user with an activity named something unmistakable
(`"SECRET-{Guid.NewGuid():N}"`). As the signed-in user, schedule an alarm carrying **that** activity id.
Then fire the alarm through the handler the way `Handler_Fires_ThenSelfDrains` does, and assert the
captured push body does **not** contain the string. Today it will.

Fix (either or both): reject a non-owned `activityId` in the endpoint with a 400 — it already has
`User.GetId()` — and scope the handler's lookup with `a.UserId == alarm.UserId`. Then assert the 400 on
the schedule call as well, so the leak is unreachable at both layers.

### 2. Cross-user cancel is already safe — prove it

`CancelSessionAsync` filters on `a.UserId == userId && a.SessionId == sessionId`. Register a session as
user A, then call cancel as user B with A's session guid. Assert 204 (a no-op is the documented answer,
never a 404) **and** that A's rows are still present. Session guids are client-issued, so this is the
only thing standing between a guessed guid and someone else's alarms.

### 3. `ScheduleTimerAlarmsValidator` — every rule, at the boundary

- **`MaxBoundaries = 64`.** 64 boundaries → 200; 65 → 400. Assert *at* the boundary, not near it.
- **Empty / null `boundaries`** → 400 ("At least one boundary is required").
- **`MaxHorizon = 1 day`.** A boundary at `UtcNow + 23h` → 200; at `UtcNow + 25h` → 400.
- **Past boundaries are accepted and dropped, not rejected** — this is deliberate (a set re-sent after a
  slow reload legitimately contains elapsed boundaries, and failing the request would cost the user the
  alarms still to come). Post two future and one past boundary; assert `registered: 2` and 200.
  `Schedule_WritesOneRowPerFutureBoundary` may already cover this — check before adding.
- **`sessionId` empty guid** → 400.
- **`activityId: 0` / negative** → 400.
- **`phaseIndex` / `phaseTotal` / `cycleIndex` / `cycleTotal`**: `0` → 400, `null` → 200 (a plain
  countdown supplies none).

### 4. `url` must stay relative — this one is a security rule

The comment says it plainly: an absolute URL reaches the service worker verbatim and would turn a push
the user trusts into an off-site redirect. `[Theory]` with:

| Input | Expected |
|---|---|
| `/activity-history/pomodoro` | 200 |
| `null` | 200 |
| `https://evil.example/x` | 400 |
| `//evil.example/x` | 400 (protocol-relative — the rule explicitly rejects a leading `//`) |
| `javascript:alert(1)` | 400 |
| `activity-history/pomodoro` (no leading slash) | 400 |
| a 513-character path | 400 (`MaximumLength(512)`) |

Then assert the accepted `url` survives to the **push envelope's top level** — `PushEnvelope_CarriesTagAndUrlAtTopLevel`
already checks the shape; this checks that the value is the one that was posted.

### 5. `boundaryAt` without a `Z` (nit in `02-findings.md`)

`ScheduleTimerAlarmsValidator:49` calls `at.ToUniversalTime()`, which treats an `Unspecified` kind as
*local server time*; `TimerBoundaryAlarmService:64` then force-labels the same value UTC with
`SpecifyKind`. So a client posting `"2026-03-10T12:25:00"` is validated under one interpretation and
stored under another. Post an unsuffixed instant and assert the stored `BoundaryAt` equals what was
posted. Whether that passes or fails, write down which interpretation is the contract — and prefer
requiring the `Z`.

### 6. `registered` is the honest count

Post 5 boundaries of which 2 are in the past; assert `registered: 3` **and** that
`db.Set<TimerBoundaryAlarm>()` holds exactly 3 rows for that session. The response field exists so that
"why did nothing ring" is answerable without reading server logs — it must not drift from the rows.

## Do not

- Do not write a test that "fixes" the accepted residual cancel race (a tab killed between Stop and the
  cancel landing fires an alarm for a session already over). It is accepted, not mitigated — see
  `summary.md`.
- Do not add a retention job or a `FiredAt` column to make a test easier. The table self-drains on fire,
  on cancel and on the registrar's stale sweep, and that is why it needs neither.

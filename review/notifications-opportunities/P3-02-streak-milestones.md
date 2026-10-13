# P3-02 — Streak milestones and new bests, inside the period summary

> Paste into a fresh session. Read `review/notifications-opportunities/_SHARED-CONTEXT.md` first. Backlog #3.
> Small. Framework (payload field, renderer, texts) → host. **No new kind, no frontend change** (the bell shows
> server-rendered text).

## Why

A new best or a round number is good news an ADHD user rarely hears. The README rules out a daily "streak
extended" push, and this doesn't need one: `RoutinePeriodEnded` already goes out at every period end and already says
"Streak continues: N."

## Facts

- `RoutinePeriodEndedPayload` (`framework/Sydowwe.Framework.Contracts/notification/payload/NotificationPayloads.cs:83`).
  Property names are persisted JSON keys, so new members need defaults for older rows to keep deserialising.
- `NotificationTextRenderer.RenderPeriodEndedBody` (`framework/Sydowwe.Notifications/application/NotificationTextRenderer.cs:391`)
  appends `t.StreakExtendedSuffix(streak)` for `Extended`. Virtuals at `…/text/NotificationTexts.cs:177-190`. Host
  wording is at `AdhdTimeOrganizer/infrastructure/notification/SkPortalNotificationTexts.cs:48` and
  `EnPortalNotificationTexts.cs:37`.
- On a period end, the best is raised in exactly one place: `RoutineResetService.TryReset`
  (`AdhdTimeOrganizer.Routines/domain/service/RoutineResetService.cs:257-259`). Afterwards `Streak == BestStreak` reads
  the same for a tie and for a record, so **"new best" cannot be derived after the fact**. Return it from `TryReset` on
  `RoutinePeriodReset` (line 220), whose doc comment already explains that only this method sees both sides.
- Catch-up: `TryResetCatchUp` (line 342) returns `RoutinePeriodCatchUp.Latest`, and all three reset sites notify on
  `Latest` only. They are `RoutineTodoListResetJobHandler.cs:124`, `RoutineToggleIsDoneTodoListEndpoint.AfterSave` and
  `GetAllGroupedRoutineTodoListEndpoint.cs:96`.
- Producer: `RoutinePeriodNotificationService.NotifyPeriodEndedAsync(period, completion, outcome)`
  (`AdhdTimeOrganizer.Routines/application/service/`). Four test doubles implement the interface (grep
  `NotifyPeriodEndedAsync`). Passing the whole `RoutinePeriodReset` beats a fourth parameter.
- `RoutineStreakFreezeService.cs:138` also raises `BestStreak` (re-derivation after a freeze). That isn't a period end,
  so leave it alone.

## Design

1. `RoutinePeriodReset` gains `bool RaisedBest`: the streak beat a previous best above the minimum (see decisions), so
   a first-ever run of 1 isn't announced as a record.
2. Payload: `bool IsNewBest = false`, plus `int PreviousBest = 0` if the wording wants "beat your 5". Fill both from
   `Latest`.
3. Milestones need **no payload change**, because they're a function of `Streak`. Add a virtual
   `StreakMilestoneSuffix(int streak)` and a host-owned milestone set (it's wording, not mechanism). The renderer
   picks it over `StreakExtendedSuffix` when `Streak` is in the set.
4. One sentence, never two: milestone + new best → the milestone wording with "a new best" folded in.
5. The unit is the **period**, not the day: a weekly routine at 7 means 7 weeks. The wording must never say "days".

## Tests

- `TryReset` (extend `RoutineResetServiceTests`): best 5 → streak 6 raises; best 6, streak 6 (a tie) doesn't; a first
  run doesn't.
- A catch-up whose first cycle raised but whose `Latest` broke isn't a new best.
- Renderer: new best, milestone, both, and an old row without the field.
- `NotificationVocabularyTests` still refuses the generic fallback.
- Through the nightly job: the bell row's payload carries `isNewBest` (assert on rows).

## Open decisions

1. Minimum for "new best": beat a best of ≥ 1, or require streak ≥ 3?
2. The milestone set (7 / 30 / 100? also 52 for weekly?).

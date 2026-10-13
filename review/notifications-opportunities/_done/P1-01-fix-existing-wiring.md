# P1-01 — Fix what's already wired

> Paste into a fresh session. Read `review/notifications-opportunities/_SHARED-CONTEXT.md` first.

Three independent defects in notifications that already ship. Each can be its own commit. (The fourth defect
found in the same review — `RefreshTokenCleanupService` running off the scheduler — is
[P2-06](P2-06-token-cleanup-on-scheduler.md).)

---

## A. Two settings rows that mute nothing

**Facts (verified 2026-10-09):**

- `AdhdTimeOrganizer/config/dependencyInjection/ModuleServiceExtensions.cs:226-227` lists
  `NotificationType.DeadlineApproaching` and `NotificationType.ReminderDigest` in `SupportedTypes`.
- **No portal code raises either.** The only references are that list and `E2eFixtureSeeder.cs:1434-1493`, which
  writes bell rows of those types directly. Planning's personal reminders register with
  `NotificationType.PersonalReminder` and no `DigestKey` (`ReminderRegistrationService.cs:119-125`), so the
  Reminders scan never emits `ReminderDigest` here either.
- Effect: `GET /notification-preference/mine` offers two rows that do nothing, and `DeadlineApproaching` shows
  **Email ✓** (it is in `NotificationChannelDefaults.EmailOnByDefault`) — a promise the app never keeps.

**Fix:**

1. Remove both from `SupportedTypes` and from the second copy in
   `AdhdTimeOrganizer.IntegrationTests/Endpoints/NotificationCatalogTests.cs`.
2. Update the counts in the XML docs: `ModuleServiceExtensions.cs:187` ("the eleven of `NotificationType`'s
   twenty") and the `NotificationCatalogTests` class comment → **nine**.
3. Safe for the inherited base tests: the only quiet-hours-exempt kind is `TimerBoundary`
   (`QuietHoursExemptTypes`), and `Update_AcceptsAKindInTheCatalog` takes `ExpectedCatalog.First()`.
4. **E2E seed:** decide whether `E2eFixtureSeeder` keeps the two bell rows. The bell renders any kind, so they
   still display; only the settings matrix shrinks. Check the frontend `e2e/` folder for assertions on those
   settings rows, and read `docs/e2eTestSeed.md` before touching the seeder.
5. Existing `NotificationPreference` rows for the two kinds are harmless; the PUT now rejects new ones.

If [P1-02](P1-02-todo-due-reminders.md) later chooses a `DigestKey`, `ReminderDigest` comes back **with** its producer.

---

## B. The routine nudge fires at 09:00 UTC for everyone

**Facts:**

- `AdhdTimeOrganizer.Routines/infrastructure/scheduling/RoutinesScheduledJobsRegistrar.cs:42` —
  `ScheduleSpec.FromCron("0 0 9 * * ?")` with no zone, and a null zone resolves to **UTC**
  (`SchedulerService.ResolveTimeZone`, framework `Sydowwe.Scheduler/infrastructure/SchedulerService.cs:300`).
- So `RoutinePeriodEndingSoon` and `RoutineStreakGraceExpiring` reach Bratislava at 10:00/11:00, New York at
  05:00, Tokyo at 18:00. Quiet hours are opt-in and nothing seeds a default, so for most users nothing defers it.
- `RoutineSuggestedToday` already does this right: `RoutineSuggestedTodayJobHandler` runs every 15 minutes, reads
  each user's zone and the day's `WakeUpTime`, and marks per user.

**The rule you must keep:** the Routines summary says the resets and the two nudges are "UTC end to end and must
stay so". That is about the **window math** — `RoutineResetService.ComputeNextReset`, and the
`EndingSoonNotifiedFor` / `GraceNotifiedFor` marks compared against a freshly computed instant. Leave it alone.
Change only **when the sweep delivers for a given user**.

**Fix:**

1. Run `RoutinePeriodNudgeJobHandler` every 15 minutes (or hourly) instead of once a day.
2. For each user, deliver only once their local time has reached the delivery moment **and** the existing marks
   say not yet notified. The marks already make it idempotent, so a higher cadence cannot double-send.
3. Delivery moment — **decision for the owner**; recommendation: the day's `WakeUpTime` + 60 min (consistent with
   `SuggestedToday`; reuse its wake-up resolution, including whatever it does when the day has no calendar row).
   If [P2-01](P2-01-morning-briefing.md) lands, these nudges may join the briefing instead.
4. Keep the SQL pre-filter tight (periods whose lead window is open and unmarked) so 96 runs a day touch almost
   nothing. Check `RoutineSweepPaging.UserPageSize` paging still applies.
5. Don't touch the 02:00 reset — it is addressed to the database, not a person.

**Tests:** two users in different zones, fixed `TimeProvider` (inject one if the handler lacks it), assert who got a
notification row at which run; a second run in the same window sends nothing; marks unchanged for a period that
was already fully done (the summary's "a nudge is marked only when a notification actually goes out" rule).

---

## C. A broken streak never mentions the freeze that could save it

**Facts:**

- `RoutinePeriodEndedPayload` (Contracts) carries `Streak` and `Outcome`; the broken text is just
  `StreakBrokenSuffix => " The streak was broken."` (`AdhdTimeOrganizer/infrastructure/notification/EnPortalNotificationTexts.cs:38`, SK twin).
- After a break, `Streak` is already 0 — so the message cannot even say which run was lost.
- `RoutineTimePeriod.FreezesRemaining` exists, and a freeze can be applied to the newest miss after the fact
  (`RoutineStreakFreezeService.Apply`; the endpoint runs `CheckGrace` first).

**Fix:**

1. Add to the payload, additively (old rows deserialize to the defaults and keep today's text):
   `int LostStreak = 0`, `int FreezesRemaining = 0`, maybe `bool CanFreeze = false`.
2. Fill them in the reset path (`RoutineTodoListResetJobHandler` → `RoutinePeriodNotificationService.NotifyPeriodEndedAsync`):
   capture the run **before** `RecomputeStreak` zeroes it, and read `FreezesRemaining` **after**
   `RefreshFreezeBudget` (the budget refills lazily; the reset job is one of the surfaces that refills it — verify
   the order in the handler).
3. Offer the freeze only if `Apply` would succeed *now* (budget > 0, newest completion is an unfrozen miss).
   Otherwise keep the plain text.
4. Text, e.g. EN "Your 12-day streak broke — you have 1 freeze left to save it." Needs a new virtual member in the
   framework's `NotificationTexts` (e.g. `StreakBrokenFreezableSuffix(int lostStreak, int freezesLeft)`) and the
   renderer branch; host wording in both portal text classes.
5. Frontend: the deep link opens the period with the freeze action.

Framework change (payload + text member + renderer) → two-repo operation.

**Tests:** reset a period with a miss and budget left → notification payload carries the lost run and the budget;
with budget 0 → plain text; after applying the freeze through the endpoint the streak is restored (asserts on the
period row, not the response).

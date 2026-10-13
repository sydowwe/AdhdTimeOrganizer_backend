# Notifications, reminders & scheduler — opportunity review

Point-in-time snapshot, **2026-10-09**. Disposable once the items land; the durable truth stays in each
module's `docs/summary.md` / `domain-map.md`.

The question this answers: across every endpoint and service in the seven slices, the host and the three
opt-in modules — **what should notify, on which channel (push / email / both), where should the Scheduler
be doing work it isn't, and which opportunities only appear when you look across modules.**

One self-contained prompt per file, written to be pasted into a fresh session in this repo. **Every prompt
assumes [`_SHARED-CONTEXT.md`](_SHARED-CONTEXT.md)** — the add-a-kind playbook, the Reminders and Scheduler
patterns, and the traps — so read that first.

## Framework revision

Every framework path and line number in this set was read **at 5646e89**, the commit the parent repo
records. On 2026-10-09 the `framework/` checkout had drifted back to 373ac55 (an ancestor, ~70 commits
older) and was reset with `git submodule update framework`. If `git status` shows
`modified: framework (new commits)` without a framework edit of your own, run `git -C framework log -1`
before committing the gitlink — it may be a regression, not new work.

## What notifies today

| Producer | Kinds | Mechanism |
|---|---|---|
| Planning — personal reminders | `PersonalReminder` | Reminders registry; **only reminders the user creates by hand** |
| Routines | `RoutinePeriodEndingSoon`, `RoutinePeriodEnded`, `RoutineStreakGraceExpiring`, `RoutineSuggestedToday` | 09:00 **UTC** nudge sweep, 02:00 UTC reset, 15-min zone-aware wake-up sweep; all four held while away (FO5) |
| History | `TimerBoundary` | one-shot scheduler job per alarm, re-registered on boot |
| Scheduler | `ScheduledJobFailed`, `ScheduledJobOverdue` | admins; email on by default |
| Notifications | `Test` | admin-only probe |

**Nothing** in TodoLists, Tracking, ActivityProfiles or Core, and nothing from the host's auth flows beyond the
four transactional mails (confirm email, confirm email change, reset link, reset code).

## Channel rules used throughout

| Channel | Use it when | Notes |
|---|---|---|
| **Push** (+ bell row) | It is about *now* or the next hour and the user would act within minutes | Default today for every kind |
| **Bell only** | Informational, no clock | ⚠ Not expressible today — push is default-on for every kind. [P2-02](P2-02-per-kind-push-defaults.md) is the prerequisite |
| **Email** | (a) security / account events — always, transactional, through `IUserEmailSenderService`, **not** `NotifyAsync`, so they cannot be muted; (b) digests the user opted into | Never default-on for anything daily — `NotificationChannelDefaults`' own comment already says so |
| **Both** | Security events only (the mail is the guarantee, the bell is the record) | |

Every new kind also gets an explicit **held-while-away** decision (`HeldWhileAwayTypes`): unsolicited and
about a moment → held; asked for, or operational → not held.

## Index

| # | Prompt | Kind | Touches | Framework change | Depends on |
|---|---|---|---|---|---|
| P1-01 | [Fix what's already wired](P1-01-fix-existing-wiring.md) — dead settings rows, 09:00-UTC nudge, freeze never offered | bug | host, Routines, tests | yes (C only) | — |
| P1-02 | [To-do due-date reminders](P1-02-todo-due-reminders.md) ⭐ biggest gap | feature | TodoLists, host | yes (1 kind) | P1-01 A |
| P1-03 | [Planner block reminders (audit N1)](P1-03-planner-block-reminders.md) + materialiser sweep + Google double alert | feature | Planning, host | likely (1 kind) | — |
| P1-04 | [Missed & overrun sweep (audit N5)](P1-04-missed-and-overrun-sweep.md) | feature | Planning, host | yes (2 kinds) | — |
| P1-05 | [Security emails](P1-05-security-emails.md) | feature / security | framework, host | yes (auth bases + template) | — |
| P2-01 | [Morning briefing](P2-01-morning-briefing.md) — one push instead of several | feature, cross-module | host + Planning / TodoLists / Routines | yes (1 kind) | P1-02 (date-only items land here) |
| P2-02 | [Per-kind push defaults](P2-02-per-kind-push-defaults.md) | framework enabler | Notifications, host | yes | — ; unblocks every bell-only item |
| P2-03 | [Push action buttons](P2-03-push-action-buttons.md) — snooze / done from the lock screen | framework + frontend | Notifications, SW, Reminders | yes | P1-02/03/04 for most actions |
| P2-04 | [Tracker went silent](P2-04-tracker-went-silent.md) | feature | Tracking | yes (1 kind) | — |
| P2-05 | [Unconfirmed accounts: remind, then purge](P2-05-unconfirmed-accounts.md) | GDPR / housekeeping | host (+ framework) | maybe | — |
| P2-06 | [Token cleanup onto the scheduler](P2-06-token-cleanup-on-scheduler.md) | housekeeping | framework / host | small | — |
| P3 | [Backlog](P3-backlog.md) — 15 smaller ideas; 11 promoted below, 4 not | ideas | various | various | — |
| P3-01 | [Quiet hours follow the sleep window](P3-01-sleep-window-quiet-hours.md) | feature | Notifications, host | yes (flag) | — |
| P3-02 | [Streak milestones and new bests](P3-02-streak-milestones.md) | feature | Routines | yes (payload) | — |
| P3-03 | [Stopwatch check-in](P3-03-stopwatch-check-in.md) | feature | History, frontend | yes (1 kind) | — |
| P3-04 | [Evening shutdown](P3-04-evening-shutdown.md) | feature | host | yes (1 kind) | P2-01 frontend |
| P3-05 | [Weekly lines in the morning briefing](P3-05-briefing-weekly-lines.md) | feature | host, Tracking | yes (payload) | P2-01 frontend |
| P3-06 | [Routine items with a time of day](P3-06-timed-routine-items.md) | feature | Routines | yes (1 kind) | — |
| P3-07 | [Account deletion grace period](P3-07-account-deletion-grace.md) | GDPR / lifecycle | host | no | — |
| P3-08 | [Daily activity budgets](P3-08-activity-budgets.md) | feature | History | yes (1 kind) | — |
| P3-09 | [Google Calendar auto-sync](P3-09-google-calendar-auto-sync.md) | feature | host | yes (1 kind) | — |

## Suggested order

1. Fix the submodule checkout (above).
2. **P1-01** — small, removes wrong promises from the settings screen and stops 5 a.m. pushes.
3. **P1-02 → P1-03 → P1-04** — the three "the app reminds me of what I said I'd do" items. P1-02 and P1-03 share
   the self-cancelling-resolver idea; do P1-02 first and copy its shape.
4. **P1-05** — independent; can run in parallel with 3.
5. **P2-02** before any bell-only kind; **P2-01** once P1-02 exists; the rest of P2 in any order.

Parallelising: the P1 items touch different slices and can go to separate worktrees, **except** that every
item adding a `NotificationType` edits the same framework files (`NotificationType.cs`,
`NotificationPayloads.cs`, `NotificationTextRenderer.cs`, `NotificationTexts.cs`) and the host's catalog —
serialise those parts or expect merge conflicts.

## What each new kind costs

Enum member + payload record + renderer case + texts (SK, EN) + email default + host catalog entry + its
second copy in `NotificationCatalogTests` + held-while-away decision + frontend `notificationTypeMeta.ts`. The
first four live in the framework (two-repo operation). That cost is the second reason — after notification
fatigue — to prefer the morning briefing over a separate kind per signal.

## Don't

- **No re-engagement email to away users.** That is marketing under §116 zák. 452/2021 and needs opt-in
  consent. FO5's hold is the right default.
- **No daily "streak extended" pushes.** Fold good news into messages that are already going out.
- **No new kind without a held-while-away decision**, and none that can re-fire for one occurrence — dismiss is
  a hard delete, so a re-fire resurrects a dismissed row (Notifications summary, *Nothing here can resurrect…*).

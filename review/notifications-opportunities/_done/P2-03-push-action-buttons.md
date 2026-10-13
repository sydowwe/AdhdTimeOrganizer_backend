# P2-03 — Push action buttons: snooze / done from the lock screen

> Paste into a fresh session. Read `review/notifications-opportunities/_SHARED-CONTEXT.md` first.
> Framework (push document) + frontend (`public/sw-push.js` in `AntiProcrastinationApp-frontend`). Most actions only
> exist once [P1-02](P1-02-todo-due-reminders.md) / [P1-03](P1-03-planner-block-reminders.md) / [P1-04](P1-04-missed-and-overrun-sweep.md) do.

## Why

Every extra step between "reminded" and "handled" is a place where an ADHD user drops the task. Today a push can only
be tapped open. Acting on it straight from the notification is the biggest friction cut in this review.

## Facts

- The Web Push document the framework builds has **no `actions`** (no match in `framework/Sydowwe.Notifications` at
  5646e89). It carries a deep link (frontend N5) and `data.originallyDueAt` for late deliveries.
- The endpoints the actions need mostly exist: the Reminders module's per-recipient **snooze / dismiss** and
  `POST /reminder-dashboard/undo`; `PatchPlannerTaskStatusEndpoint`, `PatchPlannerTaskSpanEndpoint`; the to-do
  toggle.

## Build

1. **Per-kind action list in the push document**, rendered server-side in the recipient's locale (like the body):

| Kind | Actions |
|---|---|
| `PersonalReminder` | Snooze 10 min · Dismiss |
| `TodoItemDue` (P1-02) | Done · Snooze 1 h |
| `PlannedBlockStarting` (P1-03) | Start · Skip |
| `PlannedBlockOverrun` (P1-04) | Done · +15 min |

2. `data` carries **ids only** — what the service worker needs to call the endpoint (definition id + occurrence instant
   for a snooze; task or item id otherwise).
3. Service worker: `notificationclick` with `event.action` → `fetch(api, { credentials: 'include', method, body })`,
   then close the notification; on failure, fall back to opening the deep link.
4. Endpoints must be **idempotent and owner-scoped** (the Reminders guard already self-scopes).

## Verify early

- **Cookie auth from the SW.** The API authenticates with the httpOnly `auth-token` cookie, `SameSite=Strict`, app and
  API same-site (`docs/notificationSetup.md` §4). A SW fetch to the API should carry it; prove it in Chrome and Firefox
  before building the rest.
- **Platform support.** Chrome / Edge / Android show actions (Android shows at most a few); iOS / Safari show none — the
  tap-to-open link stays the fallback everywhere.

## Tests

Server: the push document for each kind carries the action ids and the ids in `data`, in the recipient's locale.
Frontend: SW unit test of the action → request mapping.

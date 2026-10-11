# P2-01 frontend — the morning briefing

> Paste into a fresh session in `C:\Users\jakub\WebstormProjects\AntiProcrastinationApp-frontend`. The backend half
> is done (branch `p2-01-morning-briefing` in the AdhdTimeOrganizer repo). Follow that repo's conventions: `function`
> not arrow consts, PascalCase components and camelCase props in templates.

## What the backend now does

Once per user per morning — at the day's calendar wake-up (the user's `defaultWakeUpTime` when the day has no row) plus the user's offset —
the server sends **one** notification of the new type **`MorningBriefing`** in place of several separate morning
pings. It is never sent when there is nothing to say, on a vacation or sick day, or while the welcome-back card is
open. It is held in the bell for users who have been away 3+ days. Email is off by default.

Title and body are rendered **server-side** in the user's locale (sk/en), e.g.

- en: **Your day** — `4 blocks planned, first at 09:00 · 2 to carry over · 3 to-dos due today · 1 overdue · 2 routine tasks for today`
- sk: **Váš deň** — `4 naplánované bloky, prvý o 09:00 · 2 na presunutie · 3 úlohy s termínom dnes · 1 po termíne · 2 úlohy z rutín na dnes`
- no blocks but something else: `No plan yet · 5 overdue` / `Zatiaľ bez plánu · 5 po termíne`

Payload (camelCase JSON, counts only, no ids):

```ts
{
  date: string            // 'YYYY-MM-DD', the user's own day
  blocks: number          // today's open foreground planned blocks; 0 = "no plan yet"
  firstBlockAt: string | null  // 'HH:mm:ss', first of them still ahead of the send time
  carryOver: number       // blocks left open on the previous 7 days (same set as the carry-over banner)
  dueToday: number        // open to-dos due today WITHOUT a due time (timed ones have their own TodoItemDue push)
  overdue: number         // open to-dos due before today
  routinesSuggested: number  // open routine items suggested for today
}
```

`subject` is **always null** — the briefing is about the whole day, not one row. A push click with no subject opens
`/` (the service worker's existing fallback, `public/notification-subject-routes.js`); nothing to change there.

**The routine digest steps aside.** With the briefing on (the default), `RoutineSuggestedToday` is no longer sent
to that user. Turning the briefing off brings it back.

### New user preferences

On `GET /api/user/data` (response) and `PUT /api/user/preferences` (request, null/absent = leave unchanged):

| Field | Type | Default | Rule |
|---|---|---|---|
| `morningBriefingEnabled` | `boolean` | `true` | off ⇒ no briefing, and the routine "suggested for today" digest comes back |
| `morningBriefingOffsetMinutes` | `number` | `0` | 0–180 minutes after the day's wake-up; outside → 400 |

## Do

1. **`src/app/notifications/notificationTypeMeta.ts`** — add `MorningBriefing`: an icon like `sun` (or
   `mug-hot`), colour `primary`, an app-side `label` key (the framework's `notifications.type.*` block doesn't know
   this type — copy how `RoutineSuggestedToday` / `PlannedBlockStarting` do it), and a constant route.
   Recommendation: `{ name: 'home' }` — that's also where the push click lands (`/`), so both deliveries open the
   same place. Use `{ name: 'plannerCalendar' }` instead if the home view doesn't show today's plan, due to-dos and
   routines. Comment it in the file's style: no subject, constant route on purpose.
2. **Locales** — the type-filter label (sk + en), next to the other app-side type labels.
3. **`src/core/user/dto/userAugmentation.ts`** — add both fields to `User` (optional, like the rest — read the file's
   header for why) and to `UserPreferencesRequest`; extend the header comment.
4. **`src/core/user/composable/useUserPreferences.ts`** — expose `morningBriefingEnabled` (default `true` when
   unhydrated — it's the server default) and `morningBriefingOffsetMinutes` (default `0`).
5. **Settings UI** — a `MorningBriefingField.vue` in `src/core/user/component/settings/`, placed in
   `PreferencesSection.vue` right after `SleepWindowField` (the briefing hangs off the wake-up time set there):
   - a `VSwitch` "Morning briefing" with a hint like *"One notification when your day starts: today's plan, what's
     due and overdue, and today's routines. Turning it off brings back the separate routine reminder."*
   - when on, an offset picker: *"Send at wake-up / +15 / +30 / +60 / +90 / +120 / +180 min"* (a `MyBtnToggle` or
     select; values 0, 15, 30, 60, 90, 120, 180).
   - save through `userStore.setPreferences({...})` + `showSuccessSnackbar(i18n.t('user.preferenceSaved'))`, the same
     way `PreferencesSection.vue` does for `askBeforeDelete`; errors already snackbar from the axios interceptor.
   - sk + en strings in `src/core/user/_locales/user.{sk,en}.ts`.
6. **Notification settings screen** — no code change needed: `MorningBriefing` now gets its own channel row from the
   server catalog. Check it renders with the new label rather than a raw enum name.
7. **Tests** — extend `src/app/notifications/notificationSubjectRoutes.test.ts`: `MorningBriefing` with no subject
   routes to the constant route you picked, and `resolveNotificationTargetUrl({ type: 'MorningBriefing' })` is `'/'`.
   Plus a component/composable test that the unhydrated defaults are `true` / `0`, if the repo has a pattern for that.

## Don't

- Don't parse or re-render the body client-side — the server already words it in the user's language.
- Don't hide or rename the `RoutineSuggestedToday` settings row: users who turn the briefing off get it again.
- Don't treat a missing `morningBriefingEnabled` as off.

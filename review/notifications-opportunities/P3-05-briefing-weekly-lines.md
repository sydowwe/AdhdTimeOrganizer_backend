# P3-05 — Weekly lines in the morning briefing: stuck to-dos, mapping suggestions, memories

> Paste into a fresh session. Read `review/notifications-opportunities/_SHARED-CONTEXT.md` first. Backlog #7, #6, #9,
> in one prompt because all three edit the same payload, renderer and composer. Done apart, they would be three
> two-repo edits of the same four files.

## Why not three kinds

Each is informational with no clock, and alone each is worth less than the notification it would cost. The README's
rule is to fold that kind of news into a message that already goes out, and the morning briefing (P2-01) is that
message.

## Facts

- `MorningBriefingPayload` (`framework/Sydowwe.Framework.Contracts/notification/payload/NotificationPayloads.cs:368`).
  `NotificationTextRenderer.RenderMorningBriefingBody` (`framework/Sydowwe.Notifications/application/NotificationTextRenderer.cs:294`).
  Host texts in `SkPortalNotificationTexts` / `EnPortalNotificationTexts`.
- Composer `AdhdTimeOrganizer/application/service/morningBriefing/MorningBriefingComposer.cs`:
  - bulk, one query per section per page of users;
  - unauthenticated, so scope by `UserId` by hand;
  - `IsEmpty` decides whether a morning gets a briefing at all.

  The sweep is `MorningBriefingJobHandler`, one decided morning per user per day (`User.MorningBriefingHandledFor`).
- **#7 stuck:**
  - The rule is `TodoListItemPlanHelp.For(history, postponeCount)` (`AdhdTimeOrganizer.TodoLists/application/service/TodoListItemPlanHelp.cs`).
  - The history comes from Core's seam `ITodoListItemPlanSource.PlanHistoryAsync`, **per user** (Planning implements
    it; see `TodoListItemPlannedSlotFiller` for the call).
  - The seam's contract says the counts "must never reach a response". The line must be an offer ("2 to-dos might go
    easier in smaller steps"), never "pushed N times".
- **#6 mapping:**
  - The thresholds live in `MappingSuggestionRule` (`AdhdTimeOrganizer.Tracking/domain/helper/mapping/`).
  - The computation lives **inside the endpoints** (`DesktopMappingSuggestionsEndpoint`,
    `AndroidMappingSuggestionsEndpoint`): up to 500 focus intervals, one ledger query each. Too heavy for a
    quarter-hourly sweep.
  - Extract it into a Tracking service that both endpoints and the composer call. Run it only on the weekly line's
    day, and only for users with a recently seen desktop/android source (`TrackerSourceStatus`, P2-04).
- **#9 memories:** `MemoryAnchor(AnchorMonth, AnchorYear, HighlightNote, Rating)`
  (`AdhdTimeOrganizer.ActivityProfiles/domain/model/entity/MemoryAnchor.cs`). "This month in earlier years" means
  `AnchorMonth == today.Month && AnchorYear < today.Year`. Count only; `HighlightNote` never travels.

## Design

1. **Cadence.** The lines appear on the user's **first briefing of their week** (`User.FirstDayOfWeek`), never
   daily: a stuck item repeated every morning is noise. Memories appear on the first such day in a new month.
   - One new mark, `User.MorningBriefingWeeklyFor DateOnly?` (the week start last shown), written together with the
     briefing's own mark. Memories show when that week start's month differs from the previous mark's.
   - Don't reuse `MorningBriefingHandledFor` for this: it marks *decided* mornings, including empty and away ones.
2. **These lines may make a morning non-empty, on their day only.** That's at most one extra briefing a week, and the
   only way someone who plans nothing ever sees them.
3. Payload (all defaulted, so older rows still deserialise): `int StuckTodos = 0, int MappingSuggestions = 0, int MemoriesThisMonth = 0`.
   The renderer appends them after the existing sections, and the host words them as offers.
4. **Each section fails open.** A throw logs ids, counts as 0, and never costs the user the briefing.
5. One briefing has one deep link. Either keep the briefing's own link, or link by precedence
   (stuck → the to-do list filtered to plan help; mapping → the tracking distinct-entries grid; memories → the
   memory-anchor grid).

## Frontend

Nothing renders the payload, because bodies are server-rendered. But ⚠ P2-01's frontend half is still missing
(`src/app/notifications/notificationTypeMeta.ts` has no `MorningBriefing`). Land that first.

## Tests (extend `MorningBriefingJobTests`)

- The lines appear only on the first-of-week morning, get marked, and don't repeat the next day.
- Memories appear once a month.
- A first-of-week morning whose only content is stuck items sends; the same content on a Tuesday doesn't.
- A throwing section still sends the rest.
- Mapping suggestions are computed only for users with a recent tracker source.
- The extracted mapping service returns what both endpoints did before (their existing tests pin it).

## Open decisions

1. Whether the lines may make a morning non-empty (recommended yes, weekly only).
2. Deep-link precedence.

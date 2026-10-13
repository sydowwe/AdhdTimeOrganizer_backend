# P1-02 — To-do due-date reminders ⭐

> Paste into a fresh session. Read `review/notifications-opportunities/_SHARED-CONTEXT.md` first.

## Why

`TodoListItem` has `DueDate` (`DateOnly?`) and `DueTime` (`TimeOnly?`) —
`AdhdTimeOrganizer.TodoLists/domain/model/entity/TodoListItem.cs` — and **nothing ever reminds the user about
either**. For an app whose users forget deadlines by design, this is the single largest gap in the review.

## Design

### Two cases

| Item has | Behaviour |
|---|---|
| `DueDate` **and** `DueTime` | Register a one-shot reminder at the due instant with lead offsets |
| `DueDate` only | **No reminder.** There is no instant to fire at, and inventing 09:00 is wrong. These belong in the morning briefing ([P2-01](P2-01-morning-briefing.md)), together with overdue items |

### Registration

- `ReminderKey("TodoLists", "TodoListItem", itemId, "TodoItemDue")`.
- `ReminderSchedule.OneShot(dueInstant, leadOffsets)`, where `dueInstant` is `DueDate + DueTime` **in the user's
  zone**. Copy Planning's composition (`ReminderRegistrationService.ComposeTaskInstant` — zone- and DST-aware)
  rather than re-deriving it; if it should be shared, it needs a home both slices can see (Core).
- Lead offsets — **decision** (see below). TodoLists cannot read Planning's `UserPlannerSettings.ReminderMinutesBefore`
  (slice edge), so either a fixed default (e.g. `[-30]`) or a new per-user to-do setting.

### Recipients: make it cancel itself

Register with `RecipientMode.ResolverStrategy` and add a TodoLists `IReminderRecipientResolver` (key e.g.
`todo-lists.open-item-owner`). At fire time it loads the item (the context is unauthenticated, so the filter is
inert — scope by the item id from `ReminderResolutionContext`), and returns `[item.UserId]` **only if** the item
still exists, is not done, and its due instant still matches the occurrence. Otherwise it returns nothing, and the
scan writes `Skipped / NoRecipients`.

That covers every forgotten cancel path. Explicit cancels are still needed — without them `my-upcoming` lists
reminders for finished or deleted items until they fire (the label resolver must stay pure and cannot check).

### The kind

New `NotificationType.TodoItemDue`, payload `TodoItemDuePayload(long TodoListItemId, string? Title = null,
long? TodoListId = null, DateTime? DueAt = null)`.

Why not reuse `DeadlineApproaching`: it is **email default-on** (would mail every to-do), and its payload is only
`Title` — no id, so no deep link and nothing for a label resolver. (P1-01 A removes it from the catalog.)

- Email: **off** by default (not in `EmailOnByDefault`); users can opt in.
- Held while away: recommendation **no** — it is about the user's own commitment. Owner's call.
- Also add `IReminderKindCatalog` (so "to-do reminders" can be muted in `/reminder-preference` before one exists)
  and `IReminderLabelResolver` (title on `my-upcoming`) — copy Planning's `PersonalReminderKindCatalog` /
  `PersonalReminderLabelResolver`.

## Every write path that must sync (grep-verified 2026-10-09)

Put the logic in **one** service — e.g. `TodoListItemReminderSync.SyncAsync(IReadOnlyCollection<long> itemIds)` in
`AdhdTimeOrganizer.TodoLists/application/service/` — called after each save below. The host can reference
TodoLists, so host paths call the same service.

| Path | What it changes |
|---|---|
| `CreateTodoListItemEndpoint` (`CreateTodoListItemRequest.ToEntity`) | new item, maybe with a due instant |
| `UpdateTodoListItemEndpoint` (`UpdateTodoListItemRequest.UpdateEntity` writes `DueDate` **and** `IsDone`) | date, time, completion |
| `RescheduleOverdueTodoListItemEndpoint:72` | **bulk** — every overdue item in one save |
| host `ApplyFreshStartWelcomeBackEndpoint.cs:164-169` | clears or spreads due dates |
| host `UndoFreshStartWelcomeBackEndpoint.cs:71-73` | restores them |
| `BaseToggleIsDoneTodoListEndpoint`, `BaseToggleStepIsDoneEndpoint` (last step completes the item) | completion, **and un-completion** (re-register if still in the future) |
| host `ActivityTimeRecordedEventHandler`, `PlannerTaskIsDoneChangedEventHandler` | set `IsDone` on items from other slices |
| `DeleteTodoListItemEndpoint` | delete → cancel |
| `DeleteTodoListEndpoint` | **DB cascade** (`TodoListConfiguration.cs:25`) — read item ids before, cancel after |
| activity delete (items cascade with their owning activity) | same cascade trap; Core's activity delete machinery |

Considered and rejected: a `SaveChanges` interceptor like `TodoListItemCompletionInterceptor`. It would catch every
tracked write, but registering inside a save commits the registry mid-save, and `ExecuteUpdateAsync` bypasses it
anyway. Explicit calls + the resolver net + a test per path is more honest.

## Cross-module: don't double up with the planner

A to-do can be scheduled into the planner (`PlannerTask.TodolistItemId`). Once [P1-03](P1-03-planner-block-reminders.md)
exists, an item with a planner block on its due day could ring twice. Optional refinement: the resolver consults
Core's `ITodoListItemPlanSource` (implemented by Planning; it already serves `NextPlannedSlot`) and skips when a
block for that item starts within the lead window.

## Tests (rows, not routes)

- For each path in the table: the `ReminderDefinition` row exists / moved / is cancelled (seed with
  `ReminderSeedHelper`, read with `Set<ReminderDefinition>()`).
- Scan-level: an item marked done before its instant → a `Skipped/NoRecipients` dispatch row and **no**
  `Notification` row. A deleted list → the same.
- Bulk reschedule moves every definition, not just the first.
- Date-only item → no definition.

## Open decisions (ask before coding)

1. Lead offsets: fixed default vs a new per-user to-do setting (and where it lives).
2. Held while away — recommendation no.
3. Email default — recommendation off.
4. Whether to add the planner-overlap skip now or later.

## Done when

A to-do with a due time pushes at the lead time, never after it is done or deleted, appears labelled in
`my-upcoming`, can be muted as a kind, and has its own row in the notification settings.

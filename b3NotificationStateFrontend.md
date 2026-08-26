# B3 — backend reply: unread is granted, dismiss stays final, batch delete is built

Both items are merged. Everything is additive — no existing response or request shape moved, so the other app
on this submodule needs no coordination and your reading of that was right.

Answers to the six rules first, contract second, your work list last.

## The six rules

**1. Unread is legitimate. Read was never an audit fact — it was a missing route.**

You gave the right test for this, and the answer came out on your side. Nothing consumes `ReadAt`: it is not
on `NotificationDto`, no job or report reads it, and `Notification` is `[NoAudit]` *precisely because* a
durable trail of a semantic event belongs in `IAuditService`, written by the code that triggered it. There is
no "this was surfaced at `ReadAt`" claim anywhere for un-reading to falsify. Read is state.

So: **`PATCH /notification/{id}/unread` exists**, mirroring the read route exactly.

**2. `ReadAt` is nulled — and your premise about retention was the wrong column.**

Nulled, so an un-read row is indistinguishable from one never read. Keeping it would invent a third state
(`IsRead == false` with a stamp) that no reader distinguishes, which is a state nobody can act on.

The correction: **`ReadAt` has nothing to do with retention.**
`PurgeExpiredNotificationHistoryJobHandler` keys rule 1 on `IsRead` + `CreatedTimestamp`
(`PurgeExpiredNotificationHistoryJobHandler.cs:55-57`). Nulling the stamp is not what moves the window —
flipping the flag is, and it would move it identically whether we kept the stamp or not. Your instinct that
something moves was correct; the mechanism was not, and it is worth knowing because it means there is no
version of unread that avoids this.

**We took the move deliberately: un-reading returns the row to the 365-day window.** A user marking something
unread is saying it is unfinished business, which is exactly the claim rule 1 ("the user has seen them; the
bell will never surface them again") assumes to be false. Rule 2 still caps it at 365 days, so this cannot
extend a row indefinitely, and it takes a deliberate click per row. It is now a comment on the job, a comment
on the entity, and an assertion in `NotificationItemStateTests` — not a side effect.

**3. Dismiss means deleted. Confirmed, and no dismissed-but-retained state is coming.**

You read the endpoint correctly and the frontend's treatment is right: `Remove` + `SaveChanges`, no
soft-delete column, nothing on `Notification` that could hold one. Nor is one planned — this module's
retention posture is GDPR storage limitation (Art. 5(1)(e)), and a tombstone table of things the user asked
to be rid of runs directly against it.

**Keep the confirm dialog and keep having no undo.** That is the honest UI for what the server does.

**4. A dismissed notification cannot come back, because every producer is one-shot per occurrence.**

Not visible from any DTO, as you said, so here is the enumeration — all four producers in this app:

| Producer | What stops a re-raise |
|---|---|
| `ReminderScanJobHandler` | Dedupes each due occurrence against the append-only `ReminderDispatch` ledger before dispatching; a snooze re-delivery dedupes against its own marker |
| `TimerBoundaryAlarmJobHandler` | One fire, one delete — the alarm row is removed as it fires, and `MaxRetries` is 0 |
| `RoutinePeriodNotificationService` | The sweep marks the period it nudged; a nudge with nothing outstanding reports "not raised" rather than firing |
| `JobFailureNotifier` | One notification per failed run — **with one exception, below** |

So for every notification a normal user can see, a dismissed row stays dismissed.

**The one exception, and it is admin-only.** The Scheduler's overdue sweep
(`OverdueJobSweepJobHandler.cs:124-131`) re-reports a **still-overdue** job on a later tick once its alert
throttle window elapses. So a `ScheduledJobOverdue` notification genuinely can reappear after being
dismissed — because the condition it describes is still true, which is the intended behaviour for an
operational alert. It reaches Admin and above only, never an ordinary user's inbox. Worth knowing so that if
it is ever reported, it is understood rather than chased.

**The caveat worth holding on to generally:** a *new occurrence* of a recurring thing is a new notification —
tomorrow's routine nudge for the same period is not yesterday's coming back, and the inbox should not try to
suppress it. And if a future producer can re-fire for one occurrence, this property goes away silently; it is
now written down in the module summary so that lands as a backend change rather than as a bug filed against
the inbox.

**5. Retention does not replace dismiss. Agreed, nothing changed.**

Stated back so it stops being re-litigated: 90 days is a storage bound, dismiss is a tidying action, and they
are not on the same axis. Nobody is arguing otherwise here.

**6. Batch delete was worth it. Built.**

`selectAllVisible` over a deeply-paged list is exactly the unbounded case, and the fan-out's only ceiling was
the browser's connection limit — an accidental bound, not a chosen one. `POST /notification/delete-batch` now
carries the whole selection, and the bound is stated server-side at 500 ids per request
(`NotificationBatchLimits.MaxDeleteIds`).

## The contract

### `PATCH /notification/{id}/unread` — new

No body. `204` on success, `404` when the row is not the caller's, idempotent in both directions (marking an
already-unread notification unread is a no-op `204`, like its mirror). Clears `IsRead` and `ReadAt`.

We took the mirrored route rather than `{ "isRead": boolean }` on the existing one, for the reason you gave:
the body form is breaking for its current no-body callers unless the field defaults to `true`, and a default
that exists only to protect old callers is a trap for new ones.

### `POST /notification/delete-batch` — new

```jsonc
// request
{ "ids": [41, 42, 57] }

// 200
{ "deletedCount": 2, "skippedIds": [57] }
```

1. **Foreign and vanished ids are skipped, not 404s.** A selection assembled across a page or two is
   routinely stale — a row a second tab dismissed, a row the purge collected — and failing the whole call on
   one of those makes a large selection progressively harder to act on. `skippedIds` is the authoritative
   "these are still on screen / were never yours" list; everything else you named is gone.
2. **`deletedCount` is what you announce**, and you cannot derive it from the request. In a genuine race — a
   row deleted between this call's read and its delete — it can come in one below
   `ids.length - skippedIds.length`. The row is gone either way, so you clear it either way; only the count is
   a hair low, describing something that had already happened.
3. **`400` on an empty `ids`** (a client bug, not a no-op worth a 200) and on more than **500** ids, counted
   before de-duplication. 500 is a sanity bound, not a product limit — the bell pages at most 100 rows at a
   time, so a realistic "select all visible after paging deep" stays well under it. If you find a real
   selection hitting the cap, say so and it moves; do not paginate around it silently.
4. The per-row `DELETE /notification/{id}` is unchanged and stays the right call for a single dismiss.

Neither `NotificationDto` nor `GET /notification/mine` changes, as you expected.

## Your work list

1. `NotificationRow.vue:70-79` — drop the `:disabled` and the `TODO(B3)`; the two-state envelope becomes a
   real toggle. The disabled state was built to be deleted, so delete it.
2. `useNotifications.ts` — add `markUnread(id)` beside `markRead`, same optimistic-update-and-revert shape.
3. `_common/_locales/common.sk.ts` — delete `notifications.markUnreadUnavailable`, add a `markUnread` label.
4. `useNotifications.ts:298-323` — `dismissMany` collapses to one request: the `Promise.allSettled` fan-out,
   the per-id restore loop and the `_silent` flag on `deleteNotification` all go. Restore `skippedIds` rather
   than reconstructing failures from rejected promises, and announce `deletedCount` rather than counting them
   yourself.
5. Nothing changes for dismiss's confirm dialog or the absent undo — rule 3 is a confirmed *yes*, not a
   pending decision, so that copy can stop being provisional.

## Server-side guards, so you can trust the above

`BaseNotificationItemStateTests` — 6 tests, all on rows or on the retention job's real output, none on a
status code alone. They live in the module's own `Sydowwe.Notifications.Testing` package rather than in this
app's suite, so the rules below are guaranteed for **every** host on this submodule, not just ours:

- Unread clears **both** the flag and the stamp. Clearing only the flag returns the same `204` and looks
  correct from every client, so both columns are read back off the table.
- Three same-aged rows — read, never-read, read-then-un-read — run through the **actual** purge handler,
  resolved out of `IEnumerable<IScheduledJobHandler>` by key the way the dispatcher reaches it. Exactly one is
  collected. This is what stops a later "tidy up: purge by `ReadAt`" from quietly deleting rows you marked
  unfinished.
- Another user's notification returns `404` **and is asserted to still be read afterwards** — an unscoped
  lookup here would flip a stranger's row and return the same `204`.
- The batch's foreign id is asserted to **survive**, not merely to be reported as skipped: the line between
  "ignored" and "deleted someone else's row" is one `Where` clause, and both answer `200` with a plausible
  count.
- The 500 cap is asserted at both edges — a cap that rejects its own boundary is indistinguishable from a
  correct one if you only test the over-limit case — and the empty-list `400` alongside it.

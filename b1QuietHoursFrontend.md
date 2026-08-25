# B1 — backend reply: quiet hours move to the user's clock, late deliveries say they are late

Backend change is merged. Three things changed on the wire, all additive except one field removal that
was a no-op anyway. Answers to your four questions first, contract second, your work list last.

## The four questions

**1. Whose midnight? — yours was the right reading, and the code was wrong.**

`startMinute` / `endMinute` are now read in **the user's own `User.Timezone`**. You were correct that this
is the only interpretation under which "22:00–06:00" means what the user typed; the dispatcher was reading
them in `Application:Timezone` while the reminder instant next to them was already composed in the user's
zone. Nothing errored, which is why it survived.

The zone is **not stored on the quiet-hours row**, deliberately. The window is a standing wall-clock
instruction that follows the user when they move — freezing the authoring zone would keep a relocated user
being woken on their old country's schedule until they noticed and re-saved. It is resolved per read from
the user row.

**Existing rows are reinterpreted, not migrated.** No stored value changes meaning: every account's
`Timezone` is the deployment zone today, so the two readings coincide for all of them. An account that
was *not* in the deployment zone was already being enforced wrongly, and now is not. There is no backfill
and nothing for you to prompt the user about.

**2. DST — the rule is now pinned, documented and yours to display.**

A minutes-from-midnight window genuinely has no reading for the skipped or repeated hour, so the server
picks one, in the direction of not waking anyone:

- A boundary landing in the **spring-forward gap** (a wall-clock time that never happens) moves forward to
  the first reading that does exist — the window ends at the transition.
- A boundary landing in the **repeated autumn hour** resolves to the **later** of the two instants — the
  window is never cut short.

Net effect: a 22:00–06:00 window is 23 hours long on one night a year and 25 on another. Say that in the
card subtitle if you want; saying nothing (today) reads as "this is exact", which it is not.

**3. Deferred reminders — we took (b), deliver marked-late.**

We went one option past your preference. (a) is cheaper, but the in-app row is the thing the user actually
scrolls back through, and silently not sending a push for it leaves the bell showing a reminder with no
indication it was about last night. Marking it costs one nullable instant and gives you the "was due at
23:30" line you described.

Two distinct latenesses, one threshold, both surfaced the same way:

- **Stale content** — the notification was *about* an earlier instant. Only the reminder scan knows this
  (a due occurrence held behind a quiet window all night). Persisted, so the bell shows it forever.
- **Late background delivery** — quiet hours held Web Push/Email while the row and the in-app push went out
  on time. Only the push carries it; the bell deliberately does not, because the bell was punctual.

The threshold is **30 minutes**, a server-side constant, not per-deployment — both apps on this module must
render the same thing. Producers pass the instant unconditionally and the server drops it when the delivery
was punctual, so **you will not see this field on the overwhelming majority of notifications.** That is the
intended state, not a bug.

**One gap, flagged rather than half-done: the Email channel carries no marker.** Its body is rendered
server-side, so a "was due at …" line there means formatting an instant in the recipient's zone and locale
mid-dispatch, and an unzoned timestamp in an email is worse than no timestamp. If a deferred email arriving
stale matters, say so and it is a separate small change.

**4. `ChannelHint` was metadata by design. It is now gone.**

Removed from `ReminderKindPreference`, both DTOs, the endpoint and the database. It was never enforceable —
`INotificationService.NotifyAsync` has no channel selector — and the user already has a **real** per-type,
per-channel switch in the notification preferences. Making the hint true would have given them two competing
channel switches whose disagreement has no defined winner; the honest fix was to delete the one that lied.

**Delete the `VSelect` from `ReminderKindRow.vue` and its enum options.** No coordination needed on
timing: a `channelHint` still present in a `PUT /reminder-preference/kind` body is ignored (unknown members
are dropped), so an un-updated client keeps saving opt-outs correctly in the meantime.

> Not in scope, but you should know: `ReminderDefinition.ChannelHint` still exists and is still unenforced.
> It is registration-side metadata an owning module sets, not a user-facing control, so it was left alone.
> It never reaches `/nastavenia/pripomienky`.

## The contract now

### `GET /notification-quiet-hours` — one new field

```jsonc
{
  "quietHours": {
    "startMinute": 1320,
    "endMinute": 360,
    "timeZone": "Europe/Bratislava"   // NEW
  }
}
```

1. **`timeZone` is an IANA id**, always — normalized server-side, so it is `"Europe/Bratislava"` and never
   `"Central Europe Standard Time"` regardless of which OS built the response. Hand it straight to `Intl`.
2. **Non-null whenever `quietHours` is non-null.** When no window is set the response is still
   `{"quietHours": null}` — no zone beside a null window, because the zone describes how to read *these*
   minutes, not a standalone setting. If the card needs the user's zone before a window exists, read it from
   the existing user/preferences payload.
3. **`PUT` is unchanged and carries no zone.** The minutes are interpreted in whatever the caller's
   `User.Timezone` is at delivery time. Do not add a zone to the write.

### Notifications — one new field, on both surfaces

`GET /notification/mine` and the SignalR `NotificationDto`:

```jsonc
{
  "id": 41, "type": "ReminderDue", "title": "…", "body": "…",
  "createdAt": "2026-08-25T04:05:00Z", "isRead": false,
  "originallyDueAt": "2026-08-24T21:30:00Z"   // NEW, nullable, UTC ISO-8601
}
```

Web Push document — inside the existing nested `data` object, **not** at the top level (the four keys
`sw-push.js` reads positionally are untouched):

```jsonc
{ "title": "…", "body": "…", "tag": "…", "url": "…",
  "data": { "id": 41, "type": "ReminderDue", "originallyDueAt": "2026-08-24T21:30:00Z" } }
```

`originallyDueAt` is **absent entirely** when the delivery was punctual — omitted, not `null`, matching how
`tag` and `url` are handled. Render "was due at …" only when the key is present.

It is absolute UTC: format it in the **viewer's** zone, not the window's. The bell is read on whatever
device is to hand, and this instant is a point in time, unlike the quiet-hours minutes which are wall clock.

### `GET|PUT /reminder-preference/kind` — one field removed

`channelHint` is gone from both the response rows and the request body. Everything else is unchanged.

## Your work list

1. `QuietHoursWindow.ts` — add the zone; the top-of-file comment stops being a half-truth.
2. `QuietHoursCard.vue:124-125` — format the two times against `timeZone` instead of as bare wall clock, and
   show the zone next to the window when it differs from the browser's. That difference is exactly the case
   the old bug hid, so it is worth surfacing rather than hiding.
3. Card subtitle — state the DST rule from (2) if you want the promise to be honest.
4. Notification renderer — render "was due at …" when `originallyDueAt` is present, in the viewer's zone.
   Expect it to be absent nearly always.
5. `ReminderKindRow.vue:22-33` — delete the channel `VSelect`, its enum options and the
   `reminderPreference.kind.channelHint` string. No backend coordination needed.

## Server-side guards, so you can trust the above

`QuietHoursUserZoneAndLateDeliveryTests` gives two users the **same** window minutes in **different** zones
at one instant and asserts exactly one defers — under the old single-zone reading both outcomes are
identical whichever way it falls, so the test cannot pass by coincidence. `DeferredUntil`'s value is pinned
separately (getting the defer decision right and the resume instant wrong would release a push twelve hours
off, which no "is it deferred" check catches), the IANA normalization is pinned because it only breaks on one
OS, and the late marker is asserted in both directions — present when late, absent when not — on the parsed
push JSON rather than on a row.

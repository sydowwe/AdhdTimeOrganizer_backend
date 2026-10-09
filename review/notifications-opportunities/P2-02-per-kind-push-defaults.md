# P2-02 — Per-kind push defaults (framework enabler)

> Paste into a fresh session. Read `review/notifications-opportunities/_SHARED-CONTEXT.md` first.
> Framework change — two-repo operation. **Prerequisite for every bell-only kind in this folder.**

## Facts (framework at 5646e89)

`framework/Sydowwe.Notifications/application/NotificationChannelDefaults.cs`:

```csharp
public static bool IsEnabledByDefault(NotificationType type, NotificationChannel channel) =>
    channel != NotificationChannel.Email || EmailOnByDefault.Contains(type);
```

InApp and Web Push are on for **every** kind; only email varies per kind. A low-urgency kind (mapping suggestions
ready, a stuck to-do, a memory anchor, weekly stats) would land on the lock screen for every user who never touched
their settings.

## Build

Let the **host** declare per-kind channel defaults through `NotificationCatalogOptions`, e.g.
`o.PushOffByDefault = [ … ]` (or a general `DefaultChannels[type]`). That fits the B4 direction: the deployment owns its
catalog, and the framework does not carry app vocabulary. Keep `NotificationChannelDefaults` as the fallback for the
framework's own kinds.

Every reader of "enabled by default" must use the same rule:

- the dispatcher's live fan-out;
- the deferred flush (re-checks preferences per row);
- `DeferredUntil` stamping — only for recipients who would actually get push or email;
- the "all channels off → no history row" rule;
- `GET /notification-preference/mine` — the base test `Mine_StillReportsTheEffectiveDefaults` pins this.

Extend `BaseNotificationCatalogTests` (in `Sydowwe.Notifications.Testing`) so a push-off kind is reported correctly and
is not pushed; the host's `NotificationCatalogTests` inherits it.

## Open decision

A set of push-off kinds vs a full per-kind default matrix. The set is enough for everything proposed here.

# Notifications

> Cross-cutting push notifications: reaches users live while the app is open (SignalR)
> and in the background while it is closed (Web Push).

## What it does

Any module or background job can notify users without owning delivery itself. A caller asks `INotificationService.NotifyAsync(recipients, type, payload)`; this module resolves the recipients, honours
their per-channel opt-outs, persists a history row (the bell list), and fans the message out over two channels — SignalR for connected clients and Web Push (VAPID, no Firebase) for closed ones. Used
for things like low-stock alerts, deadlines, and admin test pings.

## Setup / running

Web Push needs a VAPID key pair and the `PushNotification` config section; the SignalR hub and options binding are already wired into both hosts. Full instructions (key generation, secret placement,
migrations, browser/iOS caveats) live in
[`../docs/notificationSetup.md`](../docs/notificationSetup.md). Without VAPID keys configured, the in-app SignalR channel still works; only Web Push is inert.

## Docs

- `summary.md` — start here if you're working in this module
- `domain-map.md` — architecture, model, invariants, recipients, endpoints, navigation index
- `testing.md` — how this module is tested and the remaining gaps
- [`../docs/notificationSetup.md`](../docs/notificationSetup.md) — VAPID / config / deploy setup

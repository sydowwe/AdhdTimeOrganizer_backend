# Notification Setup

How to configure Web Push and the Email channel for the backend. One-time admin setup +
per-deployment secrets. (§7 covers Email; §1–6 cover Web Push.)

## 1. Generate a VAPID key pair (one-time, admin)

VAPID = the keypair that identifies *your server* to the browser push services. You generate
**one** keypair and reuse it across deployments (or one per deployment — your call; keep them
stable, because changing the public key invalidates every existing browser subscription).

Pick any one method:

**npm (web-push CLI):**
```bash
npx web-push generate-vapid-keys
# → Public Key:  B... (base64url, ~87 chars)
# → Private Key: ... (base64url, ~43 chars)
```

**OpenSSL (P-256):**
```bash
openssl ecparam -genkey -name prime256v1 -out vapid_private.pem
openssl ec -in vapid_private.pem -pubout -out vapid_public.pem
# convert to the base64url form the libraries expect (or use the web-push CLI, simpler)
```

You need the **base64url** public and private keys (not PEM) for config.

## 2. Backend config

Bound to `PushNotificationOptions` from the `PushNotification` section:

```json
"PushNotification": {
  "VapidSubject": "mailto:admin@yourdomain.com",
  "VapidPublicKey": "<base64url public key>",
  "VapidPrivateKey": "<base64url private key>"
}
```

- `VapidSubject` — a `mailto:` or `https:` contact; push services may use it to reach you.
- The **public key** is non-secret and is also handed to the frontend (it goes into
  `PushManager.subscribe()`).
- The **private key is a secret** — do **not** commit it. `appsettings.json` ships with empty
  key placeholders; fill the real values via the secret mechanisms below.

### Where the private key lives

| Environment | Mechanism |
|---|---|
| Local dev | `.env` (already loaded via `DotNetEnv` in `Program.cs`) **or** user-secrets: `dotnet user-secrets set "PushNotification:VapidPrivateKey" "<key>"` |
| Production | Environment variables / secret store. Config keys map with `__`: `PushNotification__VapidPrivateKey`, `PushNotification__VapidPublicKey`, `PushNotification__VapidSubject` |

Both hosts (`MojaDigitalnaFirma.AdminPortal.Sandbox`, `MojaDigitalnaFirma.HBCleaning.AdminPortal`)
read the same `PushNotification` section.

> Tip: expose the public key to the SPA however you expose other public config (e.g. a config
> endpoint or build-time env var). Don't hardcode it in two places — a mismatch silently breaks
> subscriptions.

## 3. Database

The module adds three tables: `notification`, `push_subscription`, `notification_preference`.
Generate + apply the migration yourself (per host context), e.g.:

```powershell
dotnet ef migrations add AddNotifications --project MojaDigitalnaFirma.AdminPortal.Sandbox
dotnet ef database update --project MojaDigitalnaFirma.AdminPortal.Sandbox
# repeat for MojaDigitalnaFirma.HBCleaning.AdminPortal
```

## 4. What the backend already wires up

Done in code — no action needed:
- `services.AddSignalR()` + `app.MapHub<NotificationHub>("/hubs/notifications")` in both hosts.
- `Configure<PushNotificationOptions>(... "PushNotification")` in both hosts.
- The hub authenticates with the same **httpOnly `auth-token` cookie** as the rest of the API —
  the browser sends it on the WebSocket handshake, so there is no `?access_token=` / token-in-JS
  step. SPA and API are deployed same-site (same registrable domain — `app.`/`api.` subdomains are
  same-site, and the port difference doesn't matter to SameSite), so the cookie's `SameSite=Strict`
  carries it to the handshake (set centrally in `Sydowwe.Framework/domain/helper/AuthCookies.cs`);
  the host's `CookiePolicyOptions.MinimumSameSitePolicy` is `Unspecified` so it doesn't rewrite it.
  (If the SPA is ever served from a *different registrable domain* than the API, that's genuinely
  cross-site — switch the cookie to `SameSite=None; Secure` and revisit CSRF.)
- CORS `AllowFrontend` already allows the SPA origins with credentials, and the hub uses it
  (`RequireCors("AllowFrontend")`).

## 5. User-facing flow (what happens in the browser)

1. User grants notification permission (browser prompt).
2. Frontend registers the service worker and calls `PushManager.subscribe()` with the VAPID
   **public** key.
3. Frontend POSTs the subscription (`endpoint`, `p256dh`, `auth`) to `/api/push-subscription`.
4. Backend stores it against the user; jobs/events now reach that device even when closed.

## 6. Platform caveats

- **Desktop Chrome/Edge/Firefox** — works in a normal browser tab.
- **Android Chrome** — works; best as an installed PWA.
- **iOS / iPadOS (Safari)** — Web Push works **only when the app is installed to the Home
  Screen** (PWA), iOS 16.4+. A normal Safari tab gets **no** background push. Make sure the PWA
  manifest + service worker are in place (see frontendPrompt.md).
- Use the `POST /api/notification/test` endpoint (Admin) to verify the whole path end-to-end.

## 7. Email channel (SMTP)

The third channel. It reuses the framework's existing SMTP sender (`EmailSenderService`, MailKit) —
the same one that sends password-reset mail — so a deployment that already sends any email needs **no
new secrets**.

### Environment variables

All five are required; they are read by `EmailSenderService`'s constructor:

| Variable | Example | Notes |
|---|---|---|
| `MAIL_SMTP_SERVER` | `smtp.gmail.com` | |
| `MAIL_SMTP_PORT` | `465` | connected with `SslOnConnect` |
| `MAIL_SMTP_USERNAME` | `info@yourdomain.com` | |
| `MAIL_SMTP_PASSWORD` | *(secret)* | app password / API key — `.env` or a secret store, **never** the repo |
| `MAIL_FROM_EMAIL` | `info@yourdomain.com` | envelope From |

Non-secret values live in `Properties/launchSettings.json` (dev) or the deploy environment; the
password belongs in `.env` (see each host's `config/.template.env`) or a secret store.

### Config section (optional)

```json
"EmailNotification": {
  "PortalUrl": "https://app.yourdomain.com",
  "MaxConcurrency": 4
}
```

- **`Enabled`** — omitted (the default) means *auto-detect*: the channel turns itself on exactly when
  all five `MAIL_*` vars are present. Set it to `false` to keep SMTP for password resets while
  silencing notification email, or `true` to force it on.
- **`PortalUrl`** — target of the "Otvoriť v portáli" button. Falls back to `Application:Domain`;
  the button is omitted entirely when neither is set.
- **`MaxConcurrency`** — parallel sends (default 4). Keep it low: the sender opens a fresh SMTP
  connection per message and providers throttle.

**When SMTP is absent the email branch short-circuits** — no attempt, no exception, other channels
unaffected. Delivery failures are logged per recipient (by `{UserId}` — addresses are never logged)
and never fail the batch or the triggering business operation.

### What users see

Email is **not** default-on for every notification type — see the defaults matrix in
`MojaDigitalnaFirma.Core.Notifications/docs/domain-map.md`. Approvals, compliance breaches and
deadlines mail by default; digests (`ReminderDigest`, `UpcomingHrEvents`) and `Test` do not.
Users override per (type, channel) via `PUT /api/notification-preference`.

> **Legal:** this channel is for **operational** notifications only. Marketing email requires
> opt-in consent under §116 zák. 452/2021 — do not repurpose it.

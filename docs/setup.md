# Deployment & Setup

How to run an AdminPortal (Sandbox or HBCleaning) so that authentication and request
throttling are actually secure. The app is a Kestrel process that **must** sit behind a
reverse proxy — it is not meant to be exposed directly.

## Topology

```
client ──HTTPS──▶ reverse proxy (nginx / Cloudflare / ingress) ──HTTP──▶ Kestrel :8080
```

The container listens on `http://+:8080` (see `DockerfileSandbox` / `DockerfileHbcleaning`).
TLS terminates at the proxy; the proxy forwards the real client IP and scheme to Kestrel via
`X-Forwarded-For` / `X-Forwarded-Proto`.

## Why the reverse proxy matters for security

Several auth endpoints (`/api/auth/login`, `/api/auth/microsoft-sign-in`, `/api/auth/refresh`,
the 2FA endpoints, `change-password`) are rate-limited with FastEndpoints `Throttle(...)`.
**FastEndpoints throttles on the `X-Forwarded-For` request header.** That header is only
trustworthy if a proxy you control sets it — otherwise a caller just sends a fresh value per
request and the throttle never accumulates (brute-force protection becomes useless).

The app hardens this in two steps (`framework/Sydowwe.Framework/config/ForwardedHeadersExtensions.cs`):

1. `AddTrustedForwardedHeaders()` — configures `UseForwardedHeaders` to resolve the client IP
   into `HttpContext.Connection.RemoteIpAddress`, trusting **only** the proxy networks you
   declare (see `TRUSTED_PROXY_NETWORKS` below). `ForwardLimit = 1` (one proxy hop).
2. `UseClientIpThrottleKey()` — runs right after `UseForwardedHeaders()` and overwrites
   `X-Forwarded-For` with that validated `RemoteIpAddress`, so the throttle counts per real
   client IP and ignores any forged/rotated header value.

For this to be correct you must do **both** of the following at the proxy:

- **Overwrite (do not append) `X-Forwarded-For` with the real client IP.** If the proxy
  *appends* (`$proxy_add_x_forwarded_for`), an attacker can prepend junk entries and rotate the
  string. Use the real-IP form:
  - nginx: `proxy_set_header X-Forwarded-For $remote_addr;`
  - Cloudflare: rely on `CF-Connecting-IP`; strip/normalise inbound `X-Forwarded-For` so only
    the trusted IP reaches the app.
- **Make the app reachable only through the proxy** (network isolation — don't publish `:8080`
  publicly). If clients can reach Kestrel directly, they bypass the proxy and can forge the
  header again.

### `TRUSTED_PROXY_NETWORKS`

Comma-separated CIDR ranges and/or bare IPs of the reverse proxy as seen by Kestrel. This is
the list `UseForwardedHeaders` will believe when reading `X-Forwarded-For`.

```
TRUSTED_PROXY_NETWORKS=10.0.0.0/8,172.18.0.0/16
```

- **Unset** → only loopback is trusted. Behind a containerised proxy this means the forwarded
  IP is ignored and **every** client collapses onto the proxy's IP — i.e. one shared throttle
  bucket for the whole world. You must set it in any real deployment.
- In Docker Compose, this is the compose network subnet (or the proxy container's gateway IP).
- In Kubernetes, the pod/ingress network CIDR.

## Required environment variables

Set via the container environment (or a `.env` loaded by `DotNetEnv` in development).

| Variable | Purpose |
|---|---|
| `DB_HOST` / `DB_PORT` / `DB_USER` / `DB_PASSWORD` / `DB_NAME` | Primary Postgres connection |
| `LOG_DB_USER` / `LOG_DB_PASSWORD` | Serilog log database credentials |
| `PAGE_URL` / `API_URL` | Front-end and API base URLs (CORS, links) |
| `JWT_ISSUER` / `JWT_AUDIENCE` | JWT validation parameters |
| `ECDSA_PRIVATE_KEY_PATH` | Path to the EC private key that signs auth JWTs (e.g. `secrets/ec_private.pem`) |
| `ENTRAID_TENANT_ID` / `ENTRAID_CLIENT_ID` / `ENTRAID_CLIENT_SECRET` | Microsoft Entra ID SSO |
| `ROOT_ADMIN_USERNAME` / `ROOT_ADMIN_PASSWORD` / `ROOT_ADMIN_ENTRAID_ID` | Seeded root admin |
| `COMPANY_DOMAIN` | Allowed company email domain |
| `RECAPTCHA_SECRET` | Google reCAPTCHA verification |
| `LIBRE_OFFICE_URL` | LibreOffice service for document conversion |
| `TRUSTED_PROXY_NETWORKS` | Trusted reverse-proxy ranges (see above) |
| `FIELD_ENCRYPTION_KEY` | AES-256 key (base64, 32 bytes) for at-rest encryption of high-sensitivity columns — see below |

### `FIELD_ENCRYPTION_KEY` (GDPR Art. 32 column encryption)

`Employee.BirthNumber` and `Employee.Iban` are encrypted at rest via an EF Core value converter
(`framework/Sydowwe.Framework/infrastructure/persistence/encryption/`). The key is **required** — the app throws on
startup without it.

- Generate a fresh key per deployment: `openssl rand -base64 32`. **Each database has its own key.**
- Keep it only in the deployment's `.env` (git-ignored) — never in the repo or `appsettings`.
- **Rotating the key orphans existing ciphertext.** Re-encrypt before changing it (the stored token is
  versioned `enc:v1:…` to allow a future migration).

**One-time schema migration.** The columns must be `text` (ciphertext is longer than the plaintext). Generate
and run the EF migration once: `dotnet ef migrations add EncryptEmployeePii` then update the database. On the
first startup after deploy, `EmployeeSensitiveDataEncryptionBackfill` encrypts any pre-existing plaintext rows
automatically (idempotent — safe to leave wired). No further action needed.

## Secrets

The portal expects an ECDSA key pair under `secrets/` in the content root:

- `secrets/ec_private.pem` — signs auth JWTs (path also given by `ECDSA_PRIVATE_KEY_PATH`).
- `secrets/ec_public.pem` — validates them (loaded in `config/IdentityServiceExtensions.cs`).

The Dockerfiles create `/app/secrets` with restrictive permissions; mount the real keys there
at deploy time (never bake them into the image).

## Example nginx server block

```nginx
server {
    listen 443 ssl;
    server_name app.example.com;

    # ... ssl_certificate / ssl_certificate_key ...

    location / {
        proxy_pass http://app-backend:8080;
        proxy_set_header Host              $host;
        proxy_set_header X-Forwarded-For   $remote_addr;   # overwrite, NOT append
        proxy_set_header X-Forwarded-Proto $scheme;

        # WebSocket upgrade for the SignalR notification hub (/hubs/notifications)
        proxy_http_version 1.1;
        proxy_set_header   Upgrade    $http_upgrade;
        proxy_set_header   Connection "upgrade";
    }
}
```

With `app-backend` on the `10.0.0.0/8` Docker network, set
`TRUSTED_PROXY_NETWORKS=10.0.0.0/8` so the app trusts that proxy's `X-Forwarded-For`.

## Local development & tests

- Development loads configuration from a `.env` file via `DotNetEnv`.
- Integration tests run the real `Program` against a Postgres container and never send a
  forwarded header, so `TestWebApplicationFactory` assigns each request a synthetic client IP
  (unique per request, or a constant one taken from a test-supplied `X-Forwarded-For`) so the
  IP-keyed throttle behaves deterministically. See `docs/testing.md`.

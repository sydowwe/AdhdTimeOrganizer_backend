G. Stale doc/comment cross-references. 13 spots in docs/*.md (plus one code comment in framework/Sydowwe.Framework/application/dto/request/interface/ICreateRequest.cs:11) point at MojaDigitalnaFirma.AdminPortal* /
MojaDigitalnaFirma.HBCleaning.Tests paths that don't exist here. I left these deliberately — rewriting them to AdhdTimeOrganizer.* would invent paths that also don't exist. They should be fixed once you know where the
equivalents actually live (after B/E), or the review docs deleted as other-repo history.




1. Start the app. Everything I did to DI is reasoned from source, never observed running. I deliberately didn't launch it because startup seeds a
   remote dev DB (187.77.77.42). First dotnet run is the real test of the 7 registration fixes.

2. Apply the migration. 20260727202524_IntegrateNotificationsRemindersScheduler — 12 tables, plus user.current_locale → locale and is_active default
   true. Not applied.


4. B — framework/app duplication. Two live copies of BaseTableEntity, Result, BaseGridEndpoint, the DI
   markers, the role enums, PartitionedNpgsqlMigrationsSqlGenerator. Workarounds that exist purely because of it and should be
   deleted when it lands:
- ModuleServiceExtensions dual marker scan
- the AuthMethodEnum alias in AuthFunctionalTests and the qualified call in GoogleSignInEndpoint

   The seeder family is **done** — one copy in Framework, four kinds (`IAppWideDefaultSeeder` /
   `IPerUserDefaultSeeder` / `IAppWideDevSeeder` / `IPerUserDevSeeder`) + four managers. `ModuleDevSeederAdapter`,
   both app managers, `UserDefaultSeederManager` and the four app seeder interfaces are deleted; the portal scan now pins
   the Framework assembly explicitly so the managers are always found. Untested at runtime — see item 1.

5. Syncfusion key. SYNCFUSION_LICENSE_KEY is not in config/.template.env — add it so the next person knows it exists. One package left
   (Syncfusion.XlsIO.Net.Core); Community License covers PDF later.

6. CurrentLocale wire contract — 5 sites across UserRequest, GoogleSignInResponse, GoogleSignInEndpoint. Entity is Locale now; DTOs still expose
   currentLocale to the Vue frontend. Rename both together or leave both.

7. Cleanup:
- AdhdTimeOrganizer/reference/mojaCore/ — 7 files, delete once the wiring is proven running
- 31 stale doc references to MojaDigitalnaFirma.AdminPortal / HBCleaning.Tests across module docs/
- Two files still staged-added-but-deleted (ValueRequest.cs, GetByDateCalendarValidator.cs) — will commit as deletions if you don't git restore them

8. B14 — Google Calendar OAuth (server side landed 2026-09-21, commit 1e0dc3b; framework 23e13fe pushed, parent NOT pushed):
- Google Cloud console: on the OAUTH2_GOOGLE_CLIENT_ID client, add `https://localhost:3000/google-calendar/callback` as an
  authorised redirect URI, and `<PAGE_URL>/google-calendar/callback` for every other environment. The API derives the URI
  from PAGE_URL now (the old `GoogleCalendar:RedirectUri` key is gone), so a missing registration surfaces as Google's own
  `redirect_uri_mismatch` page — not a server error.
- Confirm the production API host shares a registrable domain with PAGE_URL (`adhdtimeorganizer.com` ↔ e.g.
  `api.adhdtimeorganizer.com`). The state cookie is `SameSite=Strict`; on an unrelated domain it will not ride the
  POST /connect and every connect is the "state is missing" 400 again. The repo does not record the API host.
- Frontend: read `configured` off `GET /api/user/google-calendar/status` (hide Connect / "not available here" when false);
  `GET …/auth-url` also answers 503 `errorType: "ServiceNotConfigured"`. F33 (send `state` on connect) is required — the
  validator rejects an empty state.
- Push `dev` when ready.


Done and verified

Kernel port · Core deleted · Kernel trimmed (83→44 files, zero package deps) · user model unified (C) · persistence wired (E: 12 DbSets, 12-table
migration, 3 registrars, Quartz defaults, DbContext alias, SignalR, NotificationService<User>, payload enricher, 7 options sections, 2 dev-seeder
adapters) · package/vuln alignment · xunit v3 · no partitioning conflict.

Solution builds with 0 errors; EF model validates.

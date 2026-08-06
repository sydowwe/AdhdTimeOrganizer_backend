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


Done and verified

Kernel port · Core deleted · Kernel trimmed (83→44 files, zero package deps) · user model unified (C) · persistence wired (E: 12 DbSets, 12-table
migration, 3 registrars, Quartz defaults, DbContext alias, SignalR, NotificationService<User>, payload enricher, 7 options sections, 2 dev-seeder
adapters) · package/vuln alignment · xunit v3 · no partitioning conflict.

Solution builds with 0 errors; EF model validates.

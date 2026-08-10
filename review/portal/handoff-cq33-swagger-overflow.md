# Handoff — CQ-33: Swagger stack overflow, find the culprit validator

## The task

Dev Swagger currently runs with FastEndpoints' own `ValidationSchemaProcessor` **stripped out** in
`AdhdTimeOrganizer/Program.cs` (inside the `isDevelopment` `services.SwaggerDocument(...)` block).
Without that strip, requesting `/swagger/v1/swagger.json` kills the process with a stack overflow.

The working theory to test: **one specific validator (or the DTO it validates) produces a
self-referential schema**, and that single case is what drives the recursion. If you find and fix it,
the strip block can be deleted and dev Swagger regains FluentValidation-derived constraints.

## What is already established — don't redo this

- **It is a real upstream recursion bug, not a misconfiguration.** Reproduced live on 2026-08-10.
  Trace is thousands of identical frames:
  ```
  FastEndpoints.Swagger.ValidationSchemaProcessor.ApplyRulesToSchema(...)
    -> NJsonSchema.JsonSchema.get_ActualProperties()
      -> ApplyRulesToSchema(...)   [repeats until the stack dies]
  ```
- **The cycle guard is ineffective by design.** `ApplyRulesToSchema` takes a `HashSet<Type>`, but it
  guards on *Type* while the recursion walks schema *nodes* (`ActualProperties`). A self-referential
  schema slips past it.
- **Upgrading does not fix it.** `FastEndpoints` + `FastEndpoints.Swagger` were bumped 8.1.0 → 8.2.0,
  the solution built clean, the strip block was removed, and the process died the same way on the same
  request. Both packages were reverted to 8.1.0. Do not spend time re-testing 8.2.0.
- **A `StackOverflowException` cannot be caught.** No try/catch, no `AppDomain` handler. The process
  just dies. Plan any experiment around that.
- `RemoveToEntitySchemaProcessor` (a *separate*, legitimate processor in the same block) already strips
  `ICreateRequest<TEntity>.ToEntity` from schemas, because those pull the raw cyclic EF navigation graph
  in. **That one is load-bearing — leave it.** The overflow persists even with it active, so the
  remaining cycle is somewhere else.

## How to reproduce

```powershell
# 1. In Program.cs, delete the `validationProcessors` strip block (keep RemoveToEntitySchemaProcessor).
# 2. Run:
cd C:\Users\jakub\RiderProjects\AdhdTimeOrganizer\AdhdTimeOrganizer
dotnet run
# App listens on https://localhost:8080 (from launchSettings, not ASPNETCORE_URLS).
# Needs the dev DB at 187.77.77.42:5443 and FIELD_ENCRYPTION_KEY in .env (both already set).
# 3. Request the document — this is what triggers generation:
curl.exe -k "https://localhost:8080/swagger/v1/swagger.json"
```
Startup succeeds; the crash happens only when the document is generated on first request. Redirect
stdout/stderr to files — the trace goes to **stderr**, and it is long.

## Suggested line of attack

The recursion is over schema nodes, so look for a request DTO whose schema can reach itself, plus a
validator that makes `ValidationSchemaProcessor` descend into it. Candidates worth checking first:

1. **Validators using `ChildRules` / `SetValidator` / `RuleForEach` on a type that (transitively)
   contains itself.** `RuleForEach(x => x.Entries).ChildRules(...)` patterns are all over
   `AdhdTimeOrganizer/application/validator/`. A nested DTO that references its own parent type — or a
   tree-shaped DTO (steps containing steps, items containing items) — is the shape to hunt.
2. **To-do list / checklist DTOs.** `BaseTodoListItem` has `Steps`, and the to-do and routine request
   DTOs mirror that structure — a self-referential item/step DTO is plausible there.
3. **Anything still exposing an EF entity type in a request/response DTO.** `RemoveToEntitySchemaProcessor`
   only strips the `ToEntity` member specifically; an entity reachable by another property name would
   still drag the cyclic nav graph in.

A cheap bisect if inspection stalls: `SwaggerDocument` supports filtering endpoints
(`o.EndpointFilter = ep => ...`). Restore the strip block's *absence*, then generate the document for
one endpoint group at a time until the crash localizes. Each iteration costs a process restart, but it
narrows fast and needs no guesswork about which validator is at fault.

## Definition of done

Either:
- **(a)** the offending DTO/validator is identified and reshaped so the schema is acyclic, the strip
  block is deleted, `/swagger/v1/swagger.json` returns 200, and dev Swagger shows validation
  constraints again; or
- **(b)** it is confirmed that the cycle is unavoidable in this domain model, in which case file the
  upstream issue against FastEndpoints.Swagger (the trace and the `HashSet<Type>`-guards-the-wrong-thing
  analysis above are the report) and leave the strip block with a link to the issue.

Update `CQ-33` in `review/portal/02-findings.md` either way.

## State of the tree when this was written

- `Program.cs` — strip block restored, rewritten from "TEMP DIAGNOSTIC" to a documented permanent
  workaround carrying the diagnosis and the 8.2.0 result.
- `AdhdTimeOrganizer.csproj` — FastEndpoints packages reverted to **8.1.0**.
- `dotnet build AdhdTimeOrganizer.sln` passes on this state: 0 errors, 94 warnings (all pre-existing).
- Two migrations are pending from earlier work in the same session and were deliberately left to the
  repo owner: `DesktopActivityEntry.ExecutablePath` `varchar(2048)` → `text` (encryption, SEC-4) and a
  new `activity_history (user_id, start_timestamp)` index (PERF-10). A third,
  `20260810094941_EncryptGoogleCalendarRefreshToken`, is scaffolded but unreviewed.
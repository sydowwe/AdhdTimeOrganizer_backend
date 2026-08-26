# Task: Fix all xUnit1051 warnings in MojaDigitalnaFirma.AdminPortal.Tests

A Roslyn-based tool has already been written for this. Your job is to run it, verify
the result, and fix the handful of cases it leaves for manual handling. **Do not load
the test files into context** — the tool rewrites them; you only inspect diffs/build output.

## Background

xUnit1051: *"Calls to methods which accept CancellationToken should use
`TestContext.Current.CancellationToken`."* There are ~2000 of these in the test project.
The fix is to append `TestContext.Current.CancellationToken` as the **last argument** of
each flagged call (e.g. `PostAsJsonAsync`, `PutAsJsonAsync`, `ReadFromJsonAsync<…>`,
`PostAsync`, `SendAsync`, `SaveChangesAsync`, `ExecuteReaderAsync`, …).

`dotnet format` cannot do this — the xUnit fixer `UseCancellationTokenFixer` refuses
"Fix All in Solution", which is the only mode `dotnet format` uses. So we drive a custom
Roslyn syntax rewriter off the build log instead.

## What exists

- `tools/FixXUnit1051/` — the Roslyn rewriter (a console project). It reads an MSBuild log,
  finds each `xUnit1051` location, resolves it to the **outermost** invocation starting at
  that column (so `CreateClient().PostAsJsonAsync(...)` is fixed on `PostAsJsonAsync`, not
  `CreateClient`), and appends the cancellation-token argument. It preserves each file's
  UTF-8 BOM and is idempotent. It **skips `FindAsync`** (whose overloads make positional
  appending bind to the wrong overload) and prints those for manual fixing.
  - Usage: `dotnet <built dll> <warnings-log-file> [path-filter-substring]`
- `tools/fix-xunit1051.ps1` — wrapper that builds the test project, captures the log, runs
  the rewriter, and rebuilds to verify. Supports `-PathFilter '<substring>'` to go
  folder-by-folder.

## Steps

1. **Reset any stray edits** from earlier experimentation:
   ```powershell
   git checkout -- MojaDigitalnaFirma.AdminPortal.Tests/integration/endpoint/inventory/LocationEndpointTests.cs
   ```
   (Only this file may have been left modified by a prior test run.)

2. **Run the wrapper** from the repo root (`C:\Users\jakub\RiderProjects\MojaDigitalnaFirma`):
   ```powershell
   pwsh tools/fix-xunit1051.ps1
   ```
   It prints the warning count before and after. Expect it to go from ~2000 to near 0.

3. **Handle the `FindAsync` (and any other) manual cases** the tool reports under
   "Needs manual fix". For `DbSet.FindAsync`, wrap the key in an array so the
   `(object?[], CancellationToken)` overload is selected — do NOT just append the token:
   ```csharp
   // before
   await db.Set<Location>().FindAsync(loc.Id);
   // after
   await db.Set<Location>().FindAsync([loc.Id], TestContext.Current.CancellationToken);
   ```

4. **Verify**: rerun the wrapper (or `dotnet build` the test project) and confirm
   `xUnit1051` count is `0` and there are **no compile errors**. The tool only appends an
   argument, so a clean build proves overload resolution still works.

5. **Sanity-check behavior**: run a small, fast slice of the suite (e.g. the inventory
   endpoint tests) to confirm nothing broke at runtime:
   ```powershell
   dotnet test MojaDigitalnaFirma.AdminPortal.Tests/MojaDigitalnaFirma.AdminPortal.Tests.csproj --filter "FullyQualifiedName~inventory"
   ```

6. **Report** the before/after warning counts, the list of manually fixed `FindAsync`
   sites, and the build/test result. Show `git diff --stat` for the summary; don't paste
   full file contents.

## Guardrails

- Everything is tracked in git — if a step looks wrong, `git checkout --` the affected
  files and stop.
- `TestContext` lives in the `Xunit` namespace (xunit v3); the test files already
  `using Xunit;`. If the verify build reports `TestContext` is missing in some file, add
  `using Xunit;` to that file.
- Do not change production code — only `MojaDigitalnaFirma.AdminPortal.Tests`.
<#
.SYNOPSIS
    Fixes xUnit1051 warnings in MojaDigitalnaFirma.AdminPortal.Tests by appending
    TestContext.Current.CancellationToken to every flagged call.

.DESCRIPTION
    The xUnit code fixer can't run under `dotnet format` (it refuses "Fix All in
    Solution"), so this drives a small Roslyn rewriter (tools/FixXUnit1051) off the
    build log instead. Pipeline:
      1. Build the test project, capturing xUnit1051 warning locations.
      2. Run the rewriter, which appends TestContext.Current.CancellationToken as the
         last argument of each flagged (outermost) invocation.
      3. Rebuild to confirm the warnings are gone.

.PARAMETER PathFilter
    Case-insensitive path substring; only files whose full path contains it are
    rewritten. Use it to go folder-by-folder, e.g. -PathFilter 'integration\endpoint\inventory'.

.EXAMPLE
    pwsh tools/fix-xunit1051.ps1
    pwsh tools/fix-xunit1051.ps1 -PathFilter 'integration\endpoint\inventory'
#>
[CmdletBinding()]
param(
    [string]$PathFilter
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repoRoot "MojaDigitalnaFirma.AdminPortal.Tests\MojaDigitalnaFirma.AdminPortal.Tests.csproj"
$tool = Join-Path $PSScriptRoot "FixXUnit1051\FixXUnit1051.csproj"
$log = Join-Path $repoRoot "xunit1051_build.log"

function Build-And-CaptureWarnings {
    Write-Host "Building test project to collect xUnit1051 warnings..." -ForegroundColor Cyan
    dotnet build $testProject --no-incremental -clp:NoSummary | Out-File -FilePath $log -Encoding utf8
    $count = (Select-String -Path $log -Pattern 'xUnit1051').Count
    $color = 'Green'; if ($count) { $color = 'Yellow' }
    Write-Host "  xUnit1051 warnings: $count" -ForegroundColor $color
    return $count
}

$before = Build-And-CaptureWarnings
if ($before -eq 0) {
    Write-Host "Nothing to fix." -ForegroundColor Green
    exit 0
}

Write-Host "Applying fixes via Roslyn rewriter..." -ForegroundColor Cyan
$toolArgs = @("run", "--project", $tool, "-c", "Release", "--", $log)
if ($PathFilter) { $toolArgs += $PathFilter }
dotnet @toolArgs
if ($LASTEXITCODE -ne 0) { throw "Rewriter exited with code $LASTEXITCODE" }

$after = Build-And-CaptureWarnings
Write-Host ""
Write-Host "xUnit1051 warnings: $before -> $after" -ForegroundColor Cyan
if ($after -gt 0 -and -not $PathFilter) {
    Write-Host "Some warnings remain. Re-run to fix any that shifted, or inspect the log: $log" -ForegroundColor Yellow
}
Write-Host "Review changes with 'git diff'." -ForegroundColor Green

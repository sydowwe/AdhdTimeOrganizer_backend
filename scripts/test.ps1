<#
.SYNOPSIS
    Builds and runs tests without ever touching the IDE's bin/ — so the app can stay running under
    the Rider debugger while this runs.

.DESCRIPTION
    Everything goes through `--artifacts-path artifacts-cli`, which relocates bin/ AND obj/ for every
    project in the graph. The debugged host process holds AdhdTimeOrganizer/bin/Debug/net10.0/*.dll
    open; a normal `dotnet build` of the test project rebuilds the host into that same folder and
    dies with MSB3021 "being used by another process". A separate output tree makes the two builds
    invisible to each other. Cost: the first build in the tree is a full one, and Rider's Build and
    this script each compile their own copy.

    The runner is the xunit v3 executable, not `dotnet test`: on the .NET 10 SDK `dotnet test` still
    drives VSTest, which Microsoft.Testing.Platform refuses ("Testing with VSTest target is no longer
    supported"). Filter syntax is xunit's own — see `<exe> --help`.

    Integration tests start their own Postgres through Testcontainers, so Docker must be up, and they
    never touch the dev database the debugged app is using.

.PARAMETER Class
    Wildcarded class filter, e.g. '*CoreForeignKeyOwnershipTests'. OR'd with -Method.
    Several at once are comma-separated ('a','b'); repeating the switch is a binding error.

.PARAMETER Method
    Wildcarded method filter, fully qualified, e.g. '*.CreateActivity_*'. OR'd with -Class.
    Comma-separated for several, as with -Class.

.PARAMETER Trait
    'name=value' trait filter, e.g. 'Status=KnownGap'. Comma-separated for several.

.PARAMETER Project
    Test project directory name. Defaults to the integration tests, the only test project here.

.PARAMETER NoBuild
    Skip the build and run whatever is already in artifacts-cli.

.PARAMETER BuildOnly
    Build and stop — a compile check that cannot collide with the debugger.

.EXAMPLE
    ./scripts/test.ps1 -Class '*CoreForeignKeyOwnershipTests'

.EXAMPLE
    ./scripts/test.ps1 -Class '*Activity*','*TimerPreset*'

.EXAMPLE
    ./scripts/test.ps1            # everything
#>
[CmdletBinding()]
param(
    [string[]]$Class,
    [string[]]$Method,
    [string[]]$Trait,
    [string]$Project = 'AdhdTimeOrganizer.IntegrationTests',
    [switch]$NoBuild,
    [switch]$BuildOnly,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Rest
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $repoRoot 'artifacts-cli'
$projectFile = Join-Path $repoRoot "$Project\$Project.csproj"

if (-not (Test-Path $projectFile)) {
    throw "No such project: $projectFile"
}

if (-not $NoBuild) {
    Write-Host "Building $Project into artifacts-cli..." -ForegroundColor Cyan
    dotnet build $projectFile --artifacts-path $artifacts --nologo -v q
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if ($BuildOnly) { exit 0 }

# The artifacts layout is <artifacts>/bin/<project>/<configuration lowercased>/.
$runner = Join-Path $artifacts "bin\$Project\debug\$Project.exe"
if (-not (Test-Path $runner)) {
    throw "Runner not found at $runner — run once without -NoBuild."
}

$runnerArgs = @()
foreach ($value in $Class) { $runnerArgs += @('-class', $value) }
foreach ($value in $Method) { $runnerArgs += @('-method', $value) }
foreach ($value in $Trait) { $runnerArgs += @('-trait', $value) }
if ($Rest) { $runnerArgs += $Rest }

& $runner @runnerArgs
exit $LASTEXITCODE

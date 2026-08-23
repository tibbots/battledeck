<#
.SYNOPSIS
    Sets up a throwaway data folder with demo accounts and starts Smurftown against it.

.DESCRIPTION
    Testing this app means clicking through it, and BattlenetAccountGateway rewrites the
    whole data.yaml on every mutation - ticking a region, renaming an account, opening a
    dialog and confirming it. Against the real folder every test run is an edit to the
    real account list, and "put the values back afterwards" is a step that works until the
    run that strands halfway.

    So the app gets a different folder. SMURFTOWN_HOME does that (Smurftown/Directories.cs),
    and this script fills it with the ten invented accounts from tools/demo-data.yaml -
    all addresses under example.com, all passwords obvious placeholders.

    THE VARIABLE STAYS SET IN THE CALLING SHELL. $env: is the process environment, and a
    script shares the process of the session that called it. That is deliberate: the other
    scripts under tools/ resolve the folder through smurftown-home.ps1 and then look into
    the same one - drive-hots for the game path, capture-run for its safeguard.

.PARAMETER Path
    The folder. Default %TEMP%\smurftown-test-home. Refuses to be the real folder.

.PARAMETER Fresh
    Delete the folder first. Without it an existing one is kept and only a missing
    data.yaml is filled in - a second run does not throw away what a test just produced.

.PARAMETER NoStart
    Only set up and set the variable, start nothing.

.PARAMETER Exe
    Which build to start. Default: the newest Smurftown.exe of the known output folders.

.PARAMETER Force
    Start even though another Smurftown is already running. See the check below.

.EXAMPLE
    .\tools\test-home.ps1
    .\tools\test-home.ps1 -Fresh
    .\tools\test-home.ps1 -NoStart -Path D:\tmp\st
#>
[CmdletBinding()]
param(
    [string] $Path,
    [switch] $Fresh,
    [switch] $NoStart,
    [string] $Exe,
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$real = Join-Path $env:USERPROFILE '.smurftown'

if (-not $Path) { $Path = Join-Path $env:TEMP 'smurftown-test-home' }
$Path = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($Path.Trim()))

# THE ONE CHECK THAT MAY NOT BE SKIPPED. Everything below this line deletes files and
# writes demo accounts over them; pointed at the real folder it would do exactly what the
# script exists to prevent.
if ($Path -eq [IO.Path]::GetFullPath($real)) {
    throw "REFUSED: $Path is the real data folder. Pick another one."
}

# ---- the folder --------------------------------------------------------------------------

if ($Fresh -and (Test-Path $Path)) {
    Remove-Item -LiteralPath $Path -Recurse -Force
    Write-Host "removed  $Path" -ForegroundColor DarkGray
}
if (-not (Test-Path $Path)) { [void](New-Item -ItemType Directory -Path $Path -Force) }

$demo = Join-Path $PSScriptRoot 'demo-data.yaml'
if (-not (Test-Path $demo)) { throw "Not found: $demo" }

$data = Join-Path $Path 'data.yaml'
if (Test-Path $data) {
    Write-Host "kept     $data" -ForegroundColor DarkGray
} else {
    Copy-Item -LiteralPath $demo -Destination $data
    Write-Host "demo     $data" -ForegroundColor Green
}

# app.yaml carries the game path, the input pace, the two languages, a hand-set rotation and
# what the update check noted - no credentials, those are all in data.yaml. Copied over
# because without hotsPath drive-hots cannot start the game, and typing that path in by hand
# once per test folder is the kind of step that gets skipped.
#
# settings.yaml is the name it had before 1.3.0; a real folder the new app has not started
# against yet still holds that one, and copying it lets the test folder migrate it itself.
$appFile = Join-Path $Path 'app.yaml'
$legacy = Join-Path $Path 'settings.yaml'
if (-not (Test-Path $appFile) -and -not (Test-Path $legacy)) {
    foreach ($name in 'app.yaml', 'settings.yaml') {
        $source = Join-Path $real $name
        if (-not (Test-Path $source)) { continue }

        Copy-Item -LiteralPath $source -Destination (Join-Path $Path $name)
        Write-Host "config   $(Join-Path $Path $name)  (copied from the real folder - game path, languages)" -ForegroundColor DarkGray
        break
    }
}

# ---- the variable ------------------------------------------------------------------------

$env:SMURFTOWN_HOME = $Path
Write-Host ''
Write-Host "SMURFTOWN_HOME = $Path" -ForegroundColor Cyan
Write-Host '  set for THIS shell. A Smurftown started from another one still uses the real folder.' -ForegroundColor DarkGray

if ($NoStart) { return }

# ---- the application ---------------------------------------------------------------------

if (-not $Exe) {
    $candidates = @(
        (Join-Path $repo 'Smurftown\bin\Debug\net8.0-windows10.0.19041.0\Smurftown.exe'),
        (Join-Path $repo 'Smurftown\bin\Release\net8.0-windows10.0.19041.0\Smurftown.exe'),
        (Join-Path $repo 'dist\publish\Smurftown.exe')
    ) | Where-Object { Test-Path $_ }
    if (-not $candidates) { throw 'No Smurftown.exe found. Build first: ./dev build' }
    $Exe = @($candidates | Sort-Object { (Get-Item $_).LastWriteTime } -Descending)[0]
}

# WHY THIS ABORTS INSTEAD OF WARNING: drive-smurftown.ps1 and capture-window.ps1 pick the
# FIRST process named Smurftown that has a window. With two of them running, which window
# gets clicked is luck - and one of the two is showing the real list.
$running = @(Get-Process -Name 'Smurftown' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0 -and -not $Force) {
    # The parentheses around the concatenation are load-bearing: -f binds tighter than +,
    # so without them only the LAST fragment gets formatted - and that one carries no
    # placeholder. The message then names neither the count nor the PID it is telling you
    # to close, which is the one thing it exists to say.
    throw (("ABORTED: {0} Smurftown already running (PID {1}). The drive scripts take the " +
            "first window they find and would then be clicking a coin flip. Close it, or " +
            "-Force if you know which one you mean.") -f $running.Count, ($running.Id -join ', '))
}

$proc = Start-Process -FilePath $Exe -PassThru
Write-Host ''
Write-Host ("started  {0}" -f $Exe) -ForegroundColor Green
Write-Host ("  PID {0} - close it again with:  Stop-Process -Id {0}" -f $proc.Id) -ForegroundColor DarkGray

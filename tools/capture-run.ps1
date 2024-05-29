<#
.SYNOPSIS
    Walks through all captures for the README - announcement, countdown, shot.

.DESCRIPTION
    Calls tools/capture-window.ps1 in sequence and announces before each shot how the
    UI needs to be set up for it. The capture happens at the end of the countdown, so
    there is no going back - a botched shot is redone with -Only.

    PREREQUISITE: the data folder holds demo data, not real data. The repo is public,
    and the row shows the battletag, the dialog the email address. The script checks
    this and aborts otherwise.

    The way to satisfy that is tools/test-home.ps1: it puts the invented accounts into a
    throwaway folder and starts the app against it through SMURFTOWN_HOME, so the real
    list is not moved aside but simply never opened. This script then checks that same
    folder - both resolve it through smurftown-home.ps1.

.PARAMETER Only
    Only capture these shots, comma-separated, e.g. -Only start-menu,edit-hots

.PARAMETER Delay
    Seconds of lead time per shot. Default 15.

.EXAMPLE
    .\tools\capture-run.ps1
    .\tools\capture-run.ps1 -Only rotation,archive -Delay 20
#>
[CmdletBinding()]
param(
    [string[]] $Only,
    [ValidateRange(3, 60)] [int] $Delay = 15
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Order is intentional: first everything that only switches the filter, then the popups,
# then the modals. That way the dialog only needs to be opened once.
$SHOTS = @(
    @{ name = 'overview'; title = 'The list'
       steps = @('ACCOUNTS tab',
                 'Game filter to HEROES OF THE STORM, region filter to EU',
                 'Search field empty, hero filter empty, archive OFF',
                 'scroll all the way to the top') },

    @{ name = 'filter-game'; title = 'The game filter is a view selector'
       steps = @('Set game filter to OVERWATCH',
                 'shows different rows, dashed boxes - and the HotS extra filters are gone') },

    @{ name = 'filter-region'; title = 'Region rows'
       steps = @('Game filter back to HEROES OF THE STORM',
                 'Region filter to AM',
                 '5 rows, among them HALFMOONBAY with nothing but dashes') },

    @{ name = 'hero-filter'; title = 'Hero filter - the selection'
       steps = @('Region filter back to EU',
                 'Open the hero filter (button in the filter bar)',
                 'Click ILLIDAN and LUCIO - neither is in the free rotation right now',
                 'Leave the window open, cursor out of the shot') },

    @{ name = 'hero-filter-result'; title = 'Hero filter - the result'
       steps = @('Close the hero picker (Esc)',
                 'the list now shows 7 of 25 rows, the button says ANY OF 2') },

    @{ name = 'rotation'; title = 'Free rotation'
       steps = @('Clear the hero filter (clear cross on the button)',
                 'click the FREE symbol',
                 'the 14 free heroes of this period, with the Nexus badge',
                 'leave it open') },

    @{ name = 'archive'; title = 'Archive'
       steps = @('Close the rotation window (Esc)',
                 'Archive toggle on - the other half of the list, GLASSFERN') },

    @{ name = 'start-menu'; title = 'Starting and reading'
       steps = @('Archive off again',
                 'DURING THE COUNTDOWN: click the round blue start button on a row',
                 'CAUTION - the menu closes on any click elsewhere. So open it last',
                 'and do not touch anything after that') },

    @{ name = 'edit-account'; title = 'Account dialog, ACCOUNT tab'
       steps = @('Three-dot menu on a row -> Edit',
                 'ACCOUNT tab',
                 'Use MARBLEFOX: two regions, penalty games, placement pending') },

    @{ name = 'edit-hots'; title = 'Account dialog, HOTS tab'
       steps = @('In the same dialog, go to the HOTS tab',
                 'the region bar is there because MARBLEFOX has two regions',
                 'rank grid on top, hero grid below - scroll all the way to the top') },

    @{ name = 'settings'; title = 'Settings'
       steps = @('Close the dialog (Cancel)',
                 'SETTINGS tab') }
)

# ---- Safeguard: no real data in the shot ----------------------------------------------------
# Checks the folder the APP is running against, not %USERPROFILE% blindly: with
# SMURFTOWN_HOME set those are two different places, and a green light for a file nobody
# is photographing is worse than no check at all.
$stHome = & (Join-Path $PSScriptRoot 'smurftown-home.ps1')
$dataFile = Join-Path $stHome 'data.yaml'
if (-not (Test-Path $dataFile)) { throw "Not found: $dataFile" }

$mails = Select-String -Path $dataFile -Pattern '^\s*email:\s*(\S+)' -AllMatches |
         ForEach-Object { $_.Matches[0].Groups[1].Value }
$real = @($mails | Where-Object { $_ -notlike '*@example.com' })
if ($real.Count -gt 0) {
    throw ("ABORTED: {0} holds {1} real email addresses. The repo is public - start the " +
           "app against a test folder first: .\tools\test-home.ps1" -f $dataFile, $real.Count)
}
Write-Host ("data.yaml checked: {0} addresses, all @example.com" -f $mails.Count) -ForegroundColor DarkGray

$queue = if ($Only) { $SHOTS | Where-Object { $Only -contains $_.name } } else { $SHOTS }
if (-not $queue) { throw "No shot matches -Only $($Only -join ',')" }

$capture = Join-Path $PSScriptRoot 'capture-window.ps1'
$n = 0
$done = @()

foreach ($shot in $queue) {
    $n++
    Write-Host ''
    Write-Host ('=' * 78) -ForegroundColor DarkGray
    Write-Host ("  SHOT {0}/{1}   {2}   ->  {3}.png" -f $n, @($queue).Count, $shot.title, $shot.name) -ForegroundColor Yellow
    Write-Host ('=' * 78) -ForegroundColor DarkGray
    foreach ($s in $shot.steps) { Write-Host "   - $s" }

    & $capture -Name $shot.name -Delay $Delay
    $done += $shot.name
}

Write-Host ''
Write-Host ("Done: {0} shots in docs/images/" -f $done.Count) -ForegroundColor Green
Write-Host ('  ' + ($done -join ', ')) -ForegroundColor DarkGray
Write-Host ''
if ($stHome -eq (Join-Path $env:USERPROFILE '.smurftown')) {
    Write-Host 'Do not forget: bring back your own list -' -ForegroundColor Yellow
    Write-Host ('  {0}\data.yaml.real  ->  data.yaml' -f $stHome) -ForegroundColor Yellow
} else {
    Write-Host ("Shot against $stHome - the real list was never opened.") -ForegroundColor DarkGray
}

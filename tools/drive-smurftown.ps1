<#
.SYNOPSIS
    Drives the RUNNING Smurftown window - clicks, keys, captures.

.DESCRIPTION
    For the README images: the UI has to be in a certain state for each shot, and
    clicking that by hand is the same eleven-step routine eleven times, with eleven
    chances to forget one.

    BOUNDARY THIS SCRIPT DOES NOT CROSS: it does not start Smurftown. That stays with
    the user (CLAUDE.md, "Branch Strategy & Lifecycle") - a second instance would
    collide with his. If none is running, the script aborts instead of starting one.

    All coordinates are WINDOW-RELATIVE, i.e. exactly as they can be read off a
    capture from capture-window.ps1: the main window is borderless
    (WindowStyle="None"), so its window rectangle is its client area, and 1340x800 in
    the image is 1340x800 on the window.

.PARAMETER Do
    Sequence of steps, separated by semicolons. Commands:

      front              Bring the window to the front. Do NOT use this between
                         opening and clicking - SetForegroundWindow closes any open
                         popup.
      click:X,Y          Left click on the point
      right:X,Y          Right click
      move:X,Y           just move the cursor - before every capture, otherwise a row
                         shows up in the image in its hover state
      wheel:X,Y,N        Mouse wheel at this point, N notches (negative = downward)
      key:NAME           Escape, Enter, Tab, Home, End
      type:TEXT          type text
      wait:MS            wait
      shot:NAME          capture to docs/images/NAME.png

.PARAMETER Lang
    Which README the shots in this call are for: 'de', 'fr' or 'es'. Every 'shot:' step
    lands under docs/images/<Lang>/ instead of docs/images/. Omit it for English. This
    script does not switch Smurftown's own UI language - set that before starting it.

.EXAMPLE
    .\tools\drive-smurftown.ps1 -Do 'front; click:139,140; wait:600; move:660,770; shot:filter-game'
    .\tools\drive-smurftown.ps1 -Lang de -Do 'front; move:20,20; shot:overview'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Do,
    [ValidateSet('de', 'fr', 'es')] [string] $Lang
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not ('Ui' -as [type])) {
    Add-Type -Namespace '' -Name Ui -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
// The delta is SIGNED - a uint signature cannot express scrolling downward, and two
// overloads side by side would make resolution ambiguous.
[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, int d, IntPtr i);
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern short VkKeyScan(char c);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint f, IntPtr extra);
public struct RECT { public int Left, Top, Right, Bottom; }
'@
}
[void][Ui]::SetProcessDPIAware()

$LEFTDOWN = 0x0002; $LEFTUP = 0x0004; $RIGHTDOWN = 0x0008; $RIGHTUP = 0x0010
$WHEEL = 0x0800
$KEYUP = 0x0002
$VK = @{ escape = 0x1B; enter = 0x0D; tab = 0x09; home = 0x24; end = 0x23 }

$proc = Get-Process -Name 'Smurftown' -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) { throw 'Smurftown is not running. Starting it is the user''s job - see CLAUDE.md.' }
$hwnd = $proc.MainWindowHandle

function Get-Origin {
    $r = New-Object Ui+RECT
    if (-not [Ui]::GetWindowRect($hwnd, [ref] $r)) { throw 'Window is gone.' }
    if (($r.Right - $r.Left) -lt 200) { throw 'Window minimized - restore it first.' }
    @{ X = $r.Left; Y = $r.Top; W = $r.Right - $r.Left; H = $r.Bottom - $r.Top }
}

function Move-To([int] $x, [int] $y) {
    $o = Get-Origin
    [void][Ui]::SetCursorPos($o.X + $x, $o.Y + $y)
    Start-Sleep -Milliseconds 60
}

function Invoke-Click([int] $x, [int] $y, [switch] $Right) {
    Move-To $x $y
    if ($Right) { [Ui]::mouse_event($RIGHTDOWN,0,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 40
                  [Ui]::mouse_event($RIGHTUP,0,0,0,[IntPtr]::Zero) }
    else        { [Ui]::mouse_event($LEFTDOWN,0,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 40
                  [Ui]::mouse_event($LEFTUP,0,0,0,[IntPtr]::Zero) }
    Start-Sleep -Milliseconds 220
}

# One notch is 120 units - the same number WPF knows as
# Mouse.MouseWheelDeltaForOneLine. Positive scrolls up, negative down.
function Invoke-Wheel([int] $x, [int] $y, [int] $notches) {
    Move-To $x $y
    for ($i = 0; $i -lt [math]::Abs($notches); $i++) {
        [Ui]::mouse_event($WHEEL, 0, 0, (120 * [math]::Sign($notches)), [IntPtr]::Zero)
        Start-Sleep -Milliseconds 90
    }
    Start-Sleep -Milliseconds 250
}

function Send-Key([string] $name) {
    $key = $VK[$name.ToLower()]
    if (-not $key) { throw "Unknown key: $name" }
    [Ui]::keybd_event([byte]$key, 0, 0, [IntPtr]::Zero)
    [Ui]::keybd_event([byte]$key, 0, $KEYUP, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 180
}

function Send-Text([string] $text) {
    foreach ($ch in $text.ToCharArray()) {
        $vks = [Ui]::VkKeyScan($ch)
        $vk = $vks -band 0xFF
        $shift = ($vks -shr 8) -band 1
        if ($shift) { [Ui]::keybd_event(0x10, 0, 0, [IntPtr]::Zero) }
        [Ui]::keybd_event([byte]$vk, 0, 0, [IntPtr]::Zero)
        [Ui]::keybd_event([byte]$vk, 0, $KEYUP, [IntPtr]::Zero)
        if ($shift) { [Ui]::keybd_event(0x10, 0, $KEYUP, [IntPtr]::Zero) }
        Start-Sleep -Milliseconds 35
    }
    Start-Sleep -Milliseconds 200
}

function Save-Shot([string] $name) {
    $o = Get-Origin
    $bmp = New-Object System.Drawing.Bitmap $o.W, $o.H
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try { $g.CopyFromScreen($o.X, $o.Y, 0, 0, $bmp.Size) } finally { $g.Dispose() }

    $dir = Join-Path (Split-Path $PSScriptRoot -Parent) 'docs/images'
    if ($Lang) { $dir = Join-Path $dir $Lang }
    if (-not (Test-Path $dir)) { [void](New-Item -ItemType Directory -Path $dir) }
    $file = Join-Path $dir "$name.png"
    try { $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png) } finally { $bmp.Dispose() }
    Write-Host ("  shot  {0}.png  {1}x{2}  {3} KB" -f
        $name, $o.W, $o.H, [math]::Round((Get-Item $file).Length / 1KB)) -ForegroundColor Green
}

foreach ($raw in ($Do -split ';')) {
    $step = $raw.Trim()
    if (-not $step) { continue }
    $verb, $arg = $step -split ':', 2

    switch ($verb.Trim().ToLower()) {
        'front' { [void][Ui]::SetForegroundWindow($hwnd); Start-Sleep -Milliseconds 350
                  Write-Host '  front' -ForegroundColor DarkGray }
        'click' { $p = $arg -split ','; Invoke-Click ([int]$p[0]) ([int]$p[1])
                  Write-Host "  click $arg" -ForegroundColor DarkGray }
        'right' { $p = $arg -split ','; Invoke-Click ([int]$p[0]) ([int]$p[1]) -Right
                  Write-Host "  right $arg" -ForegroundColor DarkGray }
        'move'  { $p = $arg -split ','; Move-To ([int]$p[0]) ([int]$p[1])
                  Write-Host "  move  $arg" -ForegroundColor DarkGray }
        'wheel' { $p = $arg -split ','; Invoke-Wheel ([int]$p[0]) ([int]$p[1]) ([int]$p[2])
                  Write-Host "  wheel $arg" -ForegroundColor DarkGray }
        'key'   { Send-Key $arg;  Write-Host "  key   $arg" -ForegroundColor DarkGray }
        'type'  { Send-Text $arg; Write-Host "  type  $arg" -ForegroundColor DarkGray }
        'wait'  { Start-Sleep -Milliseconds ([int]$arg); Write-Host "  wait  $arg" -ForegroundColor DarkGray }
        'shot'  { Save-Shot $arg }
        default { throw "Unknown step: $step" }
    }
}

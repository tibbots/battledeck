<#
.SYNOPSIS
    Captures the Smurftown window and saves the image under docs/images/<Name>.png.

.DESCRIPTION
    For the README screenshots. Captured via BitBlt from the SCREEN context and not
    via PrintWindow - that is the right choice here, not a convenience shortcut:

      * WPF popups (a row's start menu and action menu, expanded selection lists) are
        THEIR OWN windows at the operating-system level. PrintWindow only draws the
        requested window and would leave out every popup - exactly what three of the
        shots are about.
      * The captured area is the MAIN WINDOW's, not the active one's. A modal thus
        appears in the shot in front of the dimmed list behind it, the way it is
        actually used. Whoever wants only the active window uses -Foreground.

    The price of this choice: whatever is on top of the window at the moment of capture
    ends up in the shot too. That is what the countdown is for - not just for clicking,
    but for clearing the desktop.

.PARAMETER Name
    File name without extension, e.g. "overview". Ends up under docs/images/<Name>.png.

.PARAMETER Delay
    Seconds until the capture. Time to bring the UI into the desired state and move
    the cursor out of the shot. Default 5.

.PARAMETER Foreground
    Capture the currently active window instead of the main window.

.EXAMPLE
    .\tools\capture-window.ps1 overview
    .\tools\capture-window.ps1 start-menu -Delay 8
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidatePattern('^[a-z0-9][a-z0-9-]*$')]
    [string] $Name,

    [ValidateRange(0, 60)]
    [int] $Delay = 5,

    [switch] $Foreground
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

# GetWindowRect returns physical pixels. Without DPI awareness, Windows rescales the
# values for this process to the virtual resolution, and at a scaling factor other than
# 100% the image would end up smaller and blurry by exactly that factor.
if (-not ('Native' -as [type])) {
    Add-Type -Namespace '' -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
public struct RECT { public int Left, Top, Right, Bottom; }
'@
}

[void][Native]::SetProcessDPIAware()

# ---- Determine window -------------------------------------------------------------------
if ($Foreground) {
    $handle = [Native]::GetForegroundWindow()
    $what = 'active window'
}
else {
    $proc = Get-Process -Name 'Smurftown' -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne 0 } |
            Select-Object -First 1
    if (-not $proc) {
        throw 'Smurftown is not running (or has no window yet). Start it, then call again.'
    }
    $handle = $proc.MainWindowHandle
    $what = 'Smurftown main window'
}

# ---- Countdown ---------------------------------------------------------------------------
Write-Host ''
Write-Host "Capture: $Name  ($what)" -ForegroundColor Cyan
Write-Host 'Bring the UI into the desired state now, move the cursor out of the shot.'
for ($i = $Delay; $i -gt 0; $i--) {
    Write-Host -NoNewline ("`r  {0,2} ..." -f $i)
    Start-Sleep -Seconds 1
}
Write-Host "`r        "

# ---- Measure the area --------------------------------------------------------------------
$rect = New-Object Native+RECT
if (-not [Native]::GetWindowRect($handle, [ref] $rect)) {
    throw 'GetWindowRect failed - the window no longer exists.'
}

$width  = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top

# A minimized window reports the placeholder size 160x28 at -32000,-32000 and is thus
# not capturable - the same pitfall as with the game window, see docs/driving-the-game.md.
if ($width -lt 200 -or $height -lt 200) {
    throw "Window measures ${width}x${height} - probably minimized. Restore it first."
}

# ---- Capture -------------------------------------------------------------------------------
$bitmap   = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try {
    $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
}
finally {
    $graphics.Dispose()
}

$target = Join-Path (Split-Path $PSScriptRoot -Parent) 'docs/images'
if (-not (Test-Path $target)) { [void](New-Item -ItemType Directory -Path $target) }

$file = Join-Path $target "$Name.png"
try {
    $bitmap.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $bitmap.Dispose()
}

$kb = [math]::Round((Get-Item $file).Length / 1KB)
Write-Host ("  {0}  {1}x{2}  {3} KB" -f $file, $width, $height, $kb) -ForegroundColor Green

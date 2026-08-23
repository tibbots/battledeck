<#
.SYNOPSIS
    Starts and drives Heroes of the Storm - clicks, keys, captures.

.DESCRIPTION
    Counterpart to drive-smurftown.ps1, but for the GAME. The difference is the
    approval: the user starts Smurftown (a second instance would collide with his),
    the AI is allowed to start Heroes of the Storm itself - CLAUDE.md, "Branch
    Strategy & Lifecycle", named exception from 21.08.2026. The reason is that every
    calibration here is measured against the running game, and a second-hand
    screenshot is only half the measurement.

    COORDINATES ARE CLIENT-RELATIVE, not window-relative. In windowed mode there are
    8 points horizontally and 31 vertically between the window frame and the client
    area - exactly the amount by which clicks would otherwise miss
    (docs/driving-the-game.md). A capture from 'shot' therefore shows the client
    area, and whatever sits at (x,y) in it is what 'click:x,y' hits.

    THREE PITFALLS that are in the code and must not be optimized away:

      1. The game process is called HeroesOfTheStorm_x64, not HeroesSwitcher_x64.exe
         like the one that gets started. Whoever waits for the start process is
         waiting for the wrong one.
      2. The first window is a loading screen of roughly 400x180. A minimum size of
         1000x600 separates it from the game.
      3. 'front' between opening and choosing closes any open list -
         SetForegroundWindow does that. Whoever needs to bring it to the front does
         so BEFORE the first click of the sequence.

.PARAMETER Do
    Sequence of steps, separated by semicolons. Commands:

      start              Start the game (path from settings.yaml in the data folder) and
                         wait for a usable window. If one is already running, nothing
                         happens.
      wait-window:SEC    wait for a window >= 1000x600, at most SEC seconds
      info               print the client area - size and position
      front              bring the window to the front
      click:X,Y          left click, client-relative
      right:X,Y          right click
      move:X,Y           just move the cursor
      wheel:X,Y,N        mouse wheel, N notches (negative = downward)
      key:NAME           escape, enter, tab, space, up, down, left, right
      type:TEXT          type text (Unicode)
      clear              End, then backspace until the field is empty
      user:BATTLETAG     type that account's e-mail, read from ~/.smurftown/data.yaml
      pw:BATTLETAG       type that account's password, read from the same file.
                         Deliberately NOT a parameter: a password passed on a command
                         line lands in the shell history, the process tree and every
                         log that records it.
      wait:MS            wait
      shot:NAME          capture the client area to <OutDir>/NAME.png
      crop:NAME,X,Y,W,H  capture only this area - small crops read better
      quit               quit the game (CloseMainWindow, then Kill)

.PARAMETER OutDir
    Where the captures go.

.EXAMPLE
    .\tools\drive-hots.ps1 -Do 'start; wait-window:180; info; shot:login'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Do,
    [string] $OutDir = "$env:LOCALAPPDATA\Temp\hots-shots"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# The data folder of the application - the real one, or the test folder SMURFTOWN_HOME
# points at. Resolved exactly once and exactly like Smurftown/Directories.cs does it; the
# reasoning is in smurftown-home.ps1.
$SmurftownHome = & (Join-Path $PSScriptRoot 'smurftown-home.ps1')

if (-not ('Ui' -as [type])) {
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class Ui {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, ref RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, int d, IntPtr e);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint f, IntPtr extra);
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll")] public static extern short VkKeyScan(char ch);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint count, INPUT[] inputs, int size);

    public struct RECT { public int Left, Top, Right, Bottom; }
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT {
        public ushort wVk; public ushort wScan; public uint dwFlags;
        public uint time; public IntPtr dwExtraInfo;
    }
    // The largest variant of the union. It is never used and still has to be here:
    // without it, Marshal.SizeOf(INPUT) reports only 32 instead of 40 bytes on x64, and
    // SendInput rejects every call with the wrong struct size - RETURN VALUE 0, no
    // error, no indication. This is exactly what the first attempt on 22.08.2026
    // failed on.
    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT {
        public int dx; public int dy; public uint mouseData; public uint dwFlags;
        public uint time; public IntPtr dwExtraInfo;
    }
    [StructLayout(LayoutKind.Explicit)]
    public struct INPUT {
        [FieldOffset(0)] public uint type;
        // On x64 the union starts after type plus padding to 8 bytes.
        [FieldOffset(8)] public KEYBDINPUT ki;
        [FieldOffset(8)] public MOUSEINPUT mi;
    }

    // A character as a UNICODE event, not as a key code.
    //
    // WHY: VkKeyScan returns a key code plus modifiers for the CURRENT keyboard layout,
    // and the modifiers sit in the high byte - bit 0 Shift, bit 1 Ctrl, bit 2 Alt.
    // Whoever only evaluates Shift ends up typing a 'q' for '@' on a German keyboard:
    // that character sits on AltGr+Q there, i.e. Ctrl+Alt. This is exactly what
    // happened on 22.08.2026 - the login went out as "name qdomain.com" and failed
    // silently.
    //
    // With KEYEVENTF_UNICODE the whole layout question disappears: the character goes
    // out as it is. The application's own InputSender does the same, and for the same
    // reason.
    public static uint TypeChar(char ch) {
        INPUT[] two = new INPUT[2];
        two[0].type = 1; two[0].ki.wScan = ch; two[0].ki.dwFlags = 4;          // UNICODE
        two[1].type = 1; two[1].ki.wScan = ch; two[1].ki.dwFlags = 4 | 2;      // UNICODE|KEYUP
        return SendInput(2, two, Marshal.SizeOf(typeof(INPUT)));
    }
}
'@
}
[void][Ui]::SetProcessDPIAware()

$LEFTDOWN = 0x0002; $LEFTUP = 0x0004; $RIGHTDOWN = 0x0008; $RIGHTUP = 0x0010
$WHEEL = 0x0800
$KEYUP = 0x0002; $SCANCODE = 0x0008
$SW_RESTORE = 9
$VK = @{ escape = 0x1B; enter = 0x0D; tab = 0x09; space = 0x20; home = 0x24; end = 0x23
         up = 0x26; down = 0x28; left = 0x25; right = 0x27; backspace = 0x08 }

if (-not (Test-Path $OutDir)) { [void](New-Item -ItemType Directory -Path $OutDir -Force) }

# The game process - NOT the switcher, which quits after the edition has been chosen.
function Get-Game {
    Get-Process -Name 'HeroesOfTheStorm_x64' -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
}

# Client area in SCREEN coordinates. GetClientRect returns 0,0,W,H - the origin comes
# from ClientToScreen. In borderless fullscreen that is the same as the window
# rectangle; in windowed mode there are 8/31 in between.
function Get-Client {
    $p = Get-Game
    if (-not $p) { throw 'Heroes of the Storm is not running.' }
    $h = $p.MainWindowHandle
    if ([Ui]::IsIconic($h)) { [void][Ui]::ShowWindow($h, $SW_RESTORE); Start-Sleep -Milliseconds 700 }

    $r = New-Object Ui+RECT
    if (-not [Ui]::GetClientRect($h, [ref] $r)) { throw 'GetClientRect failed.' }
    $o = New-Object Ui+POINT
    if (-not [Ui]::ClientToScreen($h, [ref] $o)) { throw 'ClientToScreen failed.' }
    @{ Handle = $h; X = $o.X; Y = $o.Y; W = $r.Right - $r.Left; H = $r.Bottom - $r.Top }
}

# "Front" does not mean "usable": a minimized window reports 160x28 and a loading
# screen roughly 400x180. Only from 1000x600 on is it the game.
function Wait-Window([int] $seconds) {
    $limit = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $limit) {
        $p = Get-Game
        if ($p) {
            try {
                $c = Get-Client
                if ($c.W -ge 1000 -and $c.H -ge 600) {
                    Write-Host ("  window  {0}x{1} at ({2},{3})" -f $c.W, $c.H, $c.X, $c.Y) -ForegroundColor Green
                    return $c
                }
            } catch { }
        }
        Start-Sleep -Milliseconds 1500
    }
    throw "No usable game window after $seconds s."
}

function Start-Game {
    if (Get-Game) { Write-Host '  start   already running' -ForegroundColor DarkGray; return }

    # app.yaml since 1.3.0, settings.yaml before it - a folder the new app has not started
    # against yet still holds the older one. The pattern allows leading whitespace because
    # hotsPath now sits indented under "settings:"; it is the only key of that name in
    # either file, so matching it anywhere in the file is unambiguous.
    $exe = $null
    foreach ($name in 'app.yaml', 'settings.yaml') {
        $file = Join-Path $SmurftownHome $name
        if (-not (Test-Path $file)) { continue }

        $line = Select-String -Path $file -Pattern '^\s*hotsPath:\s*(.+)$' | Select-Object -First 1
        if ($line) {
            $exe = $line.Matches[0].Groups[1].Value.Trim()
            $exe = $exe.Trim([char]39).Trim([char]34)
            break
        }
    }
    if (-not $exe -or -not (Test-Path $exe)) {
        $exe = 'C:\Program Files (x86)\Heroes of the Storm\Support64\HeroesSwitcher_x64.exe'
    }
    if (-not (Test-Path $exe)) { throw "HeroesSwitcher not found: $exe" }

    Write-Host "  start   $exe" -ForegroundColor DarkGray
    Start-Process -FilePath $exe
}

function Move-To([int] $x, [int] $y) {
    $c = Get-Client
    [void][Ui]::SetCursorPos($c.X + $x, $c.Y + $y)
    Start-Sleep -Milliseconds 80
}

function Invoke-Click([int] $x, [int] $y, [switch] $Right) {
    Move-To $x $y
    if ($Right) {
        [Ui]::mouse_event($RIGHTDOWN, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 50
        [Ui]::mouse_event($RIGHTUP, 0, 0, 0, [IntPtr]::Zero)
    } else {
        [Ui]::mouse_event($LEFTDOWN, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 50
        [Ui]::mouse_event($LEFTUP, 0, 0, 0, [IntPtr]::Zero)
    }
    Start-Sleep -Milliseconds 350
}

function Invoke-Wheel([int] $x, [int] $y, [int] $notches) {
    Move-To $x $y
    for ($i = 0; $i -lt [math]::Abs($notches); $i++) {
        [Ui]::mouse_event($WHEEL, 0, 0, (120 * [math]::Sign($notches)), [IntPtr]::Zero)
        Start-Sleep -Milliseconds 120
    }
    Start-Sleep -Milliseconds 350
}

# A game scene evaluates the SCANCODE, an input field the virtual key code. The space
# bar goes to a scene - that is why every key here carries both.
function Send-Key([string] $name) {
    $key = $VK[$name.ToLower()]
    if ($null -eq $key) { throw "Unknown key: $name" }
    $scan = [byte]([Ui]::MapVirtualKey([uint32]$key, 0))
    [Ui]::keybd_event([byte]$key, $scan, $SCANCODE, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [Ui]::keybd_event([byte]$key, $scan, ($SCANCODE -bor $KEYUP), [IntPtr]::Zero)
    Start-Sleep -Milliseconds 250
}

function Send-Text([string] $text) {
    foreach ($ch in $text.ToCharArray()) {
        # SendInput reports how many events it accepted. Zero means nothing was typed
        # at all - better to abort loudly than to send half an address.
        if ([Ui]::TypeChar($ch) -eq 0) { throw 'SendInput accepted nothing.' }
        Start-Sleep -Milliseconds 40
    }
    Start-Sleep -Milliseconds 250
}

# Email and password of an account from ~/.smurftown/data.yaml, looked up by battletag.
#
# WHY THIS LIVES HERE and is not passed as a parameter: a password that travels through
# a command line as an argument ends up in the shell history, in the process tree, and
# in every log that records it. The application itself left exactly this path behind
# with the psexec removal (CLAUDE.md, "Security model") - it does not belong back in
# via a helper script. The script reads the file the application reads anyway, and
# types from it.
function Get-Account([string] $battletag) {
    $file = Join-Path $SmurftownHome 'data.yaml'
    if (-not (Test-Path $file)) { throw "No data.yaml under $file" }

    $wanted = $battletag.Trim().ToUpperInvariant()
    $email = $null; $password = $null; $inBlock = $false

    foreach ($line in [System.IO.File]::ReadAllLines($file)) {
        # A new list entry ends the previous block. What it guards is the account that
        # carries no name of its own - a fresh entry has none, because the battletag is read
        # out of the game and not typed. Without the reset, that account's email and password
        # would be read while the block of the PREVIOUS one is still open, and this function
        # types what it returns into a login form.
        #
        # ANCHORED AT COLUMN 0, AND THAT STILL HOLDS since data.yaml became a mapping in
        # 1.3.0: YamlDotNet emits a sequence under a key without indenting it, so the items
        # begin where they always did. `WithIndentedSequences()` on the serialiser in
        # BattlenetAccountGateway would move them and break this line - quietly, and in the
        # path that types a password.
        if ($line -match '^- ') { $inBlock = $false }

        if ($line -match '^\s*-?\s*name:\s*(.+)$') {
            $name = $Matches[1].Trim().Trim([char]39).Trim([char]34).ToUpperInvariant()
            $inBlock = ($name -eq $wanted)
        }
        if ($inBlock -and $line -match '^\s*email:\s*(.+)$') {
            $email = $Matches[1].Trim().Trim([char]39).Trim([char]34)
        }
        if ($inBlock -and $line -match '^\s*password:\s*(.+)$') {
            $password = $Matches[1].Trim().Trim([char]39).Trim([char]34)
        }
    }

    if (-not $email -or -not $password) { throw "Account '$battletag' incomplete in data.yaml" }
    @{ Email = $email; Password = $password }
}

function Save-Shot([string] $name, [int] $cx = -1, [int] $cy = 0, [int] $cw = 0, [int] $ch = 0) {
    $c = Get-Client
    if ($cx -lt 0) { $cx = 0; $cy = 0; $cw = $c.W; $ch = $c.H }

    $bmp = New-Object System.Drawing.Bitmap $cw, $ch
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try { $g.CopyFromScreen(($c.X + $cx), ($c.Y + $cy), 0, 0, $bmp.Size) } finally { $g.Dispose() }

    $file = Join-Path $OutDir "$name.png"
    try { $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png) } finally { $bmp.Dispose() }
    Write-Host ("  shot    {0}.png  {1}x{2}  {3} KB" -f
        $name, $cw, $ch, [math]::Round((Get-Item $file).Length / 1KB)) -ForegroundColor Green
}

function Stop-Game {
    $p = Get-Process -Name 'HeroesOfTheStorm_x64' -ErrorAction SilentlyContinue
    if (-not $p) { Write-Host '  quit    not running' -ForegroundColor DarkGray; return }
    foreach ($one in $p) { [void]$one.CloseMainWindow() }
    Start-Sleep -Seconds 6
    $p = Get-Process -Name 'HeroesOfTheStorm_x64' -ErrorAction SilentlyContinue
    if ($p) { $p | Stop-Process -Force; Start-Sleep -Seconds 3 }
    Write-Host '  quit    stopped' -ForegroundColor DarkGray
}

foreach ($raw in ($Do -split ';')) {
    $step = $raw.Trim()
    if (-not $step) { continue }
    $verb, $arg = $step -split ':', 2

    switch ($verb.Trim().ToLower()) {
        'start'       { Start-Game }
        'wait-window' { [void](Wait-Window ([int]$arg)) }
        'info'        { $c = Get-Client
                        Write-Host ("  info    client {0}x{1} at ({2},{3})" -f $c.W, $c.H, $c.X, $c.Y) -ForegroundColor Cyan }
        'front'       { $c = Get-Client
                        [void][Ui]::BringWindowToTop($c.Handle)
                        [void][Ui]::SetForegroundWindow($c.Handle)
                        Start-Sleep -Milliseconds 500
                        Write-Host '  front' -ForegroundColor DarkGray }
        'click'       { $p = $arg -split ','; Invoke-Click ([int]$p[0]) ([int]$p[1])
                        Write-Host "  click   $arg" -ForegroundColor DarkGray }
        'right'       { $p = $arg -split ','; Invoke-Click ([int]$p[0]) ([int]$p[1]) -Right
                        Write-Host "  right   $arg" -ForegroundColor DarkGray }
        'move'        { $p = $arg -split ','; Move-To ([int]$p[0]) ([int]$p[1])
                        Write-Host "  move    $arg" -ForegroundColor DarkGray }
        'wheel'       { $p = $arg -split ','; Invoke-Wheel ([int]$p[0]) ([int]$p[1]) ([int]$p[2])
                        Write-Host "  wheel   $arg" -ForegroundColor DarkGray }
        'key'         { Send-Key $arg;  Write-Host "  key     $arg" -ForegroundColor DarkGray }
        'type'        { Send-Text $arg; Write-Host "  type    (text)" -ForegroundColor DarkGray }
        'clear'       { # End, then backspace until nothing is left. An input field
                        # evaluates the whole batch - unlike a game scene, which needs
                        # every keystroke on its own (see Send-Key).
                        Send-Key 'end'
                        for ($i = 0; $i -lt 64; $i++) {
                            [Ui]::keybd_event(0x08, 0, 0, [IntPtr]::Zero)
                            [Ui]::keybd_event(0x08, 0, $KEYUP, [IntPtr]::Zero)
                        }
                        Start-Sleep -Milliseconds 250
                        Write-Host '  clear' -ForegroundColor DarkGray }
        'user'        { Send-Text (Get-Account $arg).Email
                        Write-Host "  user    $arg" -ForegroundColor DarkGray }
        'pw'          { Send-Text (Get-Account $arg).Password
                        # NEVER print the value - only that it was typed.
                        Write-Host "  pw      $arg (from data.yaml)" -ForegroundColor DarkGray }
        'wait'        { Start-Sleep -Milliseconds ([int]$arg)
                        Write-Host "  wait    $arg" -ForegroundColor DarkGray }
        'shot'        { Save-Shot $arg }
        'crop'        { $p = $arg -split ','
                        Save-Shot $p[0] ([int]$p[1]) ([int]$p[2]) ([int]$p[3]) ([int]$p[4]) }
        'quit'        { Stop-Game }
        default       { throw "Unknown step: $step" }
    }
}

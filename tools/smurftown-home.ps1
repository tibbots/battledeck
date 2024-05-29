<#
.SYNOPSIS
    Returns the data folder the application uses - %USERPROFILE%\.smurftown, or whatever
    SMURFTOWN_HOME points at.

.DESCRIPTION
    Call it, it returns the path:

        $stHome = & "$PSScriptRoot\smurftown-home.ps1"

    WHY A FILE OF ITS OWN: three scripts read the very files the app writes - capture-run
    checks data.yaml for real addresses, drive-hots reads settings.yaml for the game path
    and data.yaml for a login, test-home creates the folder in the first place. If one of
    them resolved this differently from Smurftown/Directories.cs, it would look into one
    folder while the app works in another. For capture-run that is not an inconvenience but
    a hole: its safeguard would then vouch for a file nobody is photographing.

    MUST STAY IN STEP WITH Smurftown/Directories.cs -> Resolve(): trimmed, environment
    variables expanded, absolute.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$configured = $env:SMURFTOWN_HOME
if ([string]::IsNullOrWhiteSpace($configured)) {
    return (Join-Path $env:USERPROFILE '.smurftown')
}

return [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($configured.Trim()))

<#
Flat-screen inspection run: switch to flat mode, start the game, jump to a scene (default '#20', the dark-band scene),
then freeze it so it can be inspected with UnityExplorer (F7). F5 unfreezes/refreezes. Mouse click = blink in flat.
  flat_debug.ps1 [scene] [-NoBuild]
Back to VR afterwards: close the game, then  flat_mode.ps1 off
#>
param([string]$Scene = '#20', [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$Sim = Join-Path $PSScriptRoot 'bye_sim.ps1'
. (Join-Path $PSScriptRoot '_common.ps1')
$Bep = Get-BepInExDir
$Log = "$Bep\LogOutput.log"

if (Get-Process 'Before Your Eyes' -ErrorAction SilentlyContinue) { Stop-Process -Name 'Before Your Eyes'; Start-Sleep 4 }
& (Join-Path $PSScriptRoot 'flat_mode.ps1') on
$gs = @{ Scene = $Scene; NoAutoplay = $true }; if ($NoBuild) { $gs.NoBuild = $true }
& (Join-Path $PSScriptRoot 'goto_scene.ps1') @gs | Out-Null

# The goto is sometimes lost (Continue not clicked yet, or set too early and overwritten): verify and retry.
for ($try = 0; $try -lt 4; $try++) {
    $tail = (Get-Content $Log -Tail 200) -join "`n"
    if ($tail -match "\[cmd\] goto '" -and $tail -notmatch 'goto: not in gameplay') { break }
    if ($tail -match 'goto: not in gameplay') { & $Sim click continue; Start-Sleep 15 }
    & $Sim goto $Scene; Start-Sleep 6
}
Start-Sleep 4   # let the scene load in
& $Sim freeze on
Start-Sleep 1
if ((Get-Content $Log -Tail 50) -match '\[freeze\] on') { "frozen in $Scene. F7 = UnityExplorer, F5 = unfreeze/refreeze." } else { 'WARNING: freeze not confirmed in log; press F5 in game.' }

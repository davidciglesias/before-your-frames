<#
Switch the game between VR (UUVR loaded) and flat (UUVR moved aside). UnityExplorer stays installed either way (F7 opens it).
  flat_mode.ps1 on     flat: move UUVR out of BepInEx\plugins
  flat_mode.ps1 off    VR: put UUVR back
  flat_mode.ps1        show current mode
#>
param([ValidateSet('on', 'off', '')][string]$Mode = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')
$Bep = Get-BepInExDir
$live = Join-Path $Bep 'plugins\uuvr-mono-legacy'
$park = Join-Path $Bep 'plugins-disabled\uuvr-mono-legacy'
if ($Mode -and (Get-Process 'Before Your Eyes' -ErrorAction SilentlyContinue)) { throw 'Close the game first.' }
if ($Mode -eq 'on' -and (Test-Path $live)) {
    New-Item -ItemType Directory -Force (Split-Path $park) | Out-Null
    if (Test-Path $park) { throw "Both $live and $park exist; remove one." }
    Move-Item $live $park
}
if ($Mode -eq 'off' -and (Test-Path $park)) {
    if (Test-Path $live) { throw "Both $live and $park exist; remove one." }
    Move-Item $park $live
}
if (Test-Path $live) { 'mode: VR (UUVR loaded)' } else { 'mode: flat (UUVR parked in plugins-disabled)' }

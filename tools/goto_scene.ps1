<#
Restart the game with the freshly built plugin, enter gameplay, jump to a scene and start autoplay.
  goto_scene.ps1 <scene name fragment> [-Avoid name1,name2] [-NoBuild] [-NoAutoplay]
#>
param([Parameter(Mandatory = $true)][string]$Scene, [string[]]$Avoid = @(), [switch]$NoBuild, [switch]$NoAutoplay)
$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot
$Sim = Join-Path $PSScriptRoot 'bye_sim.ps1'
. (Join-Path $PSScriptRoot '_common.ps1')
$Bep = Get-BepInExDir
$Status = "$Bep\BYEFrameBlink_status.json"

& $Sim autoplay off
if (Get-Process 'Before Your Eyes' -ErrorAction SilentlyContinue) { Stop-Process -Name 'Before Your Eyes'; Start-Sleep 4 }
if (-not $NoBuild) { Push-Location $Root; dotnet build -c Release | Select-String ' error |Build succeeded'; Pop-Location }
Start-Process 'steam://rungameid/1082430'

function Snap { & $Sim status goto; Start-Sleep 1; try { Get-Content $Status -Raw | ConvertFrom-Json } catch { $null } }
for ($i = 0; $i -lt 60; $i++) { Start-Sleep 2; $s = Snap; if ($s -and $s.scenes -match 'MainMenu') { break } }
Start-Sleep 8
& $Sim buttons; Start-Sleep 1
$cont = (Get-Content "$Bep\LogOutput.log" -Raw) -match "button 'continue' \(Continue Button\) interactable=True"
& $Sim click $(if ($cont) { 'continue' } else { 'new game' })
for ($i = 0; $i -lt 40; $i++) { Start-Sleep 3; $s = Snap; if ($s -and $s.blinkCounterPresent) { break } }
Start-Sleep 5
& $Sim goto $Scene
Start-Sleep 6
& $Sim autoplay avoid-clear
foreach ($a in $Avoid) { & $Sim autoplay "avoid $a" }
if (-not $NoAutoplay) { & $Sim autoplay on }
'ready'

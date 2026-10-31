<#
Build a self-contained copy of the mod for running the game on the Steam Frame itself (Proton): dist\frame\
  frame_bundle.ps1
Copy the folder's contents into the game folder on the device (frame_deploy.ps1 does that over SSH). It holds:
  winhttp.dll + doorstop_config.ini   the loader, pointed at BepInEx by a path relative to the game folder
  BepInEx\core, patchers, plugins     BepInEx, UUVR (its patcher sets up the VR runtime files on every launch) and this plugin
  BepInEx\config                      the PC's configs, with the BepInEx console off (under Proton it'd be a stray window)
Rai Pal is Windows-only, so on the device this replaces it. Unity Explorer is left out.
#>
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\_common.ps1"
$Root = Split-Path $PSScriptRoot
$bep = Get-BepInExDir
$game = 'F:\Steam\steamapps\common\Before Your Eyes'
$out = Join-Path $Root 'dist\frame'

Push-Location $Root
try {
    dotnet build -c Release -p:SkipDeploy=true | Select-String ' error |Build succeeded'
    if ($LASTEXITCODE -ne 0) { throw 'build failed' }
} finally { Pop-Location }

Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $out | Out-Null
Copy-Item (Join-Path $game 'winhttp.dll') $out
(Get-Content (Join-Path $game 'doorstop_config.ini')) -replace '^target_assembly=.*$', 'target_assembly=BepInEx\core\BepInEx.Preloader.dll' |
    Set-Content (Join-Path $out 'doorstop_config.ini') -Encoding ASCII

$dst = Join-Path $out 'BepInEx'
foreach ($d in 'core', 'patchers') { Copy-Item (Join-Path $bep $d) (Join-Path $dst $d) -Recurse }
New-Item -ItemType Directory (Join-Path $dst 'plugins\BYEFrameBlink'), (Join-Path $dst 'config') | Out-Null
Copy-Item (Join-Path $bep 'plugins\uuvr-mono-legacy') (Join-Path $dst 'plugins\uuvr-mono-legacy') -Recurse
Copy-Item (Join-Path $Root 'bin\Release\net46\BYEFrameBlink.dll') (Join-Path $dst 'plugins\BYEFrameBlink')
foreach ($f in 'raicuparta.uuvr-legacy.cfg', 'byeframeblink.cfg') { Copy-Item (Join-Path $bep "config\$f") (Join-Path $dst 'config') }

# BepInEx.cfg: console off ([Logging.Console] Enabled = false), everything else as on the PC.
$section = ''
$cfg = Get-Content (Join-Path $bep 'config\BepInEx.cfg') | ForEach-Object {
    if ($_ -match '^\[(.+)\]') { $section = $Matches[1] }
    if ($section -eq 'Logging.Console' -and $_ -match '^Enabled\s*=') { 'Enabled = false' } else { $_ }
}
$cfg | Set-Content (Join-Path $dst 'config\BepInEx.cfg') -Encoding UTF8

Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum |
    ForEach-Object { "bundle: $out ($($_.Count) files, {0:N1} MB)" -f ($_.Sum / 1MB) }

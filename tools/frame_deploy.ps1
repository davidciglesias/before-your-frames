<#
Copy dist\frame (frame_bundle.ps1) into the game folder on the Steam Frame over SSH.
  frame_deploy.ps1 [-Target steamos@192.168.1.59]
Needs key login (ssh-copy-id-style). Existing configs on the device are kept unless -Configs is given, so settings
changed in the headset survive a redeploy. The game must not be running.
#>
param(
    [string]$Target = $(if ($env:BYE_FRAME) { $env:BYE_FRAME } else { 'steamos@192.168.1.59' }),
    [switch]$Configs
)
$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot
$src = Join-Path $Root 'dist\frame'
if (-not (Test-Path $src)) { throw "No bundle at $src; run frame_bundle.ps1 first." }
$gameDir = '$HOME/.local/share/Steam/steamapps/common/Before Your Eyes'
$ssh = @('-o', 'BatchMode=yes', '-o', 'ConnectTimeout=5')

$keep = if ($Configs) { '' } else { 'cp -a BepInEx/config /tmp/bye-config-keep 2>/dev/null; ' }
$restore = if ($Configs) { '' } else { 'if [ -d /tmp/bye-config-keep ]; then cp -a /tmp/bye-config-keep/. BepInEx/config/; rm -rf /tmp/bye-config-keep; fi; ' }

# Pack locally, unpack on the device (one transfer, keeps the folder layout).
$tar = Join-Path ([IO.Path]::GetTempPath()) 'bye-frame.tar'
Remove-Item $tar -ErrorAction SilentlyContinue
tar -cf $tar -C $src .
if ($LASTEXITCODE -ne 0) { throw 'tar failed' }
scp @ssh $tar "${Target}:/tmp/bye-frame.tar"
if ($LASTEXITCODE -ne 0) { throw 'scp failed' }
# Sent base64-encoded: Windows PowerShell drops the quotes inside native-command arguments, and piping adds a BOM and CRs.
$script = "set -e; cd `"$gameDir`"; if pgrep -f 'Before Your Eyes.exe' >/dev/null; then echo 'game is running, quit it first' >&2; exit 1; fi; $keep tar -xf /tmp/bye-frame.tar; $restore rm -f /tmp/bye-frame.tar; ls BepInEx/plugins"
ssh @ssh $Target "echo $([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($script))) | base64 -d | bash"
if ($LASTEXITCODE -ne 0) { throw 'deploy failed' }
Remove-Item $tar
"deployed to ${Target}:$gameDir"

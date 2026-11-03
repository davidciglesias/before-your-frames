<#
Install Before Your Frames for playing Before Your Eyes in PC VR (SteamVR), without Rai Pal.
  pc_install.ps1 [-GameFolder <path>] [-Firewall] [-Uninstall]
Run it with the game installed and not running. It downloads, from their official GitHub releases and checked against
known hashes: BepInEx 5 (the mod loader), UUVR 0.3.1 (the VR mod, Unity Mono legacy build) and this mod, and puts them
in the game folder, with UUVR's "UI Patch Mode = CanvasRedirect" set. Nothing outside the game folder is changed.
Re-running it updates the mod and keeps your settings.

If Rai Pal already manages the game's mod loader, only this mod is installed/updated, into Rai Pal's BepInEx folder.

-Firewall   also allow the game to receive eye data (UDP 9000/8888) from the headset on private networks (asks for
            admin rights once). Needed for eye tracking when streaming from a Steam Frame.
-Uninstall  remove BepInEx, UUVR and this mod from the game folder (or only this mod, under Rai Pal).
#>
param(
    [string]$GameFolder,
    [switch]$Firewall,
    [switch]$Uninstall
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # PowerShell 5.1 downloads crawl with the progress bar on
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$BepInExUrl = 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.6/BepInEx_win_x64_5.4.23.6.zip'
$BepInExSha = '6c57a714a892efb4e9e1fef63d608a3391f30ec984031c465404e4ee381a0755'
$UuvrUrl = 'https://github.com/Raicuparta/uuvr/releases/download/v0.3.1/uuvr-mono-legacy.zip'
$UuvrSha = 'e23b18bed81ef1964087c434299265783567ca8ae58037488d5589590f9db680'
$ModUrl = if ($env:BYE_ZIP) { $env:BYE_ZIP } else { 'https://github.com/davidciglesias/before-your-frames/releases/latest/download/BYEFrameBlink.zip' }
$Exe = 'Before Your Eyes.exe'

function Fail($msg) { Write-Host $msg -ForegroundColor Red; exit 1 }

# Find the game in any Steam library.
if (-not $GameFolder) {
    $steam = $null
    foreach ($key in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam') {
        $p = Get-ItemProperty $key -ErrorAction SilentlyContinue
        if ($p -and $p.SteamPath) { $steam = $p.SteamPath; break }
        if ($p -and $p.InstallPath) { $steam = $p.InstallPath; break }
    }
    $libs = @()
    if ($steam) {
        $libs += $steam
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s*"([^"]*)"')) { $libs += $m.Groups[1].Value -replace '\\\\', '\' }
        }
    }
    foreach ($lib in $libs) {
        $g = Join-Path $lib 'steamapps\common\Before Your Eyes'
        if (Test-Path (Join-Path $g $Exe)) { $GameFolder = $g; break }
    }
}
if (-not $GameFolder -or -not (Test-Path (Join-Path $GameFolder $Exe))) {
    Fail 'Before Your Eyes not found. Install it in Steam first, or pass its folder: -GameFolder "D:\path\to\Before Your Eyes"'
}
$GameFolder = (Resolve-Path $GameFolder).Path
if (Get-Process -Name 'Before Your Eyes' -ErrorAction SilentlyContinue) { Fail 'The game is running; quit it first.' }
Write-Host "Game folder: $GameFolder"

# Rai Pal installs BepInEx in its own folder and points the game's doorstop_config.ini at it.
$BepInEx = Join-Path $GameFolder 'BepInEx'
$RaiPal = $false
$doorstop = Join-Path $GameFolder 'doorstop_config.ini'
if (Test-Path $doorstop) {
    $target = Select-String -Path $doorstop -Pattern '^\s*target_assembly\s*=\s*(.+)$' | Select-Object -First 1
    if ($target) {
        $t = $target.Matches[0].Groups[1].Value.Trim()
        if ([IO.Path]::IsPathRooted($t) -and $t -match 'rai-pal') {
            $RaiPal = $true
            $BepInEx = Split-Path (Split-Path $t)   # ...\BepInEx\core\BepInEx.Preloader.dll -> ...\BepInEx
            Write-Host "Rai Pal manages the mod loader here: $BepInEx"
        }
    }
}

function Add-FirewallRules {
    $exePath = Join-Path $GameFolder $Exe
    $cmd = "Remove-NetFirewallRule -DisplayName 'Before Your Frames eye data' -ErrorAction SilentlyContinue; " +
           "New-NetFirewallRule -DisplayName 'Before Your Frames eye data' -Direction Inbound -Action Allow -Protocol UDP " +
           "-LocalPort 9000,8888 -Program '$exePath' -Profile Private | Out-Null"
    Write-Host 'Adding a firewall rule (Windows will ask for admin rights)...'
    try {
        $p = Start-Process powershell -Verb RunAs -Wait -PassThru -WindowStyle Hidden -ArgumentList '-NoProfile', '-Command', $cmd
        if ($p.ExitCode -eq 0) { Write-Host 'Firewall: the game may now receive eye data on UDP 9000/8888 (private networks).' }
        else { Write-Host "Firewall rule failed (exit $($p.ExitCode))." -ForegroundColor Yellow }
    } catch { Write-Host 'Firewall rule skipped (admin rights declined).' -ForegroundColor Yellow }
}

if ($Uninstall) {
    Remove-Item (Join-Path $BepInEx 'plugins\BYEFrameBlink') -Recurse -Force -ErrorAction SilentlyContinue
    if ($RaiPal) {
        Write-Host 'Removed this mod. UUVR and BepInEx belong to Rai Pal: uninstall them there.'
    } else {
        foreach ($f in 'BepInEx', 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'changelog.txt') {
            Remove-Item (Join-Path $GameFolder $f) -Recurse -Force -ErrorAction SilentlyContinue
        }
        Write-Host 'Removed BepInEx, UUVR and this mod from the game folder.'
        Write-Host "To undo UUVR's change to the game's settings file, run Verify integrity of game files in Steam."
    }
    exit 0
}

$tmp = Join-Path ([IO.Path]::GetTempPath()) ('bye-install-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $tmp | Out-Null
try {
    function Fetch($url, $file, $sha) {
        $dest = Join-Path $tmp $file
        if ($url -match '^https?://') { Invoke-WebRequest -Uri $url -OutFile $dest -UseBasicParsing } else { Copy-Item $url $dest }
        if ($sha -and (Get-FileHash $dest -Algorithm SHA256).Hash.ToLower() -ne $sha) { throw "Checksum mismatch for $url" }
        $dest
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    function Unzip($zip, $dir) {
        # Expand-Archive -Force on 5.1 can't overwrite read-only files and is slow; extract entry by entry instead.
        $z = [IO.Compression.ZipFile]::OpenRead($zip)
        try {
            foreach ($e in $z.Entries) {
                $out = Join-Path $dir $e.FullName
                if ($e.FullName.EndsWith('/')) { New-Item -ItemType Directory $out -Force | Out-Null; continue }
                New-Item -ItemType Directory (Split-Path $out) -Force | Out-Null
                [IO.Compression.ZipFileExtensions]::ExtractToFile($e, $out, $true)
            }
        } finally { $z.Dispose() }
    }

    Write-Host 'Downloading Before Your Frames...'
    $modZip = Fetch $ModUrl 'mod.zip' ''
    if (-not $RaiPal) {
        Write-Host 'Downloading BepInEx and UUVR...'
        $bepZip = Fetch $BepInExUrl 'bepinex.zip' $BepInExSha
        $uuvrZip = Fetch $UuvrUrl 'uuvr.zip' $UuvrSha
        Unzip $bepZip $GameFolder
        Unzip $uuvrZip (Join-Path $tmp 'uuvr')
        foreach ($kind in 'patchers', 'plugins') {
            $dst = Join-Path $BepInEx "$kind\uuvr-mono-legacy"
            New-Item -ItemType Directory $dst -Force | Out-Null
            Copy-Item (Join-Path $tmp "uuvr\$kind\*") $dst -Recurse -Force
        }
    } elseif (-not (Test-Path (Join-Path $BepInEx 'plugins\uuvr-mono-legacy'))) {
        Write-Host 'UUVR is not installed in Rai Pal for this game yet: install UUVR (Unity Mono, legacy) there too.' -ForegroundColor Yellow
    }

    $modDir = Join-Path $BepInEx 'plugins\BYEFrameBlink'
    New-Item -ItemType Directory $modDir -Force | Out-Null
    $z = [IO.Compression.ZipFile]::OpenRead($modZip)
    try {
        $dll = $z.Entries | Where-Object { $_.Name -eq 'BYEFrameBlink.dll' } | Select-Object -First 1
        if (-not $dll) { throw 'BYEFrameBlink.dll is not in the downloaded zip.' }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($dll, (Join-Path $modDir 'BYEFrameBlink.dll'), $true)
    } finally { $z.Dispose() }

    # UUVR draws the game's UI on its panel (CanvasRedirect: the default mirror shows the desktop frame there).
    # Only the key inside its own section is changed; the section is added if missing. Other settings are written at
    # the first launch.
    $cfgDir = Join-Path $BepInEx 'config'
    New-Item -ItemType Directory $cfgDir -Force | Out-Null
    $cfg = Join-Path $cfgDir 'raicuparta.uuvr-legacy.cfg'
    $lines = if (Test-Path $cfg) { @(Get-Content $cfg) } else { @() }
    $out = New-Object System.Collections.Generic.List[string]
    $inSection = $false; $done = $false
    foreach ($l in $lines) {
        if ($l -match '^\[') {
            if ($inSection -and -not $done) { $out.Add('UI Patch Mode = CanvasRedirect'); $done = $true }
            $inSection = ($l.Trim() -eq '[UI]')
        }
        if ($inSection -and $l -match '^UI Patch Mode\s*=') { $out.Add('UI Patch Mode = CanvasRedirect'); $done = $true; continue }
        $out.Add($l)
    }
    if (-not $done) { if (-not $inSection) { $out.Add(''); $out.Add('[UI]') }; $out.Add('UI Patch Mode = CanvasRedirect') }
    [IO.File]::WriteAllLines($cfg, $out)
} finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

if ($Firewall) { Add-FirewallRules }

Write-Host ''
Write-Host 'Done. Start SteamVR, then launch Before Your Eyes from Steam (no launch options needed).' -ForegroundColor Green
if ($RaiPal) { Write-Host '(Rai Pal setup: launching from Rai Pal works as before.)' }
if (-not $Firewall) { Write-Host 'Streaming eye data from a Steam Frame? Run again with -Firewall, or allow the game when Windows asks.' }

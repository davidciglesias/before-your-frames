<#
Regression tests for BYEFrameBlink. Builds the plugin, (re)starts the game, drives it over OSC (tools/bye_sim.ps1)
and asserts on /bye/status snapshots. No headset interaction needed (head pose and eyes are simulated), but SteamVR
must be running because UUVR needs it.

  tools\run_tests.ps1                 build + restart game + run all tests
  tools\run_tests.ps1 -NoBuild -Attach  run against the game that's already running (already in gameplay is fine)
  tools\run_tests.ps1 -CloseGame      quit the game when done

Your byeframeblink.cfg is backed up and restored. Results go to tools\last_test_results.txt. Exit code 0 = all passed.
Note: if no story progress exists it starts a New Game; otherwise it presses Continue.
#>
param([switch]$NoBuild, [switch]$Attach, [switch]$CloseGame)

$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent
$Sim = Join-Path $PSScriptRoot 'bye_sim.ps1'
. (Join-Path $PSScriptRoot '_common.ps1')
$BepInEx = Get-BepInExDir
$Log = Join-Path $BepInEx 'LogOutput.log'
$StatusFile = Join-Path $BepInEx 'BYEFrameBlink_status.json'
$Cfg = Join-Path $BepInEx 'config\byeframeblink.cfg'
$CfgBackup = Join-Path $env:TEMP 'byeframeblink.cfg.testbackup'
$Results = New-Object System.Collections.Generic.List[object]

function Wait-Until([scriptblock]$cond, [int]$timeoutSec, [int]$pollMs = 500) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $timeoutSec) { if (& $cond) { return $true }; [Threading.Thread]::Sleep($pollMs) }
    return $false
}

function Check([string]$name, [bool]$ok, [string]$detail = '') {
    $Results.Add([pscustomobject]@{ Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Test = $name; Detail = $detail })
    $color = if ($ok) { 'Green' } else { 'Red' }
    Write-Host ("[{0}] {1} {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $detail) -ForegroundColor $color
}

function Near([double]$a, [double]$b, [double]$tol) { [Math]::Abs($a - $b) -le $tol }

$script:statusSeq = 0
function Status([string]$tag = 'snap') {
    $script:statusSeq++; $t = "$tag-$($script:statusSeq)"
    & $Sim status $t
    $ok = Wait-Until { (Test-Path $StatusFile) -and ((Get-Content $StatusFile -Raw -ErrorAction SilentlyContinue) -match [regex]::Escape("`"$t`"")) } 10 100
    if (-not $ok) { throw "No status response for '$t' (is the game running with the plugin?)" }
    return (Get-Content $StatusFile -Raw | ConvertFrom-Json)
}

function Cfg([string]$section, [string]$key, [string]$value) { & $Sim cfg "byeframeblink|$section|$key|$value"; [Threading.Thread]::Sleep(200) }

function Repeat-Send([scriptblock]$send, [double]$seconds) {
    $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.Elapsed.TotalSeconds -lt $seconds) { & $send; [Threading.Thread]::Sleep(16) }
}

try {
    # ---------------------------------------------------------------- build + launch
    if (-not $NoBuild) {
        if (Get-Process 'Before Your Eyes' -ErrorAction SilentlyContinue) { Stop-Process -Name 'Before Your Eyes'; [Threading.Thread]::Sleep(3000) }
        $build = & dotnet build (Join-Path $Root 'BYEFrameBlink.csproj') -c Release 2>&1 | Out-String
        Check 'build' ($LASTEXITCODE -eq 0) ''
        if ($LASTEXITCODE -ne 0) { Write-Host $build; throw 'build failed' }
    }

    Copy-Item $Cfg $CfgBackup -Force

    if (-not $Attach -or -not (Get-Process 'Before Your Eyes' -ErrorAction SilentlyContinue)) {
        if (Get-Process 'Before Your Eyes' -ErrorAction SilentlyContinue) { Stop-Process -Name 'Before Your Eyes'; [Threading.Thread]::Sleep(3000) }
        $launchTime = Get-Date
        Start-Process 'steam://rungameid/1082430'
        $loaded = Wait-Until { (Test-Path $Log) -and (Get-Item $Log).LastWriteTime -gt $launchTime -and ((Get-Content $Log -Raw) -match 'Before Your Frames loaded') } 120 1000
        Check 'plugin loads' $loaded
        if (-not $loaded) { throw 'plugin did not load' }
        Wait-Until { (Get-Content $Log -Raw) -match 'Chainloader startup complete' } 60 1000 | Out-Null
    }

    $logText = Get-Content $Log -Raw
    $bye = ($logText -split "`n") | Where-Object { $_ -match 'Before Your Frames' -and $_ -match 'Exception|Error' }
    Check 'no plugin errors in log at startup' (-not $bye) ($bye -join ' | ')

    # Wait until the plugin answers status (main loop running).
    Wait-Until { try { Status 'ping' | Out-Null; $true } catch { $false } } 90 2000 | Out-Null
    $s = Status 'boot'
    Check 'IL redirect count' ($s.patchedMethods -ge 70) "patchedMethods=$($s.patchedMethods)"
    $types = $s.patchedTypes -split ','
    foreach ($t in 'BlinkCounter', 'EyesClosedMechanic', 'EyesClosedEvent', 'DeathLoop', 'InteractableDialogueBlender', 'InteractableManager', 'WebcamAvStim', 'WebcamTextureStarterSelectable') {
        Check "redirects $t" ($types -contains $t)
    }
    Check 'UUVR pose driver patched' ([bool]$s.uuvrPatched)
    Check 'XR enabled' ([bool]$s.xrEnabled)

    # ---------------------------------------------------------------- into gameplay
    if (-not ($s.scenes -match '_CodaMain')) {
        Wait-Until { (Status 'menu').scenes -match 'MainMenu' } 90 2000 | Out-Null
        [Threading.Thread]::Sleep(4000)
        & $Sim buttons; [Threading.Thread]::Sleep(1500)
        $canContinue = (Get-Content $Log -Raw) -match "button 'continue' \(Continue Button\) interactable=True"
        $intro = Status 'menu-intro'
        $seenBefore = ((Get-Content $CfgBackup) -match '^Intro Seen = true').Count -gt 0
        if ($seenBefore -and -not $Attach) { Check 'intro logos skipped (intro already seen)' ($intro.introSkipped -ge 1) "skipped=$($intro.introSkipped)" }
        Check 'intro marked seen after reaching menu' ([bool]$intro.introSeen)
        $m = Status 'menu-webcam'
        Check 'webcam off in main menu' ($m.webcamPlaying -eq 0) "webcamPlaying=$($m.webcamPlaying) playsBlocked=$($m.webcamPlaysBlocked)"

        # Blink menus: aim the (simulated) head at the button, then blink.
        Cfg 'Debug' 'Test Mode' 'true'
        Cfg 'Head Tracking' 'Simulate Head' 'true'
        Cfg 'VR' 'Render Scale' '1'
        Cfg 'VR' 'Menu Render Scale' '1.5'
        # Chapter select -> "Are you sure?" -> no, all by gaze + deliberate blink (answers no: the save is untouched).
        function MenuPress([string]$target, [string]$tag) {
            & $Sim menuaim $target; [Threading.Thread]::Sleep(700)
            $before = Status "$tag-aim"
            & $Sim blink 500; [Threading.Thread]::Sleep(1200)
            $after = Status "$tag-press"
            return @($before, $after)
        }
        # Snapping: aiming near (not on) a button still selects it; far away selects nothing. Aim only, never blink here.
        & $Sim menuaim 'Quit Button|3'; [Threading.Thread]::Sleep(700)
        $sn = Status 'snap-near'
        Check 'menu panel enlarged while in a menu' (Near $sn.menuPanelWidth 2.7 0.05) "width=$($sn.menuPanelWidth)"
        Check 'UI panel texture mipmapped (crisp text)' ($sn.uiTexture -match 'mips=True' -and $sn.uiTexture -match 'Trilinear') "uiTexture=$($sn.uiTexture)"
        Check 'main menu rendered at Menu Render Scale' ($sn.eyeTexture -match 'scale=1\.50') "eyeTexture=$($sn.eyeTexture)"
        Check 'menu: 3 deg below a button still selects it' ($sn.menuHover -eq 'Quit Button') "hover=$($sn.menuHover) deg=$($sn.menuHoverDegrees)"
        & $Sim menuaim 'Quit Button|14'; [Threading.Thread]::Sleep(700)
        $sf = Status 'snap-far'
        Check 'menu: 14 deg away selects nothing' ($sf.menuHover -eq '') "hover=$($sf.menuHover) deg=$($sf.menuHoverDegrees)"
        $r = MenuPress 'Chapters Button' 'chap'
        Check 'gaze+blink opens Chapters' (($r[0].menuHover -eq 'Chapters Button') -and ($r[1].menuClicks -eq $r[0].menuClicks + 1)) "hover=$($r[0].menuHover) clicks=$($r[1].menuClicks)"
        # The chapter next to Back must stay selectable (Back's longer reach mustn't steal it). Highest unlocked chapter.
        & $Sim buttons
        $last = $null
        for ($i = 0; $i -lt 10 -and -not $last; $i++) {
            [Threading.Thread]::Sleep(500)
            $last = (Select-String -Path $Log -Pattern '\[cmd\] button .*\((Chapter \d+ Button)\)' | ForEach-Object { $_.Matches[0].Groups[1].Value } |
                Sort-Object { [int]($_ -replace '\D', '') } | Select-Object -Last 1)
        }
        Check 'chapter buttons listed' ([bool]$last) "last=$last"
        if ($last) {
            & $Sim menuaim $last; [Threading.Thread]::Sleep(700)
            $lc = Status 'last-chapter'
            Check "menu: last chapter ($last) selectable next to Back" ($lc.menuHover -eq $last) "hover=$($lc.menuHover)"
        }
        $r = MenuPress 'Chapter 1 Button' 'chap1'
        Check 'gaze+blink picks a chapter' (($r[0].menuHover -eq 'Chapter 1 Button') -and ($r[1].menuClicks -eq $r[0].menuClicks + 1)) "hover=$($r[0].menuHover)"
        $r = MenuPress 'No_Button' 'confirm'
        Check 'confirm dialog is gaze-targetable (No)' ($r[0].menuHover -eq 'No_Button') "hover=$($r[0].menuHover)"
        Check 'gaze+blink No closes the dialog, stays in menu' (($r[1].menuClicks -eq $r[0].menuClicks + 1) -and ($r[1].scenes -match 'MainMenu') -and -not ($r[1].scenes -match '_CodaMain')) "clicks=$($r[1].menuClicks) scenes=$($r[1].scenes)"
        & $Sim menuaim 'Back_Button|3'; [Threading.Thread]::Sleep(700)
        $bn = Status 'back-near'
        Check 'menu: 3 deg off Back still selects Back' ($bn.menuHover -eq 'Back_Button') "hover=$($bn.menuHover) deg=$($bn.menuHoverDegrees)"
        $r = MenuPress 'Back_Button' 'back'
        Check 'gaze+blink Back returns to main menu' ($r[1].menuClicks -eq $r[0].menuClicks + 1) "hover=$($r[0].menuHover)"
        [Threading.Thread]::Sleep(800)
        # In VR the game's settings page is replaced by our subtitles page: no subtitles + 8 languages, and back.
        $r = MenuPress 'BYE Subtitles Button' 'subs-open'
        Check 'gaze+blink opens the subtitles page' ([bool]$r[1].vrMenuOpen -and ($r[1].vrMenuRows -eq 10)) "open=$($r[1].vrMenuOpen) rows=$($r[1].vrMenuRows)"
        $r = MenuPress 'BYE Settings Back Button' 'subs-back'
        Check 'gaze+blink Back leaves the subtitles page' (-not [bool]$r[1].vrMenuOpen) "hover=$($r[0].menuHover) open=$($r[1].vrMenuOpen)"
        [Threading.Thread]::Sleep(800)

        # VR settings: an entry under Settings, our options in the same column, rows change settings, back restores the menu.
        $v0 = Status 'vr-entry'
        Check 'vr settings entry in the main menu' ($v0.vrMenuEntries -ge 1) "entries=$($v0.vrMenuEntries)"
        $r = MenuPress 'VR Settings Button' 'vr-open'
        Check 'gaze+blink opens VR settings' ([bool]$r[1].vrMenuOpen -and ($r[1].vrMenuRows -eq 8)) "open=$($r[1].vrMenuOpen) rows=$($r[1].vrMenuRows)"
        $sep0 = $r[1].eyeSeparation
        $r = MenuPress 'VR World Size Button' 'vr-size'
        Check 'VR settings: world size row cycles it' (-not (Near $r[1].eyeSeparation $sep0 0.01)) "$sep0 -> $($r[1].eyeSeparation)"
        & $Sim menuaim 'VR Aiming Dot Button'; [Threading.Thread]::Sleep(700)
        $pb0 = Status 'vr-pad-aim'
        & $Sim padblink 100; [Threading.Thread]::Sleep(800)
        $pb1 = Status 'vr-pad-press'
        Check 'controller blink button: a short press clicks a menu option' ($pb1.menuClicks -eq $pb0.menuClicks + 1) "hover=$($pb0.menuHover) clicks=$($pb0.menuClicks)->$($pb1.menuClicks)"
        $r = MenuPress 'VR Back Button' 'vr-back'
        Check 'VR settings back restores the main menu' (-not $r[1].vrMenuOpen) "open=$($r[1].vrMenuOpen)"
        & $Sim menuaim 'Quit Button'; [Threading.Thread]::Sleep(700)
        $vq = Status 'vr-after'
        Check 'main menu buttons back after VR settings' ($vq.menuHover -eq 'Quit Button') "hover=$($vq.menuHover)"

        $label = if ($canContinue) { 'continue' } else { 'new game' }
        & $Sim menuaim $label; [Threading.Thread]::Sleep(600)
        $mm = Status 'menu-aim'
        Check 'blink menus active in main menu' ([bool]$mm.menuActive)
        Check "menu gaze hovers '$label'" ($mm.menuHover -match '(?i)continue|new game') "hover=$($mm.menuHover)"
        & $Sim blink 120; [Threading.Thread]::Sleep(1500)
        $ms = Status 'menu-short-blink'
        Check 'natural (short) blink does not press menu buttons' (($ms.menuClicks -eq $mm.menuClicks) -and ($ms.scenes -match 'MainMenu') -and -not ($ms.scenes -match '_CodaMain')) "clicks=$($ms.menuClicks) scenes=$($ms.scenes)"
        & $Sim menuaim $label; [Threading.Thread]::Sleep(300)
        & $Sim blink 500; [Threading.Thread]::Sleep(1000)
        $ml = Status 'menu-long-blink'
        Check 'deliberate blink presses the menu button' ($ml.menuClicks -eq $mm.menuClicks + 1) "clicks=$($ml.menuClicks)"
        if ($ml.menuClicks -eq $mm.menuClicks) { & $Sim click $label } # fall back so the rest can still run
    }
    $inGame = Wait-Until { $x = Status 'load'; $x.blinkCounterPresent -and ($x.scenes -match '_CodaMain') } 120 3000
    Check 'reaches gameplay (_CodaMain + BlinkCounter)' $inGame
    if (-not $inGame) { throw 'could not reach gameplay' }
    if ($sn) {
        $rs = Status 'render-scale-game'
        Check 'gameplay back at Render Scale (1)' ($rs.eyeTexture -match 'scale=1\.00') "eyeTexture=$($rs.eyeTexture)"
        Check 'performance mode auto = off on Windows' ($rs.performance -match '^auto \(off\) wine=False') "performance=$($rs.performance)"
        & $Sim perf on; [Threading.Thread]::Sleep(1500)
        $pf = Status 'perf-on'
        Check 'performance mode: 2x MSAA, no HDR, shadows medium / 2 cascades / 40 m' (($pf.quality -match 'aa=2 ') -and ($pf.render -match 'hdr=False') -and ($pf.render -match 'shadowRes=(Low|Medium) dist=([0-3]?\d|40) cascades=[12] ')) "quality=$($pf.quality) render=$($pf.render)"
        & $Sim perf auto
    }
    [Threading.Thread]::Sleep(5000)

    $s = Status 'game'
    Check 'click-blink mode (DebugKeyOnly)' ($s.detectionMode -eq 'DebugKeyOnly') "detectionMode=$($s.detectionMode)"
    Check 'webcam off in gameplay' ($s.webcamPlaying -eq 0) "webcamPlaying=$($s.webcamPlaying) trackerInitsBlocked=$($s.trackerInitsBlocked)"

    # Deterministic inputs for the rest of the tests: simulated head, ignore real eye-tracker OSC.
    Cfg 'Debug' 'Test Mode' 'true'
    Cfg 'Head Tracking' 'Simulate Head' 'true'
    & $Sim head 0 0 0 0 0 0; & $Sim recenter; [Threading.Thread]::Sleep(500)

    # ---------------------------------------------------------------- blinks
    [Threading.Thread]::Sleep(1500) # let any previous lid data time out
    $a = Status 'pre-blink'
    & $Sim blink 150
    $b = Status 'post-blink'
    Check 'OSC blink -> plugin blink' ($b.pluginBlinks -eq $a.pluginBlinks + 1) "plugin $($a.pluginBlinks)->$($b.pluginBlinks)"
    Check 'OSC blink -> game BlinkConfirmed' ($b.gameBlinks -eq $a.gameBlinks + 1) "game $($a.gameBlinks)->$($b.gameBlinks)"

    & $Sim blink 20
    $c = Status 'post-flicker'
    Check '20 ms flicker filtered' ($c.pluginBlinks -eq $b.pluginBlinks) "plugin $($b.pluginBlinks)->$($c.pluginBlinks)"

    [Threading.Thread]::Sleep(1300) # the test channel outranks every tracker: let it go quiet first
    Repeat-Send { & $Sim ft 0.8 } 0.3; Repeat-Send { & $Sim ft 0.1 } 0.3; Repeat-Send { & $Sim ft 0.8 } 0.4
    $d = Status 'post-ft'
    Check 'frameeyeosc FT/v2/EyeLid addresses' ($d.pluginBlinks -eq $c.pluginBlinks + 1) "plugin $($c.pluginBlinks)->$($d.pluginBlinks)"

    $job = Start-Job -ScriptBlock { param($p) & $p hold 2.5 } -ArgumentList $Sim
    [Threading.Thread]::Sleep(1800)
    $e = Status 'mid-hold'
    Wait-Job $job -Timeout 10 | Out-Null; Remove-Job $job -Force
    $f = Status 'post-hold'
    Check 'held eyes read as closed' ([bool]$e.eyesClosed) "openness=$($e.openness)"
    Check 'eyes open after hold' (-not [bool]$f.eyesClosed) "openness=$($f.openness)"
    Check 'hold counts as one blink' ($f.pluginBlinks -eq $d.pluginBlinks + 1) "plugin $($d.pluginBlinks)->$($f.pluginBlinks)"

    # ---------------------------------------------------------------- eye-tracking sources (priority chain)
    $prev = $f
    foreach ($src in @(@('steamvr', 'SteamVR'), @('babble', 'Babble'), @('vrcft', 'VRCFT'))) {
        $cmd = $src[0]; $label = $src[1]
        [Threading.Thread]::Sleep(1300) # previous source goes quiet
        Repeat-Send { & $Sim $cmd 0.8 } 0.3; Repeat-Send { & $Sim $cmd 0.1 } 0.3; Repeat-Send { & $Sim $cmd 0.8 } 0.4
        $s = Status "src-$cmd"
        Check "$label addresses -> one blink" ($s.pluginBlinks -eq $prev.pluginBlinks + 1) "plugin $($prev.pluginBlinks)->$($s.pluginBlinks)"
        Check "$label reported as source" ($s.eyeSource -eq $label) "eyeSource=$($s.eyeSource)"
        $prev = $s
    }

    [Threading.Thread]::Sleep(1300)
    Repeat-Send { & $Sim ft 0.8; & $Sim steamvr 0.8 } 0.8
    Repeat-Send { & $Sim ft 0.8; & $Sim steamvr 0.1 } 0.4
    Repeat-Send { & $Sim ft 0.8; & $Sim steamvr 0.8 } 0.4
    $s = Status 'chain-priority'
    Check 'frameeyeosc outranks SteamVR (its blink ignored)' ($s.pluginBlinks -eq $prev.pluginBlinks) "plugin $($prev.pluginBlinks)->$($s.pluginBlinks)"
    Check 'frameeyeosc in use while both send' ($s.eyeSource -eq 'frameeyeosc') "eyeSource=$($s.eyeSource)"

    Repeat-Send { & $Sim steamvrgaze 0.4 0; & $Sim ft 0.8; & $Sim steamvr 0.8 } 0.8
    $s = Status 'chain-gaze-other'
    Check "lower source's gaze ignored" (-not [bool]$s.gazeActive) "gazeActive=$($s.gazeActive) yaw=$($s.gazeYaw)"
    Repeat-Send { & $Sim ftgaze -0.4 0; & $Sim steamvrgaze 0.4 0; & $Sim ft 0.8; & $Sim steamvr 0.8 } 0.8
    $s = Status 'chain-gaze-active'
    Check "active source's gaze used" ([bool]$s.gazeActive -and (Near $s.gazeYaw -18 1)) "gazeActive=$($s.gazeActive) yaw=$($s.gazeYaw)"

    Repeat-Send { & $Sim steamvr 0.8; & $Sim steamvrgaze 0.4 0 } 1.4
    $s = Status 'chain-fallback'
    Check 'SteamVR takes over when frameeyeosc goes quiet' ($s.eyeSource -eq 'SteamVR') "eyeSource=$($s.eyeSource)"
    Check 'gaze follows the new source' ([bool]$s.gazeActive -and (Near $s.gazeYaw 18 1)) "gazeActive=$($s.gazeActive) yaw=$($s.gazeYaw)"
    $before = $s.pluginBlinks
    Repeat-Send { & $Sim steamvr 0.1 } 0.3; Repeat-Send { & $Sim steamvr 0.8 } 0.4
    $s = Status 'chain-fallback-blink'
    Check 'fallback source blinks' ($s.pluginBlinks -eq $before + 1) "plugin $before->$($s.pluginBlinks)"

    Repeat-Send { & $Sim ft 0.8; & $Sim steamvr 0.8 } 0.9
    $s = Status 'chain-recover'
    Check 'frameeyeosc takes back over when it resumes' ($s.eyeSource -eq 'frameeyeosc') "eyeSource=$($s.eyeSource)"
    $f = $s

    [Threading.Thread]::Sleep(1600)
    $g = Status 'timeout'
    Check 'no data -> eyes open (fail-safe)' ((-not [bool]$g.lidHasData) -and (-not [bool]$g.eyesClosed))

    # ---------------------------------------------------------------- gaze aiming + reticle
    Cfg 'VR' 'Show Reticle' 'true'; [Threading.Thread]::Sleep(300)
    $h = Status 'no-gaze'
    Check 'no gaze data -> center aim' ((-not [bool]$h.gazeActive) -and (Near $h.aimAngleFromCenter 0 0.5)) "active=$($h.gazeActive) angle=$($h.aimAngleFromCenter)"
    Check 'UUVR UI overlay camera found' ([bool]$h.uiOverlayCamera)
    $reticle = [bool]$h.reticleDrawing
    if ($reticle) { Check 'reticle at view center with no gaze' ((Near $h.reticleYaw 0 0.2) -and (Near $h.reticlePitch 0 0.2)) "yaw=$($h.reticleYaw) pitch=$($h.reticlePitch)" }
    Cfg 'VR' 'Show Reticle' 'false'; [Threading.Thread]::Sleep(300)
    $hh = Status 'no-gaze-hidden'
    Check 'no eye data: hidden aiming dot shown anyway' ([bool]$hh.reticleDrawing) "drawing=$($hh.reticleDrawing)"
    Cfg 'VR' 'Show Reticle' 'true'

    Repeat-Send { & $Sim gaze 0.4 0 } 0.8
    $i = Status 'gaze-right'
    Check 'gaze right active' ([bool]$i.gazeActive)
    Check 'gaze right = 18 deg yaw' (Near $i.gazeYaw 18 1) "gazeYaw=$($i.gazeYaw)"
    Check 'aim ray bent 18 deg' (Near $i.aimAngleFromCenter 18 1.5) "angle=$($i.aimAngleFromCenter)"
    Check 'aim ray goes right' ($i.aimLocalX -gt 0.2) "localX=$($i.aimLocalX)"
    if ($reticle) { Check 'reticle follows gaze right' (Near $i.reticleYaw 18 1) "yaw=$($i.reticleYaw)" }

    Repeat-Send { & $Sim gaze 0 0.3 } 0.8
    $j = Status 'gaze-up'
    Check 'aim ray goes up' ($j.aimLocalY -gt 0.15 -and [Math]::Abs($j.aimLocalX) -lt 0.05) "local=($($j.aimLocalX), $($j.aimLocalY))"
    if ($reticle) { Check 'reticle follows gaze up' (Near $j.reticlePitch 13.5 1) "pitch=$($j.reticlePitch)" }

    # Regression: the reticle used to be clamped to UUVR's UI panel (~ +-14 deg vertically).
    Repeat-Send { & $Sim gaze 0 -0.6 } 0.8
    $jd = Status 'gaze-down'
    Check 'aim ray goes far down (27 deg)' ((Near $jd.aimAngleFromCenter 27 1.5) -and ($jd.aimLocalY -lt -0.4)) "angle=$($jd.aimAngleFromCenter) localY=$($jd.aimLocalY)"
    if ($reticle) { Check 'reticle reaches 27 deg down (not clamped)' (Near $jd.reticlePitch -27 1) "pitch=$($jd.reticlePitch)" }

    # Gaze must freeze during a blink (eyelid closed -> gaze readings are junk).
    Repeat-Send { & $Sim gaze 0.4 0; & $Sim lid 0.8 } 0.6
    Repeat-Send { & $Sim lid 0.05; & $Sim gaze -0.4 0 } 0.4
    $k = Status 'gaze-during-blink'
    Check 'gaze frozen while eyes closed' (Near $k.gazeYaw 18 1.5) "gazeYaw=$($k.gazeYaw)"
    # ...and rewind to where you looked just before the lids closed (eyes drift while closing).
    [Threading.Thread]::Sleep(1500) # gaze + lid time out
    Repeat-Send { & $Sim gaze 0.4 0; & $Sim lid 0.8 } 0.6
    Repeat-Send { & $Sim gaze -0.4 0; & $Sim lid 0.8 } 0.1
    Repeat-Send { & $Sim lid 0.05; & $Sim gaze -0.4 0 } 0.4
    $kr = Status 'gaze-rewind'
    Check 'blink aims where you looked before the lids closed' (Near $kr.gazeYaw 18 3) "gazeYaw=$($kr.gazeYaw)"

    # Reticle is head-locked: turning the head without gaze keeps it at view center (where the aim ray is).
    [Threading.Thread]::Sleep(1500) # gaze + lid time out
    & $Sim head 0 0 0 0 20 0; [Threading.Thread]::Sleep(400)
    $l = Status 'head-yaw'
    if ($reticle) { Check 'reticle stays at view center when head turns' (Near $l.reticleYaw 0 0.2) "yaw=$($l.reticleYaw)" }
    # Regression: the game's look-at ray used to ignore the VR head (only the camera transform + gaze).
    Check "game aim ray follows head yaw (20 deg)" ((Near $l.aimAngleFromCenter 20 1) -and ($l.aimLocalX -gt 0.3)) "angle=$($l.aimAngleFromCenter) localX=$($l.aimLocalX)"
    & $Sim head 0 0 0 -15 0 0; [Threading.Thread]::Sleep(400)
    $lp = Status 'head-pitch'
    Check "game aim ray follows head pitch (15 deg up)" ((Near $lp.aimAngleFromCenter 15 1) -and ($lp.aimLocalY -gt 0.2)) "angle=$($lp.aimAngleFromCenter) localY=$($lp.aimLocalY)"
    & $Sim head 0 0 0 0 0 0

    # ---------------------------------------------------------------- aim assist (bigger hitbox)
    $a = Status 'aimtest'
    Check 'aim assist self-test ran' ([bool]$a.aimTestRan)
    if ($a.aimTestRan) {
        Check 'aim assist: 2 deg miss without assist' (-not [bool]$a.aimTestBare2)
        Check 'aim assist: 2 deg off still hits (3 deg cone)' ([bool]$a.aimTestAssist2)
        Check 'aim assist: 6 deg off does not hit' (-not [bool]$a.aimTestAssist6)
    }

    # ---------------------------------------------------------------- full-view eyelid veil
    $v = Status 'veil-open'
    Check 'veil ready' ([bool]$v.veilReady)
    Check 'veil mirrors fades/backdrops too' ($v.veilFullScreenImages -ge 1) "count=$($v.veilFullScreenImages)"
    Check 'original lids/fades hidden on UI panel' ([bool]$v.veilOriginalsHidden)
    Check 'veils draw under the UI panel (menus/dialogs stay visible)' ([bool]$v.veilBehindPanel)
    Check 'volumetric light effect off in VR (it would stick to the view)' (-not [bool]$v.volumetricOn)
    Check 'overlay sprites only rendered by overlay camera (not into UI panel)' ([bool]$v.overlayLayerExclusive)
    $openUpperOk = ($v.veilUpperAlpha -lt 0.01) -or ($v.veilUpperEdge -ge 0.45)
    $openLowerOk = ($v.veilLowerAlpha -lt 0.01) -or ($v.veilLowerEdge -le -0.45)
    Check 'eyes open -> veil clear of view center' ($openUpperOk -and $openLowerOk) "upper=$($v.veilUpperEdge)@$($v.veilUpperAlpha) lower=$($v.veilLowerEdge)@$($v.veilLowerAlpha)"
    Cfg 'VR' 'Squint When Blink Ready' 'false'
    & $Sim invoke 'BlinkCanvasManager.Squint:true'; [Threading.Thread]::Sleep(1500)
    $vn = Status 'veil-nosquint'
    Check 'squint suppressed by default -> veil stays clear' ((($vn.veilUpperAlpha -lt 0.01) -or ($vn.veilUpperEdge -ge 0.45)) -and (($vn.veilLowerAlpha -lt 0.01) -or ($vn.veilLowerEdge -le -0.45))) "upper=$($vn.veilUpperEdge)@$($vn.veilUpperAlpha) lower=$($vn.veilLowerEdge)@$($vn.veilLowerAlpha)"
    & $Sim invoke 'BlinkCanvasManager.Squint:false'; [Threading.Thread]::Sleep(300)
    Cfg 'VR' 'Squint When Blink Ready' 'true'
    & $Sim invoke 'BlinkCanvasManager.Squint:true'; [Threading.Thread]::Sleep(1500)
    $vs = Status 'veil-squint'
    Check 'squint -> lids move in and veil follows' (($vs.veilUpperAlpha -gt 0.5) -and ($vs.veilUpperEdge -lt 0.45) -and ($vs.veilLowerEdge -gt -0.45)) "upper=$($vs.veilUpperEdge)@$($vs.veilUpperAlpha) lower=$($vs.veilLowerEdge)@$($vs.veilLowerAlpha)"
    & $Sim invoke 'BlinkCanvasManager.Squint:false'; [Threading.Thread]::Sleep(1500)
    $vo = Status 'veil-unsquint'
    Check 'unsquint -> veil clears again' ((($vo.veilUpperAlpha -lt 0.01) -or ($vo.veilUpperEdge -ge 0.45)) -and (($vo.veilLowerAlpha -lt 0.01) -or ($vo.veilLowerEdge -le -0.45))) "upper=$($vo.veilUpperEdge) lower=$($vo.veilLowerEdge)"
    Cfg 'VR' 'Squint When Blink Ready' 'false'

    # ---------------------------------------------------------------- vignette, webcam box
    & $Sim vignette 0.6; [Threading.Thread]::Sleep(600)
    $vg = Status 'vignette'
    Check 'vignette drawn across the whole view, not on the flat image' ([bool]$vg.vignetteModelOn -and [bool]$vg.vignetteOverlay -and -not $vg.vignetteFlatActive) "model=$($vg.vignetteModelOn) overlay=$($vg.vignetteOverlay) flat=$($vg.vignetteFlatActive)"
    Cfg 'VR' 'Vignette' 'Original'; [Threading.Thread]::Sleep(400)
    $vf = Status 'vignette-original'
    Check 'Vignette = Original keeps the flat effect' ([bool]$vf.vignetteFlatActive -and -not $vf.vignetteOverlay) "overlay=$($vf.vignetteOverlay) flat=$($vf.vignetteFlatActive)"
    Cfg 'VR' 'Vignette' 'Off'; [Threading.Thread]::Sleep(400)
    $vx = Status 'vignette-off'
    Check 'Vignette = Off draws neither' (-not $vx.vignetteFlatActive -and -not $vx.vignetteOverlay) "overlay=$($vx.vignetteOverlay) flat=$($vx.vignetteFlatActive)"
    Cfg 'VR' 'Vignette' 'Stretched'
    & $Sim vignette restore
    Check 'empty webcam preview hidden' (-not $vx.webcamFeedShown) "shown=$($vx.webcamFeedShown)"

    # ---------------------------------------------------------------- no eye tracking: head aim + controller blink
    Cfg 'VR' 'Show Reticle' 'false'
    Cfg 'Gaze' 'Eye Tracking' 'Off'
    Repeat-Send { & $Sim gaze 0.4 0; & $Sim lid 0.8 } 0.8
    $hg = Status 'head-aim'
    Check 'eye tracking off: aim follows the head, not the eyes' (-not $hg.gazeActive -and (Near $hg.aimAngleFromCenter 0 0.5)) "gazeActive=$($hg.gazeActive) angle=$($hg.aimAngleFromCenter)"
    Check 'eye tracking off: hidden aiming dot stays hidden' (-not $hg.reticleDrawing) "drawing=$($hg.reticleDrawing)"
    Repeat-Send { & $Sim lid 0.05 } 0.5; Repeat-Send { & $Sim lid 0.8 } 0.4
    $hl = Status 'head-lids'
    Check 'eye tracking off: eyelid data ignored' ($hl.pluginBlinks -eq $hg.pluginBlinks) "plugin $($hg.pluginBlinks)->$($hl.pluginBlinks)"
    & $Sim padblink 300; [Threading.Thread]::Sleep(400)
    $hp = Status 'head-pad'
    Check 'controller blink button closes the eyes' ($hp.pluginBlinks -eq $hl.pluginBlinks + 1) "plugin $($hl.pluginBlinks)->$($hp.pluginBlinks)"
    Cfg 'Gaze' 'Eye Tracking' 'Auto'

    # ---------------------------------------------------------------- blinks only: eyes blink, head aims (comfort)
    Cfg 'Gaze' 'Gaze Aiming' 'false'
    Repeat-Send { & $Sim gaze 0.4 0; & $Sim lid 0.8 } 0.8
    $bg = Status 'blinks-only-aim'
    Check 'blinks only: aim follows the head, not the eyes' (-not $bg.gazeActive -and (Near $bg.aimAngleFromCenter 0 0.5)) "gazeActive=$($bg.gazeActive) angle=$($bg.aimAngleFromCenter)"
    Check 'blinks only: hidden aiming dot stays hidden' (-not $bg.reticleDrawing) "drawing=$($bg.reticleDrawing)"
    Repeat-Send { & $Sim gaze 0.4 0; & $Sim lid 0.05 } 0.5; Repeat-Send { & $Sim gaze 0.4 0; & $Sim lid 0.8 } 0.4
    $bl = Status 'blinks-only-lids'
    Check 'blinks only: eyelids still blink' ($bl.pluginBlinks -eq $bg.pluginBlinks + 1) "plugin $($bg.pluginBlinks)->$($bl.pluginBlinks)"
    Cfg 'Gaze' 'Gaze Aiming' 'true'
    Cfg 'VR' 'Show Reticle' 'true'

    # ---------------------------------------------------------------- VR settings from the pause menu
    & $Sim invoke PauseMenuController.Pause; [Threading.Thread]::Sleep(1500)
    & $Sim click 'vr settings'; [Threading.Thread]::Sleep(1000)
    $pv = Status 'pause-vr'
    Check 'VR settings open from the pause menu' ([bool]$pv.vrMenuOpen -and ($pv.vrMenuRows -eq 8)) "open=$($pv.vrMenuOpen) rows=$($pv.vrMenuRows)"
    & $Sim invoke PauseMenuController.Resume; [Threading.Thread]::Sleep(1000)
    $pr = Status 'pause-resume'
    Check 'resuming closes VR settings' (-not $pr.vrMenuOpen) "open=$($pr.vrMenuOpen)"
    & $Sim invoke PauseMenuController.Pause; [Threading.Thread]::Sleep(1500)
    $n0 = (Get-Content $Log).Count
    & $Sim buttons
    # The log is flushed asynchronously: wait for the button list instead of reading it after a fixed delay.
    $pauseButtons = ''
    for ($w = 0; $w -lt 30 -and $pauseButtons -notmatch "\(Quit Button\)"; $w++) {
        [Threading.Thread]::Sleep(200)
        $pauseButtons = (Get-Content $Log | Select-Object -Skip $n0) -join "`n"
    }
    Check 'pause menu whole again after VR settings' (($pauseButtons -match "\(Resume Button\)") -and ($pauseButtons -match "\(Quit Button\)") -and -not ($pauseButtons -match "\(VR World Size Button\)")) ''
    & $Sim invoke PauseMenuController.Resume; [Threading.Thread]::Sleep(1000)

    # ---------------------------------------------------------------- positional tracking
    & $Sim head 0 0 0 0 0 0; & $Sim recenter; [Threading.Thread]::Sleep(300)
    Cfg 'Head Tracking' 'Positional Tracking' 'false'
    & $Sim head 0.2 0 0 0 0 0; [Threading.Thread]::Sleep(400)
    $m = Status 'pos-off'
    Check 'positional off -> camera stays put' (Near $m.childLocalX 0 0.001) "x=$($m.childLocalX)"
    Cfg 'Head Tracking' 'Positional Tracking' 'true'
    [Threading.Thread]::Sleep(400)
    $n = Status 'pos-on'
    Check 'positional on -> 0.2 m lean = 0.2 m' (Near $n.childLocalX 0.2 0.001) "x=$($n.childLocalX)"

    $z = Status 'final'
    Check 'webcam still off at end' ($z.webcamPlaying -eq 0) "webcamPlaying=$($z.webcamPlaying)"
    Check 'no "webcam detection lost" watcher in gameplay' ([int]$z.webcamWatchersSkipped -ge 1) "skipped=$($z.webcamWatchersSkipped)"
}
catch {
    Check 'test run aborted' $false $_.Exception.Message
}
finally {
    # Restore user settings (live + on disk).
    if (Test-Path $CfgBackup) {
        if (Get-Process 'Before Your Eyes' -ErrorAction SilentlyContinue) {
            $orig = Get-Content $CfgBackup
            foreach ($pair in @(@('Head Tracking', 'Simulate Head'), @('Head Tracking', 'Positional Tracking'), @('Debug', 'Test Mode'), @('VR', 'Show Reticle'), @('VR', 'Squint When Blink Ready'),
                               @('VR', 'Eye Separation Scale'), @('VR', 'Vignette'), @('Gaze', 'Eye Tracking'), @('Gaze', 'Gaze Aiming'),
                               @('VR', 'Render Scale'), @('VR', 'Menu Render Scale'), @('VR', 'Performance Mode'))) {
                $line = $orig | Where-Object { $_ -match "^$([regex]::Escape($pair[1])) = " } | Select-Object -First 1
                if ($line) { Cfg $pair[0] $pair[1] ($line -split ' = ', 2)[1] }
            }
            & $Sim head 0 0 0 0 0 0
        }
        [Threading.Thread]::Sleep(500)
        Copy-Item $CfgBackup $Cfg -Force
    }
    if ($CloseGame) { Stop-Process -Name 'Before Your Eyes' -ErrorAction SilentlyContinue }

    $failed = @($Results | Where-Object Result -eq 'FAIL').Count
    $summary = "{0} passed, {1} failed ({2})" -f ($Results.Count - $failed), $failed, (Get-Date -Format 's')
    ($Results | Format-Table -AutoSize | Out-String -Width 250) + $summary | Set-Content (Join-Path $PSScriptRoot 'last_test_results.txt')
    Write-Host $summary -ForegroundColor $(if ($failed) { 'Red' } else { 'Green' })
    exit [int]($failed -gt 0)
}

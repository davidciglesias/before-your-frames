<#
Test driver for BYEFrameBlink: sends fake eyelid / head-pose OSC to the plugin (UDP 127.0.0.1:9000).

  bye_sim.ps1 lid 0.1            both eyes 0.1 open (0 = shut, 1 = open)
  bye_sim.ps1 lid 0.1 0.9        left, right
  bye_sim.ps1 blink [ms]         one blink (default 150 ms closed)
  bye_sim.ps1 hold [seconds]     hold eyes shut (default 3)
  bye_sim.ps1 ft 0.2             frameeyeosc-style /avatar/parameters/FT/v2/EyeLidLeft|Right
  bye_sim.ps1 ftgaze x y         frameeyeosc-style gaze /avatar/parameters/FT/v2/EyeX|EyeY
  bye_sim.ps1 steamvr 0.2        Steam Link-style /avatar/parameters/LeftEyeLid|RightEyeLid (value given as openness; sent inverted)
  bye_sim.ps1 steamvrgaze x y    Steam Link-style /avatar/parameters/LeftEyeX|Y, RightEyeX|Y (y given +up; sent +down)
  bye_sim.ps1 babble 0.2         Babble-style /LeftEyeLid|/RightEyeLid to port 8888
  bye_sim.ps1 vrcft 0.2          VRCFaceTracking-style /avatar/parameters/v2/EyeOpenLeft|Right
  bye_sim.ps1 stream [seconds]   open eyes at 60 Hz with a blink every ~3 s
  bye_sim.ps1 head x y z pitch yaw roll
  bye_sim.ps1 lean               scripted lean test: +-0.2 m per axis, then yaw 90
  bye_sim.ps1 recenter
  bye_sim.ps1 shot [name]        save left-eye screenshot to BepInEx/BYEFrameBlink_shots/<name>.png
  bye_sim.ps1 freeze [on|off]    freeze/unfreeze (same as F5); no arg toggles
  bye_sim.ps1 buttons            log active UI buttons to the BepInEx log
  bye_sim.ps1 click <label>      press a UI button by label
  bye_sim.ps1 invoke Type.Method call a parameterless game method
  bye_sim.ps1 scenes             log loaded scenes
  bye_sim.ps1 addrs              log every OSC address received and its last value
  bye_sim.ps1 inspect <Type|GameObject>   log hierarchy/canvas/placement
  bye_sim.ps1 cfg "guid|Section|Key|value" set any plugin's config live
  bye_sim.ps1 gaze x y           fake gaze (1 = 45 deg, +x right, +y up)
  bye_sim.ps1 controllers [secs] log VR controller button changes for a while (default 20s)
  bye_sim.ps1 vignette <0..1|restore>  force the current vignette on (for testing), or undo
  bye_sim.ps1 padblink [ms]      hold the controller Blink Button for ms (default 150)
  bye_sim.ps1 perf <auto|on|off> set Performance Mode (on-headset settings) live
  bye_sim.ps1 msaa <0|2|4|8>     set Performance MSAA live
  bye_sim.ps1 eyeshot [name]     capture exactly what the left eye sees (world + overlays), edge to edge
  bye_sim.ps1 toggle <name> 0|1  disable/enable the cameras and renderers on objects with that name
  bye_sim.ps1 viewlayers         log every visible canvas image, camera-attached renderer and extra-camera object
#>
param([Parameter(Position = 0)][string]$Cmd, [Parameter(Position = 1, ValueFromRemainingArguments = $true)][string[]]$Rest = @())
$Values = [double[]]@($Rest | Where-Object { $null -ne ($_ -as [double]) } | ForEach-Object { [double]$_ })

$Port = 9000
$udp = New-Object System.Net.Sockets.UdpClient
$udp.Connect('127.0.0.1', $Port)

function Pad([byte[]]$b) {
    $len = $b.Length + 1; $len += (4 - $len % 4) % 4
    $out = New-Object byte[] $len; [Array]::Copy($b, $out, $b.Length); return , $out
}

function Send([string]$address, [double[]]$floats = @(), $client = $udp) {
    $ms = New-Object System.IO.MemoryStream
    $a = Pad ([Text.Encoding]::ASCII.GetBytes($address)); $ms.Write($a, 0, $a.Length)
    $t = Pad ([Text.Encoding]::ASCII.GetBytes(',' + ('f' * $floats.Count))); $ms.Write($t, 0, $t.Length)
    foreach ($f in $floats) { $fb = [BitConverter]::GetBytes([single]$f); [Array]::Reverse($fb); $ms.Write($fb, 0, 4) }
    $bytes = $ms.ToArray(); [void]$client.Send($bytes, $bytes.Length)
}

# Babble (Baballonia) sends to its own port.
function SendBabble([string]$address, [double]$v) {
    $b = New-Object System.Net.Sockets.UdpClient; $b.Connect('127.0.0.1', 8888)
    Send $address @($v) $b; $b.Close()
}

function SendString([string]$address, [string]$value) {
    $ms = New-Object System.IO.MemoryStream
    $parts = @((Pad ([Text.Encoding]::ASCII.GetBytes($address))))
    if ($null -ne $value -and $value -ne '') { $parts += , (Pad ([Text.Encoding]::ASCII.GetBytes(',s'))); $parts += , (Pad ([Text.Encoding]::UTF8.GetBytes($value))) }
    else { $parts += , (Pad ([Text.Encoding]::ASCII.GetBytes(','))) }
    foreach ($p in $parts) { $ms.Write($p, 0, $p.Length) }
    $bytes = $ms.ToArray(); [void]$udp.Send($bytes, $bytes.Length)
}

function Lid([double]$l, [double]$r = [double]::NaN) { if ([double]::IsNaN($r)) { $r = $l }; Send '/bye/lid' @($l, $r) }

function HoldValue([double]$v, [double]$seconds) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $seconds) { Lid $v; [Threading.Thread]::Sleep(16) }
}

function Head([double[]]$p) { Send '/bye/head' $p }

switch ($Cmd) {
    'lid'      { if ($Values.Count -gt 1) { Lid $Values[0] $Values[1] } else { Lid $Values[0] } }
    'ft'       { Send '/avatar/parameters/FT/v2/EyeLidLeft' @($Values[0]); Send '/avatar/parameters/FT/v2/EyeLidRight' @($Values[0]) }
    'ftgaze'   { Send '/avatar/parameters/FT/v2/EyeX' @($Values[0]); Send '/avatar/parameters/FT/v2/EyeY' @($Values[1]) }
    'steamvr'  { Send '/avatar/parameters/LeftEyeLid' @(1 - $Values[0]); Send '/avatar/parameters/RightEyeLid' @(1 - $Values[0]) }
    'steamvrgaze' {
        Send '/avatar/parameters/LeftEyeX' @($Values[0]); Send '/avatar/parameters/RightEyeX' @($Values[0])
        Send '/avatar/parameters/LeftEyeY' @(-$Values[1]); Send '/avatar/parameters/RightEyeY' @(-$Values[1]) # Steam Link: + is down
    }
    'babble'   { SendBabble '/LeftEyeLid' $Values[0]; SendBabble '/RightEyeLid' $Values[0] }
    'vrcft'    { Send '/avatar/parameters/v2/EyeOpenLeft' @($Values[0]); Send '/avatar/parameters/v2/EyeOpenRight' @($Values[0]) }
    'blink'    { $ms = if ($Values.Count) { $Values[0] } else { 150 }; HoldValue 0.8 0.3; HoldValue 0.05 ($ms / 1000); HoldValue 0.8 0.5 }
    'hold'     { $s = if ($Values.Count) { $Values[0] } else { 3 }; HoldValue 0.8 0.3; HoldValue 0.05 $s; HoldValue 0.8 0.5 }
    'stream'   {
        $total = if ($Values.Count) { $Values[0] } else { 10 }
        $sw = [Diagnostics.Stopwatch]::StartNew(); $next = 3
        while ($sw.Elapsed.TotalSeconds -lt $total) {
            if ($sw.Elapsed.TotalSeconds -ge $next) { HoldValue 0.05 0.15; $next += 3 }
            Lid 0.8; [Threading.Thread]::Sleep(16)
        }
    }
    'head'     { Head $Values }
    'lean'     {
        $steps = @(@(0,0,0,0,0,0), @(0.2,0,0,0,0,0), @(-0.2,0,0,0,0,0), @(0,0.2,0,0,0,0), @(0,-0.2,0,0,0,0),
                   @(0,0,0.2,0,0,0), @(0,0,-0.2,0,0,0), @(0,0,0,0,90,0), @(0.2,0,0,0,90,0), @(0,0,0,0,0,0))
        foreach ($s in $steps) { Write-Output ("head " + ($s -join ' ')); Head $s; [Threading.Thread]::Sleep(1000) }
    }
    'recenter' { Send '/bye/recenter' }
    'shot'     { SendString '/bye/shot' ($Rest -join ' ') }
    'overlayshot' { SendString '/bye/overlayshot' ($Rest -join ' ') }
    'followers' { SendString '/bye/followers' ($Rest -join ' ') }
    'buttons'  { SendString '/bye/buttons' '' }
    'freeze'   { SendString '/bye/freeze' ($Rest -join ' ') }
    'vfcheck'  { SendString '/bye/vfcheck' '' }
    'viewsync' { SendString '/bye/viewsync' '' }
    'lookat'   { SendString '/bye/lookat' ($Rest -join ' ') }
    'yaw'      { SendString '/bye/yaw' ($Rest -join ' ') }
    'scenes'   { SendString '/bye/scenes' '' }
    'addrs'    { SendString '/bye/addrs' '' }
    'graphics' { SendString '/bye/graphics' ($Rest -join ' ') }
    'veiltest' { SendString '/bye/veiltest' '' }
    'goto'     { SendString '/bye/goto' ($Rest -join ' ') }
    'toast'    { SendString '/bye/toast' ($Rest -join ' ') }
    'renderers' { SendString '/bye/renderers' ($Rest -join ' ') }
    'fields'   { SendString '/bye/fields' ($Rest -join ' ') }
    'rayall'   { SendString '/bye/rayall' '' }
    'ias'      { SendString '/bye/ias' '' }
    'gazetargets' { SendString '/bye/gazetargets' '' }
    'menucands' { SendString '/bye/menucands' '' }
    'aimat'    { SendString '/bye/aimat' ($Rest -join ' ') }
    'menuaim'  { SendString '/bye/menuaim' ($Rest -join ' ') }
    'autoplay' { SendString '/bye/autoplay' ($Rest -join ' ') }
    'status'   { SendString '/bye/status' ($Rest -join ' ') }
    'inspect'  { SendString '/bye/inspect' ($Rest -join ' ') }
    'cfg'      { SendString '/bye/cfg' ($Rest -join ' ') }
    'gaze'     { Send '/bye/gaze' @($Values[0], $Values[1]) }
    'click'    { SendString '/bye/click' ($Rest -join ' ') }
    'invoke'   { SendString '/bye/invoke' ($Rest -join ' ') }
    'controllers' { SendString '/bye/controllers' ($Rest -join ' ') }
    'vignette' { SendString '/bye/vignette' ($Rest -join ' ') }
    'cameras'  { SendString '/bye/cameras' '' }
    'pp'       { SendString '/bye/pp' ($Rest -join ' ') }
    'perf'     { SendString '/bye/perf' ($Rest -join ' ') }
    'msaa'     { SendString '/bye/msaa' ($Rest -join ' ') }
    'eyeshot'  { SendString '/bye/eyeshot' ($Rest -join ' ') }
    'toggle'   { SendString '/bye/toggle' ($Rest -join ' ') }
    'viewlayers' { SendString '/bye/viewlayers' '' }
    'padblink' { $ms = if ($Values.Count) { $Values[0] } else { 150 }; SendString '/bye/padblink' '1'; [Threading.Thread]::Sleep([int]$ms); SendString '/bye/padblink' '0' }
    default    { Get-Help $MyInvocation.MyCommand.Path -Full | Out-String | Write-Output }
}
$udp.Close()

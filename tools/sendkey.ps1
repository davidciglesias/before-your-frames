# Focuses the game window and taps/holds a key or clicks the mouse.
#   sendkey.ps1 -Key F8            tap F8
#   sendkey.ps1 -Key F9 -HoldMs 2000
#   sendkey.ps1 -Click             left click at the current cursor position
#   sendkey.ps1 -Click -X 600 -Y 400   move cursor to screen coords first
param([string]$Key, [int]$HoldMs = 60, [switch]$Click, [int]$X = -1, [int]$Y = -1, [string]$Proc = 'Before Your Eyes')
Add-Type @"
using System; using System.Runtime.InteropServices;
public class K {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint type);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
}
"@
$p = Get-Process -Name $Proc -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { Write-Output "process not found"; exit 1 }
[K]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 150
if ($Click) {
  if ($X -ge 0) { [K]::SetCursorPos($X, $Y) | Out-Null; Start-Sleep -Milliseconds 100 }
  [K]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds $HoldMs
  [K]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero); exit 0
}
Add-Type -AssemblyName System.Windows.Forms
$vk = [byte]([int][System.Windows.Forms.Keys]$Key)
$scan = [byte][K]::MapVirtualKey($vk, 0)
[K]::keybd_event($vk, $scan, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds $HoldMs
[K]::keybd_event($vk, $scan, 2, [UIntPtr]::Zero)

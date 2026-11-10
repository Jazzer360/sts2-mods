param([string]$Key = "", [string]$Out = "shot.png", [int]$ClickX = -1, [int]$ClickY = -1, [int]$Wait = 800)
# In-game test helper (Windows): focuses the running game, optionally sends a key (F8/F9/F10/ESC/ENTER,
# with scan codes so Godot sees them) or a left click at window-relative full-resolution coordinates,
# waits, then saves a half-size screenshot of the game window.
# Usage: ./scripts/game.ps1 -Key F10 -Out shot.png   |   ./scripts/game.ps1 -ClickX 2012 -ClickY 102
# Start the game first: Start-Process "steam://rungameid/2868840"
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
}
"@
$p = Get-Process SlayTheSpire2 | Select-Object -First 1
$h = $p.MainWindowHandle
[W]::ShowWindow($h, 9) | Out-Null
[W]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 300
$r = New-Object W+RECT; [W]::GetClientRect($h, [ref]$r) | Out-Null
$pt = New-Object W+POINT; [W]::ClientToScreen($h, [ref]$pt) | Out-Null
if ($Key -ne "") {
  $vk = switch ($Key) { "F10" {0x79} "F8" {0x77} "F9" {0x78} "ESC" {0x1B} "ENTER" {0x0D} default {[byte][char]$Key} }
  $sc = switch ($Key) { "F10" {0x44} "F8" {0x42} "F9" {0x43} "ESC" {0x01} "ENTER" {0x1C} default {0} }
  [W]::keybd_event([byte]$vk, [byte]$sc, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 80
  [W]::keybd_event([byte]$vk, [byte]$sc, 2, [UIntPtr]::Zero)
}
if ($ClickX -ge 0) {
  [W]::SetCursorPos($pt.X + $ClickX, $pt.Y + $ClickY) | Out-Null; Start-Sleep -Milliseconds 150
  [W]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [W]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
}
Start-Sleep -Milliseconds $Wait
$w = $r.R - $r.L; $hh = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $hh
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($pt.X, $pt.Y, 0, 0, $bmp.Size)
$small = New-Object System.Drawing.Bitmap $bmp, ([int]($w/2)), ([int]($hh/2))
$small.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
"client ${w}x${hh} at $($pt.X),$($pt.Y)"

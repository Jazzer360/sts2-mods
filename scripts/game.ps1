param([string]$Key = "", [string]$Out = "shot.png", [int]$ClickX = -1, [int]$ClickY = -1, [int]$Wait = 800, [string]$Text = "", [int]$HoverX = -1, [int]$HoverY = -1, [string]$Seq = "")
# In-game test helper (Windows): focuses the running game, optionally sends a key (F8/F9/F10/ESC/ENTER,
# with scan codes so Godot sees them) or a left click at window-relative full-resolution coordinates,
# waits, then saves a half-size screenshot of the game window.
# -Text types characters (console commands; Key BACKQUOTE opens the console), -HoverX/-HoverY moves the
# mouse without clicking (to read a card's tabs mid-play). Order: Key, Text, click, hover.
# -Seq runs steps in one go (fast enough to queue card plays), separated by ";": k:<char> presses a key
# (1-0 pick up a hand card), c clicks where the mouse is, m:x,y moves the mouse, w:ms waits,
# s:path saves a screenshot. Every step checks the game still has focus.
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
  [DllImport("user32.dll")] public static extern short VkKeyScan(char c);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint type);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
}
"@
$p = Get-Process SlayTheSpire2 | Select-Object -First 1
$h = $p.MainWindowHandle
[W]::ShowWindow($h, 9) | Out-Null
[W]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 300
# Safety: input only ever goes to the game. Stop (sending nothing more) the moment it isn't the foreground window.
function Assert-GameFocused { if ([W]::GetForegroundWindow() -ne $h) { Write-Error "the game isn't the foreground window: stopped, nothing more sent"; exit 2 } }
if (($Key -ne "") -or ($Text -ne "") -or ($ClickX -ge 0) -or ($HoverX -ge 0)) { Assert-GameFocused }
$r = New-Object W+RECT; [W]::GetClientRect($h, [ref]$r) | Out-Null
$pt = New-Object W+POINT; [W]::ClientToScreen($h, [ref]$pt) | Out-Null
if ($Key -ne "") {
  $vk = switch ($Key) { "F10" {0x79} "F8" {0x77} "F9" {0x78} "ESC" {0x1B} "ENTER" {0x0D} "BACKQUOTE" {0xC0} "TAB" {0x09} default {[byte][char]$Key} }
  $sc = switch ($Key) { "F10" {0x44} "F8" {0x42} "F9" {0x43} "ESC" {0x01} "ENTER" {0x1C} "BACKQUOTE" {0x29} "TAB" {0x0F} default {0} }
  [W]::keybd_event([byte]$vk, [byte]$sc, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 80
  [W]::keybd_event([byte]$vk, [byte]$sc, 2, [UIntPtr]::Zero)
}
if ($Text -ne "") {
  foreach ($c in $Text.ToCharArray()) {
    Assert-GameFocused
    $k = [W]::VkKeyScan($c); $v = $k -band 0xFF; $shift = ($k -band 0x100) -ne 0
    $scan = [W]::MapVirtualKey([uint32]$v, 0)
    if ($shift) { [W]::keybd_event(0x10, 0x2A, 0, [UIntPtr]::Zero) }
    [W]::keybd_event([byte]$v, [byte]$scan, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 25
    [W]::keybd_event([byte]$v, [byte]$scan, 2, [UIntPtr]::Zero)
    if ($shift) { [W]::keybd_event(0x10, 0x2A, 2, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 25
  }
}
if ($ClickX -ge 0) {
  Assert-GameFocused
  [W]::SetCursorPos($pt.X + $ClickX, $pt.Y + $ClickY) | Out-Null; Start-Sleep -Milliseconds 150
  [W]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [W]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
}
if ($HoverX -ge 0) { [W]::SetCursorPos($pt.X + $HoverX, $pt.Y + $HoverY) | Out-Null }
function Save-Shot([string]$path) {
  $ww = $r.R - $r.L; $hh2 = $r.B - $r.T
  $b2 = New-Object System.Drawing.Bitmap $ww, $hh2
  $g2 = [System.Drawing.Graphics]::FromImage($b2)
  $g2.CopyFromScreen($pt.X, $pt.Y, 0, 0, $b2.Size)
  (New-Object System.Drawing.Bitmap $b2, ([int]($ww/2)), ([int]($hh2/2))).Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
}
if ($Seq -ne "") {
  foreach ($step in $Seq.Split(";")) {
    $t = $step.Trim(); if ($t -eq "") { continue }
    if ($t.StartsWith("w:")) { Start-Sleep -Milliseconds ([int]$t.Substring(2)); continue }
    if ($t.StartsWith("s:")) { Save-Shot $t.Substring(2); continue }
    Assert-GameFocused
    if ($t.StartsWith("k:")) {
      $k = [W]::VkKeyScan($t[2]); $v = $k -band 0xFF; $scan = [W]::MapVirtualKey([uint32]$v, 0)
      [W]::keybd_event([byte]$v, [byte]$scan, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 20
      [W]::keybd_event([byte]$v, [byte]$scan, 2, [UIntPtr]::Zero)
    } elseif ($t -eq "c") {
      [W]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 20; [W]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    } elseif ($t.StartsWith("m:")) {
      $xy = $t.Substring(2).Split(","); [W]::SetCursorPos($pt.X + [int]$xy[0], $pt.Y + [int]$xy[1]) | Out-Null
    }
  }
}
Start-Sleep -Milliseconds $Wait
$w = $r.R - $r.L; $hh = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $hh
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($pt.X, $pt.Y, 0, 0, $bmp.Size)
$small = New-Object System.Drawing.Bitmap $bmp, ([int]($w/2)), ([int]($hh/2))
$small.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
"client ${w}x${hh} at $($pt.X),$($pt.Y)"

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class WShot2 {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lp);
  public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
'@

$script:target = (Get-Process SpinePet).Id
$cb = {
  param($h, $lp)
  $wp = 0
  [WShot2]::GetWindowThreadProcessId($h, [ref]$wp) | Out-Null
  if ($wp -eq $script:target) {
    $sb = New-Object System.Text.StringBuilder 64
    [WShot2]::GetWindowText($h, $sb, 64) | Out-Null
    if ($sb.ToString() -eq 'SpinePet') {
      $r = New-Object WShot2+RECT
      [WShot2]::GetWindowRect($h, [ref]$r) | Out-Null
      $w = $r.Right - $r.Left
      $ht = $r.Bottom - $r.Top
      Write-Output ("CONFIG {0} x {1}" -f $w, $ht)
      [WShot2]::SetForegroundWindow($h) | Out-Null
      Start-Sleep -Milliseconds 600
      $bmp = New-Object System.Drawing.Bitmap $w, $ht
      $g = [System.Drawing.Graphics]::FromImage($bmp)
      $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
      $bmp.Save('d:\SpineTools\_cfg.png')
      Write-Output 'saved'
    }
  }
  return $true
}
[WShot2]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null

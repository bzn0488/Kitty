Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class WinFocus {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint procId);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lp);
}
"@

$godot = Get-Process -Name "Godot*" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $godot) { Write-Host "NO_GODOT_PROCESS"; exit }
$godotPid = $godot.Id
Write-Host "Godot PID: $godotPid"

$found = @()
$cb = [WinFocus+EnumWindowsProc]{
    param($h, $l)
    [uint32]$procId = 0
    [WinFocus]::GetWindowThreadProcessId($h, [ref]$procId) | Out-Null
    if ($procId -eq $godotPid -and [WinFocus]::IsWindowVisible($h)) {
        $sb = New-Object System.Text.StringBuilder 256
        [WinFocus]::GetWindowText($h, $sb, 256) | Out-Null
        $script:found += [PSCustomObject]@{ hwnd = $h; title = $sb.ToString() }
    }
    return $true
}
[WinFocus]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null

$game = $found | Where-Object { $_.title -match 'GuandanKitty' } | Select-Object -First 1
if ($game) {
    [WinFocus]::ShowWindow($game.hwnd, 9) | Out-Null
    Start-Sleep -Milliseconds 200
    [WinFocus]::SetForegroundWindow($game.hwnd) | Out-Null
    Write-Host "FOCUSED: $($game.title) (hwnd=$($game.hwnd))"
} else {
    Write-Host "NO_GAME_WINDOW_FOUND"
}

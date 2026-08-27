param(
    [string]$OutputPath = "$PSScriptRoot\astraltft-preflight.json"
)

# AstralTFT diagnostic helper for the first real Windows benchmark pass.
# It intentionally does not read game memory, inject code, or interact with Vanguard.
$ErrorActionPreference = 'Stop'

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class WindowProbe {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextLengthW(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
}
"@

$os = Get-CimInstance Win32_OperatingSystem
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$system = Get-CimInstance Win32_ComputerSystem
$gpus = @(Get-CimInstance Win32_VideoController | ForEach-Object {
    [pscustomobject]@{
        name = $_.Name
        driverVersion = $_.DriverVersion
        currentResolution = if ($_.CurrentHorizontalResolution -and $_.CurrentVerticalResolution) {
            "$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)"
        } else { $null }
    }
})

$processes = @(Get-Process -ErrorAction SilentlyContinue |
    Where-Object { $_.ProcessName -match 'League|TFT|Riot|Unreal' -or $_.MainWindowTitle -match 'League|TFT|Teamfight|Riot' } |
    ForEach-Object {
        [pscustomobject]@{
            processName = $_.ProcessName
            pid = $_.Id
            cpuSeconds = $_.CPU
            workingSetBytes = $_.WorkingSet64
            mainWindowTitle = $_.MainWindowTitle
        }
    })

$windows = [System.Collections.Generic.List[object]]::new()
[WindowProbe]::EnumWindows({
    param($hwnd, $lParam)
    if (-not [WindowProbe]::IsWindowVisible($hwnd)) { return $true }

    $length = [WindowProbe]::GetWindowTextLengthW($hwnd)
    if ($length -le 0) { return $true }
    $sb = [System.Text.StringBuilder]::new($length + 1)
    [void][WindowProbe]::GetWindowTextW($hwnd, $sb, $sb.Capacity)
    $title = $sb.ToString()

    [uint32]$pid = 0
    [void][WindowProbe]::GetWindowThreadProcessId($hwnd, [ref]$pid)
    try { $process = Get-Process -Id $pid -ErrorAction Stop } catch { return $true }

    $rect = New-Object WindowProbe+RECT
    if (-not [WindowProbe]::GetClientRect($hwnd, [ref]$rect)) { return $true }
    $width = [Math]::Max(0, $rect.Right - $rect.Left)
    $height = [Math]::Max(0, $rect.Bottom - $rect.Top)
    $looksRelevant = $title -match 'League|TFT|Teamfight|Riot' -or $process.ProcessName -match 'League|TFT|Riot|Unreal'
    if (-not $looksRelevant -and ($width -lt 1280 -or $height -lt 720)) { return $true }

    $windows.Add([pscustomobject]@{
        processName = $process.ProcessName
        pid = $pid
        hwnd = ('0x{0:X}' -f $hwnd.ToInt64())
        clientWidth = $width
        clientHeight = $height
        minimized = [WindowProbe]::IsIconic($hwnd)
        title = $title
    })
    return $true
}, [IntPtr]::Zero) | Out-Null

$report = [pscustomobject]@{
    schemaVersion = 1
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    windows = [pscustomobject]@{
        caption = $os.Caption
        version = $os.Version
        buildNumber = $os.BuildNumber
    }
    cpu = [pscustomobject]@{
        name = $cpu.Name
        logicalProcessors = $cpu.NumberOfLogicalProcessors
    }
    ramBytes = [int64]$system.TotalPhysicalMemory
    gpus = $gpus
    candidateProcesses = $processes
    candidateWindows = @($windows)
    privacy = 'Hardware/window metadata only. No TFT process memory is read.'
}

$report | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 $OutputPath

Write-Host "AstralTFT preflight"
Write-Host "Windows:" $os.Caption $os.Version "Build" $os.BuildNumber
Write-Host "CPU:" $cpu.Name
Write-Host "RAM GB:" ([math]::Round($system.TotalPhysicalMemory / 1GB, 1))
Write-Host "Candidate windows:" $windows.Count
$windows | Sort-Object processName, pid | Format-Table processName, pid, hwnd, clientWidth, clientHeight, minimized, title -AutoSize
Write-Host "`nSaved machine-readable report to: $OutputPath"
Write-Host "This report contains hardware/window metadata only. It does not read TFT process memory."

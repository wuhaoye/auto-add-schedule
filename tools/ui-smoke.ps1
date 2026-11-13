param(
    [string]$Exe = "src/AutoAddSchedule/bin/Debug/net10.0-windows/AutoAddSchedule.exe",
    [string]$OutDir = ".tmp/ui",
    [switch]$DoCapture,
    [switch]$ConfirmCapture,
    [switch]$KeepRunning
)

# 开发期 GUI 冒烟脚本：
#   启动程序 → 截图主界面 → （可选）发送 Ctrl+Alt+\ 并模拟拖选 → 截图遮罩/工具条
#   →（可选）按 Enter 触发真实流程 → 截图结果 → 关闭程序。
# 仅用于人工/自动视觉验证，不参与程序运行。
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class UiSmokeNative
{
    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);
}
"@

$VK_CONTROL = 0x11
$VK_MENU = 0x12
$VK_OEM_5 = 0xDC
$VK_RETURN = 0x0D
$VK_ESCAPE = 0x1B
$KEYEVENTF_KEYUP = 0x0002
$MOUSEEVENTF_LEFTDOWN = 0x0002
$MOUSEEVENTF_LEFTUP = 0x0004

function Send-KeyDown([byte]$vk) { [UiSmokeNative]::keybd_event($vk, 0, 0, [UIntPtr]::Zero) }
function Send-KeyUp([byte]$vk) { [UiSmokeNative]::keybd_event($vk, 0, $KEYEVENTF_KEYUP, [UIntPtr]::Zero) }

function Send-HotkeyCapture {
    Send-KeyDown $VK_CONTROL
    Send-KeyDown $VK_MENU
    Send-KeyDown $VK_OEM_5
    Start-Sleep -Milliseconds 60
    Send-KeyUp $VK_OEM_5
    Send-KeyUp $VK_MENU
    Send-KeyUp $VK_CONTROL
}

function Send-Drag([int]$fromX, [int]$fromY, [int]$toX, [int]$toY) {
    [UiSmokeNative]::SetCursorPos($fromX, $fromY) | Out-Null
    Start-Sleep -Milliseconds 120
    [UiSmokeNative]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    $steps = 12
    for ($i = 1; $i -le $steps; $i++) {
        $x = [int]($fromX + ($toX - $fromX) * $i / $steps)
        $y = [int]($fromY + ($toY - $fromY) * $i / $steps)
        [UiSmokeNative]::SetCursorPos($x, $y) | Out-Null
        Start-Sleep -Milliseconds 30
    }
    [UiSmokeNative]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
}

function Save-Screenshot([string]$name) {
    $fullDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutDir))
    if (-not (Test-Path $fullDir)) { New-Item -ItemType Directory -Force -Path $fullDir | Out-Null }
    $path = Join-Path $fullDir $name
    & (Join-Path $PSScriptRoot "screenshot.ps1") -OutPath (Join-Path $OutDir $name) | Out-Null
    Write-Output ("截图：" + $path)
}

Get-Process -Name "AutoAddSchedule" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

$exePath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Exe))
if (-not (Test-Path $exePath)) {
    throw "找不到可执行文件：$exePath（请先 pwsh tools/build.ps1）"
}

Start-Process -FilePath $exePath -ArgumentList @("--page", "calendar") | Out-Null
Start-Sleep -Seconds 4
Save-Screenshot "01-main.png"

if ($DoCapture) {
    Send-HotkeyCapture
    Start-Sleep -Seconds 2
    Save-Screenshot "02-overlay.png"

    Send-Drag 200 200 900 500
    Start-Sleep -Milliseconds 900
    Save-Screenshot "03-toolbar.png"

    if ($ConfirmCapture) {
        Send-KeyDown $VK_RETURN
        Send-KeyUp $VK_RETURN
        Start-Sleep -Seconds 20
    }
    else {
        Send-KeyDown $VK_ESCAPE
        Send-KeyUp $VK_ESCAPE
        Start-Sleep -Milliseconds 800
    }

    Save-Screenshot "04-after.png"
}

if (-not $KeepRunning) {
    Get-Process -Name "AutoAddSchedule" -ErrorAction SilentlyContinue | Stop-Process -Force
}

Write-Output "UI 冒烟脚本执行完成。"

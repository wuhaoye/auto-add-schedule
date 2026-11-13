param(
    [string]$OutPath = ".tmp/screen.png"
)

# 手动/自动视觉检查用：抓取当前桌面（所有显示器）到指定 PNG。
# 注意：本机为 200% DPI，必须先声明 Per-Monitor V2 感知，否则只能截到屏幕左上角。
# 仅开发调试使用，不参与程序运行。
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class ScreenshotDpiNative
{
    // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
    [DllImport("user32.dll")]
    public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);
}
"@

[ScreenshotDpiNative]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
$bitmap = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($bounds.X, $bounds.Y, 0, 0, $bitmap.Size)

$fullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutPath))
$directory = Split-Path -Parent $fullPath
if (-not (Test-Path $directory)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

$bitmap.Save($fullPath, [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()

Write-Output ("桌面截图已保存：" + $fullPath + "（" + $bounds.Width + "x" + $bounds.Height + "）")

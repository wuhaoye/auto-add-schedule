param(
    [string]$OutPath = "src/AutoAddSchedule/Assets/app.ico"
)

# 生成应用图标（蓝色圆角方块 + 白色日历），输出多尺寸 ICO。
# 仅在需要更新图标时手动运行：pwsh tools/make-icon.ps1
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

Add-Type -AssemblyName System.Drawing

function New-RoundedPath([System.Drawing.RectangleF]$rect, [float]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $rect = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
    $radius = [Math]::Max(2.0, $size * 0.2)
    $path = New-RoundedPath $rect $radius
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)),
        (New-Object System.Drawing.PointF($size, $size)),
        [System.Drawing.Color]::FromArgb(255, 61, 126, 255),
        [System.Drawing.Color]::FromArgb(255, 26, 76, 190))
    $g.FillPath($brush, $path)

    # 白色日历主体
    $calX = $size * 0.22
    $calY = $size * 0.26
    $calW = $size * 0.56
    $calH = $size * 0.5
    $calRect = New-Object System.Drawing.RectangleF($calX, $calY, $calW, $calH)
    $calRadius = [Math]::Max(1.0, $size * 0.06)
    $calPath = New-RoundedPath $calRect $calRadius
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillPath($white, $calPath)

    # 顶部蓝色条（日历头部）
    $headerH = [Math]::Max(1.0, $size * 0.12)
    $headerRect = New-Object System.Drawing.RectangleF($calX, $calY, $calW, $headerH)
    $headerPath = New-RoundedPath $headerRect $calRadius
    $headerBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 47, 111, 237))
    $g.FillPath($headerBrush, $headerPath)
    $g.FillRectangle($headerBrush, $calX, $calY + $headerH / 2, $calW, $headerH / 2)

    # 两个挂环
    $ringW = [Math]::Max(1.0, $size * 0.06)
    $ringH = [Math]::Max(1.5, $size * 0.1)
    $ringBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillRectangle($ringBrush, $calX + $calW * 0.2, $calY - $ringH * 0.45, $ringW, $ringH)
    $g.FillRectangle($ringBrush, $calX + $calW * 0.7, $calY - $ringH * 0.45, $ringW, $ringH)

    # 日历内的对勾（表示已添加日程）
    if ($size -ge 24) {
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 47, 111, 237), [Math]::Max(1.2, $size * 0.075))
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        [System.Drawing.PointF[]]$points = @(
            (New-Object System.Drawing.PointF(($calX + $calW * 0.28), ($calY + $calH * 0.62))),
            (New-Object System.Drawing.PointF(($calX + $calW * 0.43), ($calY + $calH * 0.76))),
            (New-Object System.Drawing.PointF(($calX + $calW * 0.74), ($calY + $calH * 0.4)))
        )
        $g.DrawLines($pen, $points)
        $pen.Dispose()
    }

    $white.Dispose(); $headerBrush.Dispose(); $path.Dispose(); $calPath.Dispose(); $headerPath.Dispose(); $g.Dispose()
    return $bmp
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = @()
foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $images += [pscustomobject]@{ Size = $size; Bytes = $ms.ToArray() }
    $ms.Dispose()
}

$outStream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($outStream)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$images.Count)

$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $writer.Write([byte]$dim)
    $writer.Write([byte]$dim)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$img.Bytes.Length)
    $writer.Write([UInt32]$offset)
    $offset += $img.Bytes.Length
}

foreach ($img in $images) {
    $writer.Write($img.Bytes)
}

$writer.Flush()
$fullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutPath))
$directory = Split-Path -Parent $fullPath
if (-not (Test-Path $directory)) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
[System.IO.File]::WriteAllBytes($fullPath, $outStream.ToArray())
$writer.Dispose(); $outStream.Dispose()

Write-Output ("图标已生成：" + $fullPath + "（" + (Get-Item $fullPath).Length + " 字节）")
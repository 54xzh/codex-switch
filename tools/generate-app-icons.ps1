param(
    [string]$SourcePath = (Join-Path $PSScriptRoot "..\codex-switch-winui\Assets\1.png"),
    [string]$AssetsDir = (Join-Path $PSScriptRoot "..\codex-switch-winui\Assets")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$resolvedSourcePath = (Resolve-Path $SourcePath).Path
$resolvedAssetsDir = (Resolve-Path $AssetsDir).Path

function Save-IconPng {
    param(
        [Parameter(Mandatory)]
        [System.Drawing.Image]$Source,

        [Parameter(Mandatory)]
        [string]$Destination,

        [Parameter(Mandatory)]
        [int]$Width,

        [Parameter(Mandatory)]
        [int]$Height,

        [Parameter(Mandatory)]
        [ValidateSet("crop", "contain")]
        [string]$Mode
    )

    $bitmap = New-Object System.Drawing.Bitmap($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

        if ($Mode -eq "crop") {
            $scale = [Math]::Max(([double]$Width / [double]$Source.Width), ([double]$Height / [double]$Source.Height))
        }
        else {
            $scale = [Math]::Min(([double]$Width / [double]$Source.Width), ([double]$Height / [double]$Source.Height))
        }

        $drawWidth = [float]($Source.Width * $scale)
        $drawHeight = [float]($Source.Height * $scale)
        $offsetX = [float](($Width - $drawWidth) / 2)
        $offsetY = [float](($Height - $drawHeight) / 2)

        $destRect = [System.Drawing.RectangleF]::new($offsetX, $offsetY, $drawWidth, $drawHeight)
        $srcRect = [System.Drawing.RectangleF]::new(0, 0, $Source.Width, $Source.Height)

        $graphics.DrawImage($Source, $destRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
        $bitmap.Save($Destination, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$targets = @(
    [pscustomobject]@{ Name = "StoreLogo.png"; Width = 50; Height = 50; Mode = "crop" }
    [pscustomobject]@{ Name = "LockScreenLogo.png"; Width = 24; Height = 24; Mode = "crop" }
    [pscustomobject]@{ Name = "LockScreenLogo.scale-200.png"; Width = 48; Height = 48; Mode = "crop" }
    [pscustomobject]@{ Name = "Square44x44Logo.png"; Width = 44; Height = 44; Mode = "crop" }
    [pscustomobject]@{ Name = "Square44x44Logo.scale-200.png"; Width = 88; Height = 88; Mode = "crop" }
    [pscustomobject]@{ Name = "Square44x44Logo.targetsize-16.png"; Width = 16; Height = 16; Mode = "crop" }
    [pscustomobject]@{ Name = "Square44x44Logo.targetsize-16_altform-unplated.png"; Width = 16; Height = 16; Mode = "crop" }
    [pscustomobject]@{ Name = "Square44x44Logo.targetsize-24.png"; Width = 24; Height = 24; Mode = "crop" }
    [pscustomobject]@{ Name = "Square44x44Logo.targetsize-24_altform-unplated.png"; Width = 24; Height = 24; Mode = "crop" }
    [pscustomobject]@{ Name = "Square44x44Logo.targetsize-32.png"; Width = 32; Height = 32; Mode = "crop" }
    [pscustomobject]@{ Name = "Square44x44Logo.targetsize-32_altform-unplated.png"; Width = 32; Height = 32; Mode = "crop" }
    [pscustomobject]@{ Name = "Square44x44Logo.targetsize-48.png"; Width = 48; Height = 48; Mode = "crop" }
    [pscustomobject]@{ Name = "Square44x44Logo.targetsize-48_altform-unplated.png"; Width = 48; Height = 48; Mode = "crop" }
    [pscustomobject]@{ Name = "Square44x44Logo.targetsize-256.png"; Width = 256; Height = 256; Mode = "crop" }
    [pscustomobject]@{ Name = "Square44x44Logo.targetsize-256_altform-unplated.png"; Width = 256; Height = 256; Mode = "crop" }
    [pscustomobject]@{ Name = "Square150x150Logo.png"; Width = 150; Height = 150; Mode = "crop" }
    [pscustomobject]@{ Name = "Square150x150Logo.scale-200.png"; Width = 300; Height = 300; Mode = "crop" }
    [pscustomobject]@{ Name = "Wide310x150Logo.png"; Width = 310; Height = 150; Mode = "contain" }
    [pscustomobject]@{ Name = "Wide310x150Logo.scale-200.png"; Width = 620; Height = 300; Mode = "contain" }
    [pscustomobject]@{ Name = "SplashScreen.png"; Width = 620; Height = 300; Mode = "contain" }
    [pscustomobject]@{ Name = "SplashScreen.scale-200.png"; Width = 1240; Height = 600; Mode = "contain" }
)

$sourceImage = [System.Drawing.Image]::FromFile($resolvedSourcePath)

try {
    Copy-Item -LiteralPath $resolvedSourcePath -Destination (Join-Path $resolvedAssetsDir "2.png") -Force

    foreach ($target in $targets) {
        Save-IconPng `
            -Source $sourceImage `
            -Destination (Join-Path $resolvedAssetsDir $target.Name) `
            -Width $target.Width `
            -Height $target.Height `
            -Mode $target.Mode
    }
}
finally {
    $sourceImage.Dispose()
}

Write-Output "已生成图标文件。"

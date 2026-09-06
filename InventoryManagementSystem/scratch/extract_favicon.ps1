Add-Type -AssemblyName System.Drawing

$logoPath = "c:\Nightcode\inventorymgmt\InventoryManagementSystem\wwwroot\images\logo-dark.png"
if (-not (Test-Path $logoPath)) {
    $logoPath = "c:\Nightcode\inventorymgmt\InventoryManagementSystem\wwwroot\images\logo.png"
}

$img = [System.Drawing.Image]::FromFile($logoPath)
Write-Host "Source image loaded: $($img.Width) x $($img.Height)"

# Crop the left mark (symbol) from the logo
# The icon symbol is located on the left portion of the logo
$cropWidth = [int]($img.Height)
if ($cropWidth -gt $img.Width) { $cropWidth = $img.Width }

# Create square bitmap for favicon
$squareBmp = New-Object System.Drawing.Bitmap($cropWidth, $cropWidth)
$g = [System.Drawing.Graphics]::FromImage($squareBmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.Clear([System.Drawing.Color]::Transparent)

# Draw left portion of logo (the M-style icon mark)
$srcRect = New-Object System.Drawing.Rectangle(0, 0, $cropWidth, $cropWidth)
$destRect = New-Object System.Drawing.Rectangle(0, 0, $cropWidth, $cropWidth)
$g.DrawImage($img, $destRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)

# Make any non-transparent dark pixels pure white for dark browser tab visibility
# Or check if logo-dark already has white mark
$finalFavicon = New-Object System.Drawing.Bitmap(32, 32)
$gFinal = [System.Drawing.Graphics]::FromImage($finalFavicon)
$gFinal.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$gFinal.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$gFinal.Clear([System.Drawing.Color]::Transparent)
$gFinal.DrawImage($squareBmp, (New-Object System.Drawing.Rectangle(0, 0, 32, 32)))

$outputPath = "c:\Nightcode\inventorymgmt\InventoryManagementSystem\wwwroot\favicon.png"
$finalFavicon.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)

Write-Host "White M-style mark favicon created at: $outputPath"

$g.Dispose()
$squareBmp.Dispose()
$img.Dispose()
$gFinal.Dispose()
$finalFavicon.Dispose()

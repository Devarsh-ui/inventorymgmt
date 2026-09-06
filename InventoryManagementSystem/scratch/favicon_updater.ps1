Add-Type -AssemblyName System.Drawing

$width = 64
$height = 64
$bmp = New-Object System.Drawing.Bitmap($width, $height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

# Clear background transparent
$g.Clear([System.Drawing.Color]::Transparent)

# Draw rounded square glass badge
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$rect = New-Object System.Drawing.Rectangle(2, 2, 60, 60)
$radius = 16

$path.AddArc($rect.X, $rect.Y, $radius, $radius, 180, 90)
$path.AddArc($rect.Right - $radius, $rect.Y, $radius, $radius, 270, 90)
$path.AddArc($rect.Right - $radius, $rect.Bottom - $radius, $radius, $radius, 0, 90)
$path.AddArc($rect.X, $rect.Bottom - $radius, $radius, $radius, 90, 90)
$path.CloseFigure()

# Dark glass gradient background fill
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(240, 15, 23, 42), [System.Drawing.Color]::FromArgb(240, 30, 41, 59), 45)
$g.FillPath($brush, $path)

# Subtle highlight border
$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(180, 59, 130, 246), 2)
$g.DrawPath($pen, $path)

# White Text: SIMS
$font = New-Object System.Drawing.Font("Inter", 16, [System.Drawing.FontStyle]::Bold)
if ($font.Name -ne "Inter") {
    $font = New-Object System.Drawing.Font("Arial", 16, [System.Drawing.FontStyle]::Bold)
}

$textBrush = [System.Drawing.Brushes]::White
$sf = New-Object System.Drawing.StringFormat
$sf.Alignment = [System.Drawing.StringAlignment]::Center
$sf.LineAlignment = [System.Drawing.StringAlignment]::Center

$g.DrawString("SIMS", $font, $textBrush, (New-Object System.Drawing.RectangleF(0, 0, 64, 64)), $sf)

# Save to favicon.png
$outputPath = "c:\Nightcode\inventorymgmt\InventoryManagementSystem\wwwroot\favicon.png"
$bmp.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)

Write-Host "Favicon generated successfully at: $outputPath"

$g.Dispose()
$bmp.Dispose()

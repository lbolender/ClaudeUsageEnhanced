Add-Type -AssemblyName System.Drawing
$source = 'D:\ClaudeUsage\docs\dock-screenshot.png'
$dest = 'D:\ClaudeUsage\docs\dock-focused.png'
$bitmap = [System.Drawing.Bitmap]::FromFile($source)
# Crop center-top area where dock band sits (roughly center 600px wide, 50px tall)
$x = [Math]::Floor($bitmap.Width / 2) - 300
$rect = New-Object System.Drawing.Rectangle($x, 0, 600, 50)
$cropped = $bitmap.Clone($rect, $bitmap.PixelFormat)
# Scale up 3x for visibility
$scaled = New-Object System.Drawing.Bitmap($cropped.Width * 3, $cropped.Height * 3)
$g = [System.Drawing.Graphics]::FromImage($scaled)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.DrawImage($cropped, 0, 0, $scaled.Width, $scaled.Height)
$scaled.Save($dest, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$scaled.Dispose()
$cropped.Dispose()
$bitmap.Dispose()
Write-Host "Focused dock saved to $dest"

Add-Type -AssemblyName System.Drawing
$source = 'D:\ClaudeUsage\docs\dock-screenshot.png'
$dest = 'D:\ClaudeUsage\docs\dock-cropped2.png'
$bitmap = [System.Drawing.Bitmap]::FromFile($source)
# Crop center-top: dock band area (center 800px, 40px tall)
$x = [int]($bitmap.Width / 2) - 400
$rect = New-Object System.Drawing.Rectangle($x, 0, 800, 40)
$cropped = $bitmap.Clone($rect, $bitmap.PixelFormat)
$cropped.Save($dest, [System.Drawing.Imaging.ImageFormat]::Png)
$cropped.Dispose()
$bitmap.Dispose()
Write-Host "Done: $dest"

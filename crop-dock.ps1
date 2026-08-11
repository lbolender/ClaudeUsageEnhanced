Add-Type -AssemblyName System.Drawing
$source = 'D:\ClaudeUsage\docs\dock-screenshot.png'
$dest = 'D:\ClaudeUsage\docs\dock-cropped.png'
$bitmap = [System.Drawing.Bitmap]::FromFile($source)
# Crop top 60 pixels across full width - captures the dock band area
$rect = New-Object System.Drawing.Rectangle(0, 0, $bitmap.Width, 60)
$cropped = $bitmap.Clone($rect, $bitmap.PixelFormat)
$cropped.Save($dest, [System.Drawing.Imaging.ImageFormat]::Png)
$cropped.Dispose()
$bitmap.Dispose()
Write-Host "Cropped dock saved to $dest"

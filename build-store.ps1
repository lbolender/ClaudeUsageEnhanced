$ErrorActionPreference = "Stop"

$proj = Join-Path $PSScriptRoot "ClaudeUsage"
$bin = Join-Path $proj "bin"
$publish = Join-Path $proj "bin\Release\net9.0-windows10.0.26100.0\win-x64\publish"
$staging = Join-Path $bin "msix-staging"
$msix = Join-Path $PSScriptRoot "ClaudeUsage-Store.msix"
$dotnet = "dotnet"

$makeAppx = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe"
if (-not (Test-Path $makeAppx)) {
    $makeAppx = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\makeappx.exe" | Select-Object -First 1
}

Write-Host "[1/3] Publish managed assemblies..."
& $dotnet publish (Join-Path $proj "ClaudeUsage.csproj") -c Release -r win-x64 --self-contained false -p:GenerateAppxPackageOnBuild=false -v minimal -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "[2/3] Stage MSIX payload..."
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item $staging -ItemType Directory | Out-Null
Copy-Item "$publish\*" $staging -Recurse -Force

$stagingAssets = Join-Path $staging "Assets"
$stagingPublic = Join-Path $staging "Public"
if (-not (Test-Path $stagingAssets)) { New-Item $stagingAssets -ItemType Directory | Out-Null }
if (-not (Test-Path $stagingPublic)) { New-Item $stagingPublic -ItemType Directory | Out-Null }
Copy-Item (Join-Path $proj "Assets\*") $stagingAssets -Recurse -Force
Copy-Item (Join-Path $proj "Public\*") $stagingPublic -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $proj "Package.appxmanifest") (Join-Path $staging "AppxManifest.xml") -Force

Write-Host "[3/3] Packing Store MSIX..."
if (Test-Path $msix) { Remove-Item $msix -Force }
& $makeAppx pack /d $staging /p $msix /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }

Write-Host -ForegroundColor Green "`nSuccess! Upload this file to the Microsoft Store:"
Write-Host -ForegroundColor Cyan $msix

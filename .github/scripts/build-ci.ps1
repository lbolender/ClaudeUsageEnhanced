$ErrorActionPreference = "Stop"

# We assume this script is run from the root of the repository
$root = $PSScriptRoot | Split-Path -Parent | Split-Path -Parent
$proj = Join-Path $root "ClaudeUsage"
$publish = Join-Path $proj "bin\Release\net9.0-windows10.0.26100.0\win-x64\publish"
$manifest = Join-Path $proj "Package.appxmanifest"

# signtool rejects an MSIX whose certificate subject differs from the manifest publisher,
# so the subject is always taken from the manifest instead of being repeated here
$publisher = ([xml](Get-Content $manifest)).Package.Identity.Publisher

# We'll create a dist folder in the root
$dist = Join-Path $root "dist"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item $dist -ItemType Directory | Out-Null

$staging = Join-Path $dist "msix-staging"
$msix = Join-Path $dist "ClaudeUsage.msix"
$cer = Join-Path $dist "ClaudeUsage.cer"
$pfx = Join-Path $dist "ClaudeUsage.pfx"

$dotnet = "dotnet"
# Use default path on windows-latest runner
$makeAppx = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe"
if (-not (Test-Path $makeAppx)) {
    # Fallback if specific version is not found
    $makeAppx = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\makeappx.exe" | Select-Object -First 1
}
$signTool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
if (-not (Test-Path $signTool)) {
    $signTool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe" | Select-Object -First 1
}

Write-Host "[1/5] Publish managed assemblies"
& $dotnet publish (Join-Path $proj "ClaudeUsage.csproj") -c Release -r win-x64 --self-contained false -p:GenerateAppxPackageOnBuild=false -v minimal -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "[2/5] Stage MSIX payload"
New-Item $staging -ItemType Directory | Out-Null
Copy-Item "$publish\*" $staging -Recurse -Force

$stagingAssets = Join-Path $staging "Assets"
$stagingPublic = Join-Path $staging "Public"
if (-not (Test-Path $stagingAssets)) { New-Item $stagingAssets -ItemType Directory | Out-Null }
if (-not (Test-Path $stagingPublic)) { New-Item $stagingPublic -ItemType Directory | Out-Null }
Copy-Item (Join-Path $proj "Assets\*") $stagingAssets -Recurse -Force
Copy-Item (Join-Path $proj "Public\*") $stagingPublic -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item $manifest (Join-Path $staging "AppxManifest.xml") -Force

$tempCert = $null
try {
    if ($env:SIGNING_PFX_BASE64) {
        Write-Host "[3/5] Use signing cert from SIGNING_PFX_BASE64"
        # A stable cert means users trust it once; later releases install without importing a new one
        $pfxPassword = $env:SIGNING_PFX_PASSWORD
        [IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:SIGNING_PFX_BASE64))
        $signingCert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($pfx, $pfxPassword)
        if ($signingCert.Subject -ne $publisher) {
            throw "Signing cert subject '$($signingCert.Subject)' does not match manifest publisher '$publisher'"
        }
        [IO.File]::WriteAllBytes($cer, $signingCert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    } else {
        Write-Host "[3/5] Generate ephemeral CI cert (no SIGNING_PFX_BASE64 secret set)"
        # Fallback: a fresh self-signed cert per build, so every release ships a different cert to trust
        $pfxPassword = "CI_Build_Pwd_" + (New-Guid).ToString()
        $tempCert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $publisher -KeyUsage DigitalSignature -FriendlyName "ClaudeUsage CI" -CertStoreLocation "Cert:\CurrentUser\My" -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3","2.5.29.19={text}")
        $securePwd = ConvertTo-SecureString -String $pfxPassword -Force -AsPlainText
        Export-PfxCertificate -Cert $tempCert -FilePath $pfx -Password $securePwd | Out-Null
        Export-Certificate -Cert $tempCert -FilePath $cer | Out-Null
    }

    Write-Host "[4/5] MakeAppx pack"
    & $makeAppx pack /d $staging /p $msix /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }

    Write-Host "[5/5] Sign MSIX"
    & $signTool sign /fd SHA256 /a /f $pfx /p $pfxPassword $msix | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "signtool failed" }
}
finally {
    # The private key must never end up in dist/ or stay in the runner's cert store, even on failure
    if (Test-Path $pfx) { Remove-Item $pfx -Force }
    if ($tempCert) { Remove-Item "Cert:\CurrentUser\My\$($tempCert.Thumbprint)" }
}

Remove-Item $staging -Recurse -Force

Write-Host "Build complete. Artifacts in dist/:"
Get-ChildItem $dist | Select-Object Name, Length

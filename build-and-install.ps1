# ClaudeUsage - full build + sign + install (re-run after every code change)
# Run from a normal PowerShell prompt; UAC will be prompted for the install step.

$ErrorActionPreference = "Stop"
$proj = Join-Path $PSScriptRoot "ClaudeUsage"
$bin = Join-Path $proj "bin"
$publish = Join-Path $proj "bin\Release\net9.0-windows10.0.26100.0\win-x64\publish"
$staging = Join-Path $bin "msix-staging"
$msix = Join-Path $bin "ClaudeUsage.msix"
$cer = Join-Path $bin "ClaudeUsage.cer"
$pfx = Join-Path $bin "ClaudeUsage.pfx"
$pfxPassword = "ClaudeUsageDev!"
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
$makeAppx = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe"
$signTool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
# Package name, publisher and version come from the manifest; the dev cert subject must equal the publisher
$identity = ([xml](Get-Content (Join-Path $proj "Package.appxmanifest"))).Package.Identity

Write-Host "[1/6] Publish managed assemblies"
& $dotnet publish (Join-Path $proj "ClaudeUsage.csproj") -c Release -r win-x64 --self-contained false -p:GenerateAppxPackageOnBuild=false -v minimal -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "[2/6] Stage MSIX payload"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item $staging -ItemType Directory | Out-Null
Copy-Item "$publish\*" $staging -Recurse -Force
# Ensure project Assets/ and Public/ override anything copied from publish.
$stagingAssets = Join-Path $staging "Assets"
$stagingPublic = Join-Path $staging "Public"
if (-not (Test-Path $stagingAssets)) { New-Item $stagingAssets -ItemType Directory | Out-Null }
if (-not (Test-Path $stagingPublic)) { New-Item $stagingPublic -ItemType Directory | Out-Null }
Copy-Item (Join-Path $proj "Assets\*") $stagingAssets -Recurse -Force
Copy-Item (Join-Path $proj "Public\*") $stagingPublic -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $proj "Package.appxmanifest") (Join-Path $staging "AppxManifest.xml") -Force

Write-Host "[3/6] Ensure dev cert exists"
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $identity.Publisher } | Select-Object -First 1
if (-not $cert) {
  $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $identity.Publisher -KeyUsage DigitalSignature -FriendlyName "ClaudeUsage Dev" -CertStoreLocation "Cert:\CurrentUser\My" -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3","2.5.29.19={text}")
}
$securePwd = ConvertTo-SecureString -String $pfxPassword -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $securePwd | Out-Null
Export-Certificate -Cert $cert -FilePath $cer | Out-Null

Write-Host "[4/6] MakeAppx pack"
if (Test-Path $msix) { Remove-Item $msix -Force }
& $makeAppx pack /d $staging /p $msix /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }

Write-Host "[5/6] Sign MSIX"
& $signTool sign /fd SHA256 /a /f $pfx /p $pfxPassword $msix | Out-Null
if ($LASTEXITCODE -ne 0) { throw "signtool failed" }

Write-Host "[6/6] Install"
$trusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
if (-not $trusted) {
  # Cert import needs admin; only elevate on first install.
  $installScript = @"
`$ErrorActionPreference = 'Stop'
Import-Certificate -FilePath '$cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
Add-AppxPackage -Path '$msix' -ForceTargetApplicationShutdown
"@
  $installScriptPath = Join-Path $bin "elevated-install.ps1"
  Set-Content -Path $installScriptPath -Value $installScript
  $proc = Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile","-ExecutionPolicy","Bypass","-File","`"$installScriptPath`"" -PassThru -Wait
  if ($proc.ExitCode -ne 0) { throw "Elevated install failed (exit code $($proc.ExitCode)) - package NOT updated" }
} else {
  Add-AppxPackage -Path $msix -ForceTargetApplicationShutdown
}
$installed = (Get-AppxPackage -Name $identity.Name).Version
if ($installed -ne $identity.Version) { throw "Installed version $installed does not match manifest $($identity.Version) - install failed" }

Write-Host ""
Write-Host "Restarting CmdPal..."
Get-Process -Name "Microsoft.CmdPal.UI" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
explorer.exe "shell:AppsFolder\Microsoft.CommandPalette_8wekyb3d8bbwe!App"

Write-Host ""
Write-Host "Done. Verify:"
Get-AppxPackage -Name $identity.Name | Format-Table Name, Version, PackageFamilyName -AutoSize
Write-Host "Open CmdPal (Win+Alt+Space). To put ClaudeUsage widget on the Dock: Settings -> Dock -> add bands."

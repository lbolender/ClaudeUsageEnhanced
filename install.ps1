# install.ps1
$ErrorActionPreference = "Stop"

# Ensure script is running as Administrator (required for installing the certificate)
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Restarting script with Administrator privileges to install the certificate..."
    $scriptArgs = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    Start-Process powershell -Verb RunAs -ArgumentList $scriptArgs -Wait
    exit
}

$scriptDir = $PSScriptRoot
$cerFile = Join-Path $scriptDir "ClaudeUsage.cer"
$msixFile = Join-Path $scriptDir "ClaudeUsage.msix"

if (-not (Test-Path $cerFile) -or -not (Test-Path $msixFile)) {
    Write-Host -ForegroundColor Red "Error: ClaudeUsage.cer or ClaudeUsage.msix not found in this directory."
    Write-Host "Please extract all files from the ZIP before running this script."
    Read-Host "Press Enter to exit..."
    exit
}

Write-Host "Installing certificate..."
Import-Certificate -FilePath $cerFile -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null

Write-Host "Installing ClaudeUsage package..."
Add-AppxPackage -Path $msixFile -ForceTargetApplicationShutdown

Write-Host -ForegroundColor Green "Installation complete!"
Write-Host "Restarting CmdPal..."
Get-Process -Name "Microsoft.CmdPal.UI" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
explorer.exe "shell:AppsFolder\Microsoft.CommandPalette_8wekyb3d8bbwe!App"

Write-Host ""
Write-Host "Open CmdPal (Win+Alt+Space). To put ClaudeUsage widget on the Dock: Settings -> Dock -> add bands."
Read-Host "Press Enter to exit..."

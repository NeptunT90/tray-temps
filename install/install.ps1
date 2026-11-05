<#
  Installs the latest TrayTemps release into %LOCALAPPDATA%\TrayTemps
  and adds it to the user's startup folder.

  Usage (from a browser): download this file, then
    powershell -ExecutionPolicy Bypass -File install.ps1
  Or with a custom repository:
    powershell -ExecutionPolicy Bypass -File install.ps1 -Repo owner/tray-temps
#>
param(
    [switch]$NoAutostart,
    [string]$Repo = "NeptunT90/tray-temps"
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$target = Join-Path $env:LOCALAPPDATA "TrayTemps"
Write-Host "Installing TrayTemps to $target"

$release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" `
    -UseBasicParsing -Headers @{ "User-Agent" = "TrayTemps-installer" }
$asset = $release.assets | Where-Object { $_.name -like "TrayTemps-*-win64.zip" } | Select-Object -First 1
if (-not $asset) { throw "No TrayTemps-*-win64.zip asset found in the latest release of $Repo" }

$tempZip = Join-Path $env:TEMP $asset.name
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $tempZip -UseBasicParsing

Get-Process TrayTemps -ErrorAction SilentlyContinue | Stop-Process -Force
New-Item -ItemType Directory -Force $target | Out-Null
Expand-Archive -Path $tempZip -DestinationPath $target -Force
Remove-Item $tempZip -Force

if (-not $NoAutostart) {
    $link = Join-Path ([Environment]::GetFolderPath("Startup")) "TrayTemps.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($link)
    $shortcut.TargetPath = Join-Path $target "TrayTemps.exe"
    $shortcut.WorkingDirectory = $target
    $shortcut.Description = "CPU/GPU/disk/board temperatures on the taskbar"
    $shortcut.Save()
    Write-Host "Startup shortcut created: $link"
}

Start-Process (Join-Path $target "TrayTemps.exe")
Write-Host "Done. Confirm the UAC prompt so the sensors can be read."


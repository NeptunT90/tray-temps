<#
  Removes TrayTemps: stops the process, deletes the startup shortcut,
  the installation folder and the settings/log files under %APPDATA%.
#>
$ErrorActionPreference = "SilentlyContinue"

Get-Process TrayTemps | Stop-Process -Force
Remove-Item (Join-Path ([Environment]::GetFolderPath("Startup")) "TrayTemps.lnk") -Force
Remove-Item (Join-Path $env:LOCALAPPDATA "TrayTemps") -Recurse -Force
Remove-Item (Join-Path $env:APPDATA "dsh-tray-temps") -Recurse -Force
Write-Host "TrayTemps removed."

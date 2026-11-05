<#
  Builds TrayTemps.exe and packs a portable zip.

  Requires only Windows (.NET Framework 4.8 csc.exe). The sensor libraries are
  downloaded from the official LibreHardwareMonitor release and cached in .cache.
#>
param(
    [string]$Version = "1.0.0",
    [string]$OutDir,
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $root "dist" }

$lhmVersion = "v0.9.6"
$lhmZipUrl = "https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/download/$lhmVersion/LibreHardwareMonitor.zip"
$cache = Join-Path $root ".cache"
$lhmDir = Join-Path $cache "lhm"

if (-not (Test-Path (Join-Path $lhmDir "LibreHardwareMonitorLib.dll"))) {
    New-Item -ItemType Directory -Force $cache | Out-Null
    $zip = Join-Path $cache "lhm.zip"
    Write-Host "Downloading LibreHardwareMonitor $lhmVersion ..."
    Invoke-WebRequest -Uri $lhmZipUrl -OutFile $zip -UseBasicParsing
    Expand-Archive -Path $zip -DestinationPath $lhmDir -Force
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
Copy-Item (Join-Path $lhmDir "*.dll") $OutDir -Force
Copy-Item (Join-Path $PSScriptRoot "TrayTemps.exe.config") $OutDir -Force
Copy-Item (Join-Path $root "scripts\*.cmd") $OutDir -Force
Copy-Item (Join-Path $root "README.md") $OutDir -Force
Copy-Item (Join-Path $root "LICENSE") $OutDir -Force
Copy-Item (Join-Path $root "THIRD-PARTY-NOTICES.md") $OutDir -Force
Set-Content (Join-Path $OutDir "VERSION.txt") $Version -Encoding ASCII

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }

# /version needs four numeric parts
$fileVersion = $Version.TrimStart("v")
while (($fileVersion -split "\.").Count -lt 4) { $fileVersion += ".0" }

Push-Location $OutDir
try {
    & $csc /nologo /target:winexe /out:"TrayTemps.exe" `
        /r:"LibreHardwareMonitorLib.dll" /r:System.Windows.Forms.dll /r:System.Drawing.dll `
        /r:System.Management.dll /r:Microsoft.CSharp.dll (Join-Path $root "src\TrayTemps.cs")
    if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }
}
finally {
    Pop-Location
}

if (-not $SkipZip) {
    $zipPath = Join-Path $OutDir "TrayTemps-$Version-win64.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $OutDir "*") -DestinationPath $zipPath -Force
    Write-Host "Release archive: $zipPath"
}

Write-Host "Build finished: $OutDir"



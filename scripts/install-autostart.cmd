@echo off
chcp 65001 >nul
set "LINK=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\TrayTemps.lnk"
powershell -NoProfile -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('%LINK%'); $s.TargetPath='%~dp0TrayTemps.exe'; $s.WorkingDirectory='%~dp0'; $s.Save()"
echo TrayTemps will start with Windows. One UAC confirmation at logon.
pause

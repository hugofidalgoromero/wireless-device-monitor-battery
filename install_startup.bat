@echo off
set "DIR=%~dp0"
set "LNK=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\WirelessDeviceMonitorBattery.lnk"
powershell -NoProfile -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('%LNK%'); $s.TargetPath='%DIR%WirelessDeviceMonitorBattery.exe'; $s.WorkingDirectory='%DIR%'; $s.Save()"
start "" "%DIR%WirelessDeviceMonitorBattery.exe"
echo Wireless Device Monitor Battery will now start with Windows. Do not move this folder; if you do, run this file again.
echo Wireless Device Monitor Battery se abrira al iniciar Windows. No muevas esta carpeta; si la mueves, vuelve a ejecutar este archivo.
pause

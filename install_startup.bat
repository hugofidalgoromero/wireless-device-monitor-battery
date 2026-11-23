@echo off
set "DIR=%~dp0"
set "LNK=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\BatteryTray.lnk"
powershell -NoProfile -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('%LNK%'); $s.TargetPath='%DIR%BatteryTray.exe'; $s.WorkingDirectory='%DIR%'; $s.Save()"
start "" "%DIR%BatteryTray.exe"
echo BatteryTray se abrira al iniciar Windows. No muevas esta carpeta; si la mueves, vuelve a ejecutar este archivo.
pause

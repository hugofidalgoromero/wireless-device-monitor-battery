@echo off
del "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\BatteryTray.lnk" 2>nul
taskkill /im BatteryTray.exe /f >nul 2>&1
echo BatteryTray cerrado y quitado del inicio de Windows.
pause

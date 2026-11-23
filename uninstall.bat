@echo off
del "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\WirelessDeviceMonitorBattery.lnk" 2>nul
taskkill /im WirelessDeviceMonitorBattery.exe /f >nul 2>&1
echo App closed and removed from Windows startup.
echo Aplicacion cerrada y quitada del inicio de Windows.
pause

@echo off
"%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /debug- /platform:x86 /out:WirelessDeviceMonitorBattery.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll "%~dp0src\WirelessDeviceMonitorBattery.cs"
if errorlevel 1 (echo Build failed / Error al compilar.) else (echo Done / Listo: WirelessDeviceMonitorBattery.exe)
pause

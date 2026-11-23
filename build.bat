@echo off
"%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /debug- /platform:x86 /out:BatteryTray.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll "%~dp0src\BatteryTray.cs"
if errorlevel 1 (echo Error al compilar.) else (echo Listo: BatteryTray.exe)
pause

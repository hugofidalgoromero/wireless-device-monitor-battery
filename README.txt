WIRELESS DEVICE MONITOR BATTERY
===============================

EN
--
Shows the battery level of your Bluetooth devices (mouse, keyboard, headset...)
next to the Windows tray. ~30 KB, about 1 MB of RAM, no installer, no dependencies.

1. Extract this folder somewhere permanent (e.g. Documents). Do not move it later.
2. Double-click WirelessDeviceMonitorBattery.exe. It appears left of the system tray.
3. To start it with Windows: run install_startup.bat. To remove it: uninstall.bat.

- Hover a device: opens a panel with its name, type, status, power level,
  charging state, link type, Bluetooth address and power source.
- Right-click: device list, "Refresh now" and "Exit". Refreshes every 5 minutes.
- Colors: cyan, yellow (<= 50%), red (<= 20%), green while charging.
- Language follows Windows (Spanish/English). Force it: --lang en  or  --lang es

Notes
- Windows may show "Windows protected your PC" because the .exe is not signed.
  Click "More info" > "Run anyway".
- If your antivirus blocks it, run build.bat: it builds the .exe from the source
  (src\WirelessDeviceMonitorBattery.cs) with the compiler that ships with Windows.
- Only devices whose battery Windows reads over Bluetooth are shown. Devices with
  a vendor-specific USB receiver (Logitech, Razer, SteelSeries...) will not appear.
- Works on Windows 10 and 11 (designed for 11).

ES
--
Muestra la bateria de tus dispositivos Bluetooth (raton, teclado, auriculares...)
junto a la bandeja de Windows. ~30 KB, ~1 MB de RAM, sin instalador ni dependencias.

1. Descomprime esta carpeta donde quieras dejarla (p. ej. Documentos). No la muevas despues.
2. Doble clic en WirelessDeviceMonitorBattery.exe. Aparece a la izquierda de la bandeja.
3. Para que arranque con Windows: install_startup.bat. Para quitarlo: uninstall.bat.

- Pasa el raton por encima de un dispositivo: panel con su nombre, tipo, estado,
  nivel de energia, si esta cargando, tipo de enlace, direccion Bluetooth y fuente.
- Clic derecho: lista de dispositivos, "Actualizar ahora" y "Salir". Se actualiza cada 5 minutos.
- Colores: cian, amarillo (<= 50%), rojo (<= 20%), verde si esta cargando.
- El idioma sigue el de Windows (espanol/ingles). Para forzarlo: --lang es  o  --lang en

Avisos
- Windows puede decir "Windows protegio su PC" porque el .exe no esta firmado.
  Pulsa "Mas informacion" y luego "Ejecutar de todas formas".
- Si tu antivirus lo bloquea, ejecuta build.bat: genera el .exe desde el codigo
  (src\WirelessDeviceMonitorBattery.cs) con el compilador que ya trae Windows.
- Solo aparecen dispositivos cuya bateria Windows lea por Bluetooth. Los que usan
  receptor USB propio (Logitech, Razer, SteelSeries...) no se muestran.
- Funciona en Windows 10 y 11 (pensado para 11).

MIT License - https://github.com/hugofidalgoromero/wireless-device-monitor-battery

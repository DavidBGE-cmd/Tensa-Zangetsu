# ⚡ Tensa-Zangetsu • Universal Android Servicing Suite

<p align="center">
  <b>Suite profesional de flasheo, servicio técnico a bajo nivel y administración de particiones para Android.</b><br/>
  Diseñado con la estética <b>DOTS Futuristic</b> (OLED Black <code>#08090C</code>, Blanco nítido y Rojo Carmesí <code>#FF1744</code>).
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 10.0"/>
  <img src="https://img.shields.io/badge/GUI-Avalonia_11-E64A19?style=for-the-badge&logo=avalonia&logoColor=white" alt="Avalonia UI"/>
  <img src="https://img.shields.io/badge/Architecture-Clean_MVVM-00E676?style=for-the-badge" alt="Clean MVVM"/>
  <img src="https://img.shields.io/badge/License-GPL_v3-blue?style=for-the-badge" alt="License"/>
</p>

---

## 🚀 Características Principales

### 1. 📟 Detección Inteligente en Vivo & CMD Studio
* **Clasificación de Modos en Tiempo Real (<1s):**
  * `🟢 ANDROID SYSTEM (ADB ONLINE)`: Sistema iniciado con depuración USB autorizada.
  * `🟡 ADB NO AUTORIZADO (RSA)`: Advertencia inmediata en pantalla con guía para autorizar la clave RSA en el dispositivo.
  * `🟣 MODO RECOVERY`: Identificación de Stock Recovery, TWRP u OrangeFox.
  * `🔵 ADB SIDELOAD`: Modo Sideload listo para transferir paquetes OTA (`adb sideload rom.zip`).
  * `🟠 FASTBOOT BOOTLOADER`: Fastboot de bajo nivel (`is-userspace: no`).
  * `🔴 FASTBOOT-D USERSPACE`: Espacio de usuario para particiones dinámicas super (`is-userspace: yes`).
  * `🔵 SAMSUNG DOWNLOAD`: Modo Descarga / Odin vía identificadores USB PnP (04E8:685D/6860).
  * `⚡ MEDIATEK BROM / PRELOADER`: Puerto VCOM de arranque de hardware.
  * `🐉 QUALCOMM EDL 9008`: Modo de descarga de emergencia Sahara / Firehose.
  * `📱 UNISOC BSL`: Puerto serial de diagnóstico U2S Diag.
  * `⚠️ FALTA CONTROLADOR`: Detección a nivel de bus USB con botón de remediación automática en 1 clic.
* **Control de Ranuras A/B & Particiones Dinámicas:**
  * Conmutador directo entre **Bootloader** y **Fastboot-D**.
  * Detección y alternancia de ranura activa (**Slot A ↔ Slot B**).
  * Desbloqueo universal con `fastboot flashing unlock` y fallback a `fastboot oem unlock`.

---

### 2. 📱 Módulos de Fabricante (OEM Workstations)

#### 🟠 Xiaomi / Redmi / POCO
* **Verificación de Anti-Rollback (ARB):** Consulta `fastboot getvar anti` para evitar *Hard-Bricks* por degradación indebida de versión.
* **Codename & Token:** Lee el nombre clave de placa y el token criptográfico para Mi Unlock.
* **Bypass Setup Wizard:** Comandos directos para omitir el asistente de configuración de MIUI y HyperOS sin cuenta previa.
* **Mi Assistant Sideload:** Reinicio a modo de recuperación asistido para instalación de ROMs oficiales.

#### 🔵 Motorola Rescue & Smart Servicing
* **Extractor de Unlock Data:** Ejecuta `fastboot oem get_unlock_data`, procesa las 5 líneas hexadecimales, las **concatena en un solo código limpio** y las copia al portapapeles.
* **Desbloqueo con Clave:** Envío seguro de clave oficial (`fastboot oem unlock <key>`).
* **Reboot a EDL 9008:** Ingreso a modo Qualcomm 9008 directamente desde Fastboot (`fastboot oem reboot-edl`) sin abrir el equipo.
* **Diagnóstico de Hardware:** Consulta `ro.build.version.full`, canal de operadora (`channelid`: RETLA, RETEU, etc.) y `securestate`.

#### 🔷 Samsung Galaxy (Odin & Fastboot)
* **Reboot Download Mode:** Reinicio inmediato a la pantalla celeste de Odin (`adb reboot download`).
* **Reboot Fastboot-D:** Acceso al entorno FastbootD para instalación de GSIs en dispositivos Samsung Android 10+.
* **Inspección de CSC & PDA:** Lectura de código de región de operadora y versión de firmware.

#### 🟣 Vivo / iQOO
* **Fastboot to EDL 9008:** Reinicio a modo de emergencia Qualcomm.
* **Lectura BBK Info:** Consulta de propiedades propietarias de hardware.

#### 🔴 OnePlus / OPPO / Realme (OPLUS)
* **Desbloqueo OnePlus:** Desbloqueo de bootloader y particiones críticas (`flashing unlock_critical`).
* **Variables OPLUS:** Lectura de `project_name` y versión de software OPLUS.

---

### 3. ⚙️ Protocolos por Chipset

* **MediaTek (MTK):**
  * Handshake nativo en BootROM (BROM) y Preloader con desactivación de Watchdog Timer (WDT).
  * **Scatter Flasher:** Carga de archivos `*_Android_scatter.txt`, checklist de particiones y cálculo de memoria en MB.
  * **Gestor de Tabla GPT:** Lectura, volcado y escritura partición por partición.
  * **Respaldo de IMEI & Radiofrecuencias:** Volcado conjunto de `nvram`, `nvdata`, `protect1` y `protect2`.
  * **Dump Completo sin Userdata:** Respaldo íntegro del sistema omitiendo datos privados de usuario.
* **Qualcomm Snapdragon (EDL 9008):**
  * Soporte para cargadores Firehose Programmer (`.mbn` / `.elf`) y tablas `rawprogram0.xml` / `patch0.xml`.
* **Unisoc / Spreadtrum (SPD / BSL):**
  * Protocolo SCI U2S Diag y flasheador de paquetes oficiales `.PAC`.

---

### 4. 🔌 Centro de Controladores & Runtimes (1-Click Install)
Descarga e instala controladores firmados WHQL desde servidores oficiales y CDNs directos:
* **Universal ADB & Fastboot:** Google Android SDK Platform-Tools.
* **MediaTek MTK VCOM:** CDC ACM + UsbDk Filter.
* **Qualcomm 9008:** Qualcomm HS-USB QDLoader 9008 (WHQL signed v1.0.10061).
* **Unisoc SPRD:** Driver oficial USB2Serial (`DPInst64.exe` / `sprdvcom.inf`).
* **Samsung Mobile:** SAMSUNG Mobile USB Composite Driver oficial v1.9.5.0.
* **Limpiador de Puertos COM Fantasma:** Limpieza de registros huérfanos en `HKLM\SYSTEM\CurrentControlSet\Control\COM Name Arbiter` para liberar puertos COM bloqueados.

---

## 🏗️ Estructura del Proyecto

```
Tensa-Zangetsu/
├── src/
│   ├── TensaZangetsu.App/                 # Aplicación de escritorio Avalonia UI (Estilo DOTS)
│   ├── TensaZangetsu.Services/            # Motor de servicios (AdbFastboot, DriverToolkit, FlashEngine)
│   ├── TensaZangetsu.Protocol.Mtk/        # Protocolo BROM, DA, Scatter y extensiones MTK
│   ├── TensaZangetsu.Protocol.Qcom/       # Protocolo Qualcomm Sahara y Firehose
│   ├── TensaZangetsu.Protocol.Sprd/       # Protocolo Unisoc / Spreadtrum BSL y PAC
│   ├── TensaZangetsu.UsbWatcher/          # Monitoreo en vivo de bus USB y PnP
│   ├── TensaZangetsu.Gpt/                 # Parser y gestor de particiones GPT
│   ├── TensaZangetsu.BlockDevice/         # Abstracción de operaciones de bloques EMMC/UFS
│   ├── TensaZangetsu.Transport.LibUsb/    # Capa de transporte LibUsb / UsbDk
│   ├── TensaZangetsu.Transport.SerialPort/# Capa de transporte Serial / VCOM
│   └── TensaZangetsu.CLI/                 # Interfaz de línea de comandos (tensazangetsu)
├── docs/                                  # Especificaciones de protocolos y arquitectura
├── licenses/                              # Licencias de librerías de terceros
├── Directory.Build.props                  # Configuración de compilación global
├── Directory.Build.targets                # Metas de compilación
├── TensaZangetsu.slnx                     # Solución XML moderna de Visual Studio / .NET 10
└── README.md                              # Documentación del proyecto
```

---

## 🛠️ Requisitos & Compilación

### Requisitos Previos:
* **SDK de .NET 10.0** (o superior) instalado en el sistema.
* Windows 10/11 (x64 / ARM64).

### Instrucciones de Compilación:

1. **Clonar el repositorio:**
   ```bash
   git clone https://github.com/DavidBGE-cmd/Tensa-Zangetsu.git
   cd Tensa-Zangetsu
   ```

2. **Compilar la solución:**
   ```bash
   dotnet build TensaZangetsu.slnx
   ```

3. **Ejecutar la interfaz gráfica:**
   ```bash
   dotnet run --project src/TensaZangetsu.App/TensaZangetsu.App.csproj
   ```

---

## 📄 Licencia

Este proyecto está distribuido bajo la licencia **GNU General Public License v3.0**. Consulta el archivo `LICENSE` para más detalles.

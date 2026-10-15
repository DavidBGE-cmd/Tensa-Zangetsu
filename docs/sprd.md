# SPRD / Unisoc BSL

`GeekFlashCore.Protocol.Sprd.Abstractions` provee contratos públicos para .NET 8, y `GeekFlashCore.Protocol.Sprd` implementa el protocolo BSL sobre `ITransport` síncrono. El núcleo es reutilizable en CLI, escritorio y servicios sin depender de WMI ni DLLs de plataforma. La fachada asíncrona `IProtocol` y la API síncrona comparten un gate serializado; únicamente los límites de recursos del host (como Loader Provider) efectúan esperas asíncronas.

La implementación actual cubre BootROM→FDL1→FDL2, conexión automática/explícita con FDL ya cargado, enumeración de particiones nativas/GPT, escritura Raw/Sparse en streaming (enmarcada o Raw v1/v2 explícito), lectura por rangos, Chip UID, borrado de partición completa, reinicio normal y apagado. Validado en hardware real sobre iPlay40/ums512: detección COM automática, handshake con corrección de CRC en BootROM, subida y ejecución de FDL de dos etapas con firma y reconexión automática con FDL2 ya cargado.

## CLI

El nombre del protocolo puede indicarse como `sprd`, `unisoc` o `spreadtrum`. En Windows, la CLI sin parámetros descubre automáticamente el puerto de descarga `SPRD U2S Diag`; `--protocol sprd` limita el escaneo al protocolo SPRD. Actualmente reconoce de forma específica `VID_1782&PID_4D00` (`1782:4D00`). La etapa (BootROM/FDL1/FDL2) se determina mediante el handshake validado, sin enviar paquetes automáticos de conmutación DIAG.

Se puede especificar el puerto COM con `--protocol sprd --port COMx`. `--usb 1782:4D00` deduce SPRD utilizando LibUsb. El tiempo de espera predeterminado es de 30 segundos, configurable con `--device-wait-timeout MS` (cancelable con Ctrl+C).

```text
geekflash help sprd
geekflash --protocol sprd
geekflash --protocol sprd --device-wait-timeout 60000 connect
geekflash --pac "firmware.pac" --non-interactive connect
geekflash --protocol sprd --port COM7 --loader FDL1.bin --sprd-fdl1-address FDL1_ADDRESS --sprd-fdl2 FDL2.bin --sprd-fdl2-address FDL2_ADDRESS connect
geekflash --protocol sprd --port COM7 partitions all
geekflash --protocol sprd --port COM7 --sprd-entry fdl2 --sprd-partition-source native --sprd-partition-unit UNIT_BYTES read boot boot.img
geekflash --protocol sprd --port COM7 --sprd-entry fdl2 --sprd-partition-source native --sprd-partition-unit UNIT_BYTES write boot boot.img
geekflash --protocol sprd --port COM7 --sprd-entry fdl2 --sprd-partition-source native --sprd-partition-unit UNIT_BYTES erase cache
geekflash --protocol sprd --port COM7 --sprd-entry fdl2 reboot system
geekflash --protocol sprd --port COM7 --sprd-partition-source gpt partitions all
geekflash --protocol sprd --port COM7 --sprd-entry fdl2 sprd-chip-uid
```

`FDL1_ADDRESS` / `FDL2_ADDRESS` y `UNIT_BYTES` deben sustituirse por valores confirmados del dispositivo (admiten notación hexadecimal `0x`). La omisión de la etapa de entrada o `--sprd-entry auto` detecta automáticamente; opciones explícitas `brom/fdl1/fdl2` mantienen su ruta habitual. Al identificar BootROM se requieren ambos Loaders; en FDL1 sólo FDL2; en FDL2 no se sube ningún Loader ni se solicita al Provider.

### Configuración Automática de Loader vía PAC

La opción `--pac RUTA` reutiliza el desempaquetador PAC de `FirmwareUnpacker`: selecciona automáticamente SPRD como protocolo y extrae de forma unívoca ambos Loaders a partir de los File-ID `FDL`/`FDL1` y `FDL2`. La dirección de carga se obtiene del archivo de configuración XML bajo `BMAConfig/SchemeList/Scheme/File` según su `ID` (`Block/Base`, admitiendo enteros y hexadecimales `0x`).

Los Loaders se transmiten directamente en streaming como fragmentos `PAC::entry` sin descomprimir el paquete completo a disco ni generar archivos temporales. No se pueden mezclar `--pac` con opciones manuales de Loader o direcciones.

La detección automática envía inicialmente un byte `CHECK_BAUD`: la trama completa de `VERSION` debe coincidir unívocamente con CRC16/XMODEM (BootROM) o checksum FDL (FDL1), seguida de la confirmación `CONNECT ACK`. Una respuesta vacía `UNSUPPORTED_COMMAND` (0xfe) bajo checksum FDL indica un FDL2 ya cargado, confirmando con `DISABLE_TRANSCODE ACK`. Si no hay respuesta dentro de 500 ms, se permite una consulta CONNECT; un `VERIFY_ERROR` vacío bajo CRC16 corrige el handshake hacia BootROM.

El origen de la tabla de particiones toma `auto` por defecto: valida estrictamente la cabecera GPT inicial; si no está disponible, consulta el catálogo nativo. Se puede forzar con `gpt` o `native`.

Las entradas nativas de `READ_PARTITION` contienen nombre y tamaño numérico sin unidades; `--sprd-partition-unit BYTES` indica los bytes por unidad: `capacidadBytes = size * unitBytes`. En modo GPT no se requiere este parámetro ya que la capacidad se calcula directamente a partir de la geometría LBA.

La disposición de longitud por defecto es `--sprd-length 32` (o `64`/`64-reserved` si el FDL lo soporta).

`--sprd-disable-transcode` envía explícitamente `DISABLE_TRANSCODE` para desactivar el escape de bytes tras recibir ACK.

## API del Host

### Detección de Dispositivos

`SprdDeviceIdentify` implementa `IDeviceIdentify`, verificando metadatos `1782:4D00` completos. La enumeración de dispositivos USB en Windows sólo devuelve instancias con `Present = TRUE` para evitar puertos COM desconectados antiguos.

`SprdProtocolOptions.EntryStage` tiene por defecto `SprdBootStage.Auto`.

### Origen de Particiones

`PartitionTableSource` es `Auto` por defecto (admitiendo `Native` y `UserPartitionGpt`). `Auto` consulta la tabla nativa sólo si la cabecera fija carece de firmas GPT estándar o si el `READ_START` inicial devuelve `OPERATION_FAILED` (0x84) / `UNSUPPORTED_COMMAND` (0xfe) seguido de un `READ_END ACK` exitoso.

Por defecto `GptSectorSize = null` (`--sprd-sector-size auto`): lee una vez el prefijo `user_partition` y evalúa en memoria las geometrías de 512 y 4096 bytes; se acepta únicamente si una de ellas es estrictamente válida.

### Profile de Descarga Raw

Por defecto `RawDataMode = Disabled`. Con soporte de hardware confirmado, el host puede activar `Version1` o `Version2`, indicando `RawDataFlushSizeBytes` (1 byte a 4 MiB) y `RawDataUsbPacketSize` (8 a 1024).

### Chip UID

`ReadChipUid(ct)` transmite el comando 0x1a esperando 0xab y entre 1 y 256 bytes, entregando un array de bytes independiente.

### Conexión y Almacenamiento

El sondeo inicial CHECK_BAUD espera 500 ms por defecto (`EntryProbeTimeoutMilliseconds`). Tras el ACK de ejecución en BootROM, espera `Fdl1StartDelayMilliseconds = 500` antes del handshake FDL1. La subida de FDL2 utiliza `Fdl2BlockSize = 528` independiente de los bloques de almacenamiento (`TransferBlockSize = 4096`).

```csharp
// transport, fdl1Source, fdl2Source, imageSource y direcciones provistos por el host.
var options = new SprdProtocolOptions
{
    KnownPartitions = new[] { new SprdPartition("boot", confirmedBootBytes) }
};
using var protocol = new SprdProtocol(transport, options, leaveTransportOpen: true);
using var resources = new SprdConnectionResources(
    new SprdLoader(fdl1Source, confirmedFdl1Address),
    new SprdLoader(fdl2Source, confirmedFdl2Address));
protocol.Connect(resources, cancellationToken: cancellationToken);

long written = protocol.WritePartition("boot", imageSource, cancellationToken: cancellationToken);
protocol.ReadPartition("boot", 0, confirmedBootBytes, outputStream, cancellationToken: cancellationToken);

using var boot = protocol.OpenPartition("boot", cancellationToken);
byte[] bytes = new byte[512];
int count = boot.ReadAt(0, bytes);
```

Los nombres de partición son cadenas UTF-16LE terminadas en NUL (hasta 35 code units). `OpenPartition` entrega un `IReadableBlockDevice` de sólo lectura con soporte para lecturas por rangos y no alineadas.

### Exploración de Particiones y Tamaño de Bloques

En sesiones interactivas:

```text
geekflash --pac firmware.pac --sprd-block-size 32768
browse super
ls super/vendor
lp info super
read super/vendor/build.prop vendor-build.prop
```

El parámetro `--sprd-block-size BYTES` configura `TransferBlockSize` (rango 1 a 65534, 4096 por defecto; admite 32768 en dispositivos compatibles). Ambos Loaders utilizan bloques de 528 bytes de forma independiente.

La decodificación comprueba presupuestos y cancelaciones cada 256 bytes en buffer para optimizar la latencia. Las notificaciones del SerialPort emplean `TryEnter` sin bloqueos invertidos con el stream de .NET, respaldadas por un sondeo de hasta 100 ms.

En pruebas reales sobre iPlay40 con 32 KiB fijos, BootROM explícito y `--sprd-disable-transcode`, la partición boot de 35 MiB se leyó en 13.374 segundos (~2.62 MiB/s) con SHA-256 coincidente con la copia de respaldo; en misc, la lectura a 32 KiB rindió 2.807 MiB/s frente a 0.291 MiB/s a 4 KiB:

```text
geekflash --pac firmware.pac --sprd-entry brom --sprd-disable-transcode --sprd-block-size 32768
```

## Manejo de Fallos y Restricciones

Respuestas NAK, tramas corruptas, fallos de verificación, timeouts o cancelaciones durante transferencias colocan la sesión en estado Faulted y cierran el transporte. No se reintentan escrituras con resultado desconocido. Primero se invoca `Disconnect` y luego se reconecta con la etapa adecuada. Errores de validación de parámetros previos a la I/O preservan la sesión.

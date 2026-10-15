# Guía de Uso de Qualcomm CLI

El ejecutable de la CLI es `geekflash`, compilado para .NET 10; las bibliotecas de protocolos tienen como destino .NET 8. `geekflash --help` muestra con prioridad los comandos generales, `geekflash help all` visualiza todas las opciones de conexión y sintaxis del host, y `geekflash devices` únicamente enumera dispositivos.

En sesiones interactivas, tras ejecutar con éxito `reboot system|download|poweroff`, la CLI se cierra directamente liberando adecuadamente la sesión y el transporte; lo mismo aplica para `power reset|reset_to_edl|off` y `qcom power`. Los fallos o errores de argumentos no provocan el cierre del programa.

## Conexión y Recursos

```powershell
geekflash --port COM7 --loader programmer.elf info
geekflash --usb 05c6:9008 --loader programmer.elf --read-timeout 5000 --write-timeout 5000 info
geekflash --protocol qcom --device-wait-timeout 30000 --loader programmer.elf interactive
```

## rawprogram y patch XML

### PROGRAM Escrito Mediante PATCH

El modo predeterminado es `--program-write-mode auto`: utiliza inicialmente PROGRAM, y únicamente si recibe un NAK explícito y completo de "comando no soportado" antes de enviar los datos de la imagen (sin haber entrado en rawmode), la escritura actual y las posteriores de PROGRAM en esta sesión cambiarán a PATCH. Un NAK ordinario, error de autenticación, timeout, respuesta parcial, error durante transferencia Raw o fallo final de ACK no activan el fallback automático; rechazos desconocidos seguirán reportando error, pudiendo forzar `patch` de forma explícita. Se reconocen en los logs o razones cadenas completas como `Unsupported command: program`, `Unknown command: program`, `program is not supported`, `program command is not supported`, `no handler for program` (ignorando mayúsculas/minúsculas, espacios y puntos finales).

```powershell
# Forzar escritura de misc mediante PATCH (si la imagen mide 8192 bytes, en sectores de 512/4096 se envían 1024 comandos PATCH de 8 bytes)
geekflash --port COM7 --loader programmer.elf --program-write-mode patch write misc misc.img
# Forzar mantener la ruta PROGRAM original, desactivando el fallback automático
geekflash --port COM7 --loader programmer.elf --program-write-mode program write misc misc.img
```

El modo abarca `write/program`, elementos PROGRAM con origen de imagen en rawprogram, Sparse Raw/Fill y escrituras en dispositivos de bloques; los sectores Don'tCare de Sparse se siguen omitiendo, y el relleno final junto con el offset del archivo fuente permanecen inalterados. El atributo `filename="DISK"` en PATCH indica escritura en el dispositivo físico, sin modificar archivos locales ni generar patch.xml. Cada instrucción escribe hasta 8 bytes en formato little-endian por sector; de ser necesario usa 4, 2 o 1 byte. Cada comando espera ACK acumulando el progreso en bytes, completándose sólo tras la confirmación de todos. La consola muestra resúmenes y avisos de fallback; el archivo de log incluye la dirección, longitud, índice, ACK acumulado y duración de cada PATCH, sin registrar el valor escrito, el contenido del archivo, tokens ni el XML completo.

PATCH requiere viajes de ida y vuelta comando por comando, siendo sensiblemente más lento que PROGRAM; no debe utilizarse involuntariamente para imágenes grandes. Ante fallos o cancelaciones intermedias el proceso se detiene sin reintentar ni revertir lo ya escrito; resultados indeterminados requieren reconexión. Opciones de PROGRAM incompatibles con NAND, spare, ECC, skip-bad-block o last-sector rechazan la conversión. La autenticación, Digest/VIP y hooks de fabricante se siguen ejecutando, y la tabla de firmas debe coincidir con las tramas PATCH resultantes.

En Core se utiliza `new QcomProtocolOptions { ProgramWriteMode = FirehoseProgramWriteMode.Patch }`; las variantes `Program`, `Auto` y `Patch` comparten implementación síncrona/asíncrona.

Una vez conectado, ingresa la ruta del archivo o un comodín:

```text
rawprogram rawprogram*.xml
patch patch*.xml
```

Ingresar directamente `rawprogram0.xml`, `rawprogram*.xml`, `patch0.xml` o `patch*.xml` es igualmente válido; `qcom rawprogram ...`, `qcom patch ...` y `program rawprogram*.xml` también están soportados. Rutas con espacios deben ir entre comillas, por ejemplo `rawprogram "D:\ROM\factory images\rawprogram*.xml"`. En modo no interactivo se puede usar `geekflash --port COM7 --loader programmer.elf --non-interactive rawprogram "D:\ROM\images\rawprogram*.xml"`.

Cada comodín sólo busca en el nivel de directorio actual, ordenando los resultados alfabéticamente; múltiples argumentos conservan su orden explícito y archivos duplicados sólo se ejecutan una vez. Las imágenes se resuelven relativas a la carpeta del XML, admitiendo subdirectorios pero prohibiendo rutas que escapen de dicha carpeta. Se pre-inspecciona la estructura, parámetros, imágenes y planificación Sparse de un XML antes de ejecutarlo en el orden del documento; múltiples XMLs se validan y ejecutan de forma sucesiva sin soporte transaccional o de rollback.

Los comandos adaptados en rawprogram incluyen `program`, `patch`, `erase`, `nop`, `setbootablestoragedrive`, `fixgpt`, `xblgpt`, `getstorageinfo`, `getsha256digest`, `benchmark`, `power`, `firmwarewrite`. Comandos no adaptados se omiten resumiendo la causa. Si el dispositivo reporta formalmente su lista de comandos admitidos, se omiten aquellos no declarados. `power` debe ser el último comando y finaliza la sesión al tener éxito.

Líneas de program con filename vacío no escriben nada. Raw se posiciona mediante `file_sector_offset * SECTOR_SIZE_IN_BYTES`, rellenando con ceros únicamente el último sector de los datos reales sin llenar de ceros toda la partición. Sparse se detecta por la cabecera real del archivo.

El progreso de escritura muestra la partición actual, LUN y archivo de imagen, por ejemplo `Escribiendo partición boot_a (LUN 0) | Archivo boot.img`.

El progreso utiliza un diseño multilínea detallando nombre, cantidad/porcentaje, velocidad media y tiempo transcurrido en milisegundos (`00:00:00.125`). La velocidad media de la partición se calcula sobre los bytes reales divididos entre el tiempo monotónico desde el inicio hasta el ACK final. Cada XML y lote global cuenta con un resumen independiente.

Elementos omitidos detallan índice, comando, partición, LUN, sector inicial, cantidad de sectores y causa. Expresiones como `NUM_DISK_SECTORS` se evalúan y muestran numéricamente junto con la expresión original.

Los archivos patch solo transmiten comandos con filename=DISK, sin alterar imágenes GPT locales. Soportan expresiones numéricas, hexadecimales, sumas/restas con `NUM_DISK_SECTORS` y `CRC32(start,length)`. LUNs, tamaños de sector, capacidades, offsets y operaciones enteras comprueban límites estrictos; cualquier fallo o cancelación detiene la operación exigiendo reconexión.

## Visualización de Comandos y Edición Interactiva

La conexión, info y help muestran la intersección de capacidades implementadas con el formato "Comandos del dispositivo => Comandos del Host", alineados por longitud.

```text
  benchmark               => benchmark
  program                 => write / rawprogram
  read                    => read / partitions / browse
  setbootablestoragedrive => setbootablestoragedrive
```

La línea de comandos interactiva admite navegación con ↑/↓ por los últimos 200 comandos, autocompletado cíclico con Tab/Shift+Tab, edición con ←/→, Home/End, Backspace/Delete y limpieza con Esc. El historial reside únicamente en memoria.

La detección automática solo acepta dispositivos COM identificados como Qualcomm EDL; identificadores VID/PID personalizados utilizan `--usb`. Los timeouts se expresan en milisegundos:

| Parámetro | Por defecto | Finalidad |
| --- | ---: | --- |
| `--device-wait-timeout` | 30000 | Presupuesto total de espera para dispositivos hot-plug |
| `--connect-timeout` | 1500 | Configuración de sondeo inicial de protocolo |
| `--resource-timeout` | Sin límite interactivo; 15000 no interactivo | Solicitud de Loader, Digest, Sign o recursos de autenticación (`-1` sin límite) |
| `--read-timeout` / `--write-timeout` | 10000 | I/O síncrono individual |

Ctrl+C surte efecto en límites de bloques de datos y tramas de respuesta; llamadas síncronas bloqueadas esperan como máximo el timeout unitario actual.

Al completar la detección Sahara, antes de subir el Loader se imprime la identidad completa del hardware (fabricante, SoC, número de serie en hexadecimal con prefijo `0x` y `PkHash` completo devuelto por `ReadOemPkHash`).

La estrategia de comunicación se determina en orden: `--vendor` → evidencia de arranque Firehose → fabricante del Loader aceptado → Sahara OEM. Si todo es desconocido, se listan los fabricantes antes de Configure y autenticación (se puede elegir `Generic` o cancelar).

## Patch PBL

Durante la conexión, la CLI coteja los identificadores de chip Sahara para SDM845, SDM710 y SM6125 (Snapdragon 665). Tras validar el Loader, muestra "Aplicando Patch PBL" y lo ejecuta automáticamente.

710 / 845 completan 175 interacciones con fragmentos internos, cierran el transporte, esperan 1000 ms y reabren para subir el Loader. 665 realiza hasta 100 interacciones y envía seis bloques de recursos extraídos de capturas; tras el segundo Hello conserva el primer READ_DATA64 continuando con la subida del Loader del usuario sin cerrar el puerto. Peticiones de Loader tras el patch se limitan a rangos del archivo sin superar 4 MiB por bloque.

En Core se activa mediante `QcomProtocolOptions.EnablePblPatch = true` (por defecto false).

## Modos Oplus

Ambos modos Oplus se ejecutan antes de Configure y la consulta de almacenamiento: Log de arranque → Digest → verify XML → Sign (rellenado con ceros hasta 4096 bytes) → verify passed → sha256init → Configure. Fallos en Digest, timeouts de Sign o tramas parciales detienen la conexión inmediatamente.

La inicialización Legacy transmite `<verify EnableVip="0"/>` sin `value="ping"`. Verify, sha256init y Configure utilizan `<?xml version="1.0" encoding="UTF-8" chimerais="power" ?>` con espacio final. Pt conserva `verify value="ping" EnableVip="1"` y asignación de particiones.

Legacy envía sha256init tras confirmar Sign, esperando hasta 1500 ms. Si este comando solo produce el registro `ERROR: Failed to run the last command -1` sin ACK y luego continúa a Configure, se conserva intencionadamente emitiendo un Warning.

Legacy autoconfigura iniciando desde UFS con ZlpAwareHost=1, SkipWrite=0, SkipStorageInit=0, MaxPayloadSizeToTargetInBytes=1048576, MemoryName=ufs. Pt mantiene mapeo por particiones y validación de permisos. Al indicar solo `--oplus-digest`, se selecciona Pt por defecto; ambos modos requieren suministrar Sign binario mediante `--oplus-sign`.

```powershell
geekflash --port COM7 --loader programmer.elf --oplus-digest Digest.bin --oplus-sign Sign.bin --non-interactive info
geekflash --port COM7 --loader programmer.elf --oplus-mode OplusDigestLegacy --oplus-digest Digest.bin --oplus-sign Sign.bin --non-interactive info
```

### Reanudar Loader en Espera del Primer Digest

Si el Loader fue subido y emitió `VIP is enabled, receiving the signed table`, el dispositivo ya salió de Sahara. Si la CLI se cerró al ingresar Sign, reabrir el puerto no repetirá los logs de arranque.

Si el hardware sigue esperando el primer Digest y aún no se envió ninguno, se puede reanudar explícitamente:

```powershell
geekflash --port COM77 --vendor Oplus --oplus-mode OplusDigestLegacy --oplus-digest "D:\firmware\Digest.bin" --oplus-resume
```

Esto salta Sahara y logs iniciales, enviando Digest directamente, seguido de Verify → Sign → sha256init → Configure. En Core corresponde a `OplusDigestConfiguration.ResumeAwaitingDigest`.

### Conteo de Paquetes Legacy

Tras confirmar el Digest inicial, el contador inicia en cero; cada XML y transferencia de salida completa cuenta como 1. Al alcanzar capacidad 53, en los conteos 51/52/53/54 se envían 3/2/1/0 comandos NOP antes del Digest del paquete 55.

Errores de verificación de Sign completos y recuperables permiten un reintento manual; el presupuesto para leer advertencias NAK es de 1000 ms. Si vuelve a esperar la tabla de firmas, se reenvía Digest, verify y el nuevo Sign.

## Logs de Archivo para Depuración

Por defecto, se genera un archivo de log único en la carpeta `logs` del ejecutable, imprimiendo su ruta completa al iniciar. El archivo contiene información de nivel Debug (nombres de comando, bytes enviados, respuestas/rawmode, presupuestos, duraciones y trazas completas de excepciones) sin necesidad de `--verbose`.

```powershell
geekflash --loader programmer.elf --oplus-mode OplusDigestLegacy --oplus-digest Digest.bin --oplus-sign Sign.bin --log-file .\logs\oplus-debug.log info
```

La consola muestra información en español (etapas, datos de dispositivo, selección de recursos, progreso y resultados breves). Excepciones solo muestran un aviso conciso con la ruta del log; la traza detallada permanece en el archivo.

## Lectura de Archivos

```powershell
geekflash --port COM7 --loader programmer.elf --non-interactive read boot boot-backup.img
geekflash --port COM7 --loader programmer.elf --non-interactive read sector 0 0 34 gpt.bin
```

La lectura escribe en un archivo temporal único en la carpeta destino, reemplazándolo atómicamente solo tras el éxito definitivo del protocolo. Cancelaciones o fallos eliminan el archivo temporal.

## Montaje de LP, Lectura/Escritura de Particiones y Edición de Metadatos

En el prompt interactivo escribe `browse <partición> [lun] [lp-slot]`, por ejemplo `browse super`, `browse super 0 1` o `browse system_a`. `browse help` muestra la ayuda del explorador. También se puede acceder directamente desde la línea de comandos:

```powershell
geekflash --port COM7 --loader programmer.elf browse super
geekflash browse-image "D:\images\super.img"
geekflash browse-image "D:\images\system.img"
geekflash browse help
```

`browse-image <raw-image> [lp-slot]` no requiere dispositivo y admite imágenes brutas de LP, EROFS o Ext (no procesa contenedores Android Sparse directamente). En el hardware se utiliza segmentación por rangos de GPT y asignaciones por extent de LP sin volcar la imagen completa de Super previamente.

Comandos disponibles en el explorador:

```text
cd /super/system_a/etc
ls
up
cd ..
pwd
print /super/system_a/etc/settings.conf
read /super/system_a/etc/settings.conf "D:\backup\settings.conf"
find --all *.conf /super/system_a "D:\backup\system_a"
find build.prop /super
exit
```

`up` y `cd ..` retroceden un nivel, pudiendo salir sucesivamente del directorio, del sistema de archivos, del contenedor LP y llegar a la raíz virtual `/`. `print <ruta>` muestra el texto de archivos regulares pequeños (hasta 24 KiB) directamente en el terminal. `read <ruta> <destino>` extrae archivos individuales o imágenes brutas de partición completa (incluso si está montada como Ext/EROFS). `find <patrón> [ruta] [destino]` busca recursivamente en sistemas de archivos EROFS/Ext.

Presionar Ctrl+C durante una búsqueda sólo cancela dicha búsqueda, devolviendo el control al explorador tras recibir la última confirmación pendiente del hardware.

También se puede interactuar directamente desde el prompt principal sin entrar en `browse`:

```text
ls super
ls super/system_a
read super/system_a system_a.img
write super/system_a system_a.img
read super/system_b system_b.img 0 1
```

La escritura soporta imágenes Raw y expansión de Android Sparse, rellenando con ceros el espacio sobrante si la imagen es menor a la partición. Si excede la capacidad, se rechaza antes de escribir, requiriendo usar `lp resize`.

La edición de metadatos LP se realiza mediante `lp`:

```text
lp info super
lp rename super system_a system_new
lp resize super system_new 4294967296
lp attributes super system_new ReadOnly
lp group-add super dynamic 8589934592 None
lp move super system_new dynamic
lp group-resize super dynamic 9663676416
```

Están disponibles además `lp add`, `lp remove`, `lp group-rename`, `lp group-flags` y `lp group-remove`. Se verifican nombres, capacidades y espacio disponible antes de aplicar cambios, validando las copias principal y secundaria mediante readback.

# Compatibilidad de Subida y Diagnóstico de MediaTek

Este documento aborda el protocolo estándar de MediaTek, así como la integración y logging de MTK en la CLI. Abarca el enrutamiento de callbacks DA1→DA2, sin alterar los protocolos de Qualcomm o SPRD.

## Compatibilidad de Subida

La subida en BROM se realiza por defecto escribiendo bloques de 65536 bytes del host en streaming, sin enviar un ZLP final redundante. El backend USB se encarga de los paquetes físicos; los bloques del host no equivalen al tamaño máximo de paquete del endpoint. Se preservan el relleno de bytes impares con ceros, verificación XOR, estados finales y la política de no reintentar fallos.

```text
geekflash --protocol mtk --loader DA.bin --verbose --log-file D:\logs\mtk.log info
```

Únicamente si la compatibilidad del dispositivo lo exige de forma explícita, se puede restaurar el formato de escritura anterior:

```text
--mtk-brom-chunk 64 --mtk-brom-zlp
```

En el SDK esto corresponde a `MtkProtocolOptions.BromUploadChunkSize` (0 usa BufferSize, otros valores de 64 a 1048576) y `BromUploadZeroLengthPacket` (por defecto false). Estas opciones sólo afectan la subida de DA/certificados/datos de autenticación en BROM, sin alterar el flujo FLOW/checksum/ACK de XFlash. La recepción de paquetes raw/nombrados en XFlash utiliza el tamaño de paquete de lectura negociado; tramas que excedan la longitud son rechazadas sin reenviar comandos.

## Estratificación de Logs

- Interfaz de usuario por defecto: conexión, DA, EMI, evidencias de autenticación, geometría de almacenamiento y eventos de inicio/fin de lectura, escritura y borrado; fallbacks recuperables emiten Warning. Borrados nombrados no infieren capacidades.
- `--verbose`: añade información Debug detallada sobre comandos, longitud de escrituras USB, tramas, ACK/estado, fragmentos leídos, presupuestos, tiempos y contexto de fallos.
- Durante la salida de herramientas (particionado, I/O, explorador) se suprimen logs de alta frecuencia Info/Debug, permitiendo resúmenes Warning/Error aprobados.
- Logs a archivo: nivel Information y superior por defecto; `--verbose` recolecta Debug; configurable mediante `--log-file`. En el SDK se recomienda configurar Serilog **antes** de instanciar `MtkProtocol`, asociando eventos mediante `MtkSessionId`.
- No se registran payloads en bruto, firmas, challenges, tokens, valores de checksum, identificadores privados ni el texto completo de mensajes del dispositivo.

"Subida de DA verificada" indica únicamente aceptación de checksum/estado, no éxito de autenticación ni disponibilidad de almacenamiento. `Unsupported` o `NotRequired` no deben interpretarse como autenticación exitosa del host. Los tamaños en Sparse reflejan rangos lógicos de imagen (los huecos conservan su contenido), y las escrituras Raw incluyen relleno de alineación; no representan estadísticas de tráfico físico USB.

## Timeouts y Desconexiones

Los candidatos de Preloader (0E8D:2000/6000) envían primero una activación A0 antes de ejecutar el handshake de cuatro pasos; un buffer de 1024 bytes procesa respuestas `READY` duplicadas o fragmentadas. BROM no agrega activación, manteniendo su flujo habitual. `MaximumHandshakePrefix` tiene un valor por defecto de 64 (rango 0 a 1024).

### Ventana EMI en XFlash

La ruta XFlash con `--mtk-preloader` conserva la ventana `MTK_BLOADER_INFO` completa (con cabecera), no limitándose a enviar los datos Legacy `MTK_BIN+12`. La ventana correcta para Ares v51 es de 448 bytes (en lugar de 336); la trama de longitud y la carga de FLOW deben coincidir exactamente, esperando al final el estado del grupo.

Antes de la identificación completa de FD, fallos transitorios en USB o en el handshake inicial liberan el candidato y continúan esperando (cancelable con Ctrl+C). Tras la identificación, fallos en autenticación, DA o I/O en curso donde no se pueda confirmar el límite de comando invalidan la sesión anterior requiriendo reconexión. Un timeout simplemente indica que el presupuesto expiró, no garantiza que el hardware haya sido desconectado.

Al depurar, verificar: `SessionState`, `BootStage`, `CommandName`, `Command`, `Status`, `ExceptionType`, `ReceivedLength`, `ReadFragmentCount`, `ElapsedMilliseconds`, `TimeoutMilliseconds` y el tamaño negociado de paquete.

Respuestas USB IN de cero bytes exitosas en DA1/DA2 pueden representar un ZLP, sin interpretarse inmediatamente como EOF; conexiones abiertas consumen hasta 4 paquetes cero consecutivos bajo el mismo presupuesto sin reenviar comandos. Longitud FLOW de 0 en el protocolo no es un ZLP USB y es ilegal.

## Modificación de DA1 y Enrutamiento de DA2

Si el reemplazo devuelto por el callback BeforeDa1 modifica bytes no firmados de DA1 con la misma disposición de ejecución, y la subida/inicialización estándar de DA1 concluye con éxito, la ruta BROM omite pasos innecesarios de Carbonara. Preloader y archivos pre-modificados provistos directamente por el host no realizan deducciones automáticas.

El resumen de DA2 distingue entre la solicitud de BootTo estándar y la confirmación de grupos de parámetros y estados de ejecución. Si tras superar ambos estados la consulta `SlaEnabledStatus` falla, corresponde a una consulta de autenticación posterior de DA2 y no a un fallo de subida.

En DAs antiguos, cuando el subcomando SlaEnabledStatus devuelve 0xC0010004 en su ACK inicial, se registra como `Unsupported` en nivel Debug continuando con las consultas de paquetes y almacenamiento, sin interpretarse como autenticación exitosa ni deshabilitación de SLA.

El límite de paquetes del dispositivo en XFlash puede ser de 2 MiB. El host escribe con el tamaño negociado (límite por defecto 2 MiB), independiente del buffer BROM; flujos FLOW grandes se reciben por defecto en solicitudes USB de 64 KiB, consumiendo paquetes cortos directamente y agrupando la salida en buffers de hasta 1 MiB antes de emitir el ACK.

## Progreso de Almacenamiento en XML y Límites de Sesión

Formatos de progreso de pre-borrado como `OK!PROGRESS@100@\0` se reconocen aceptando el delimitador `@`. Es indispensable recibir `OK!EOT`; un 100% no equivale a escritura completada.

En entradas XML normales, tras recibir un `CMD:END` estructuralmente completo se emite primero un ACK; si el ACK es exitoso, se registra el límite de comando, preservando el resultado exacto aunque el END señale fallo. Ante `ERR` explícito, se consumen eventos conocidos de progreso/END hasta retornar al límite de comando.

Errores de validación de parámetros, recursos, tablas de partición o digest de seccfg posteriores al límite preservan el estado `StorageReady`, el transporte y la generación de sesión, emitiendo una advertencia de conexión preservada.

Errores recuperables con `MtkProtocolException.RequiresReconnect=false` presentan mensajes descriptivos sin obligar a reconectar.

La recepción de datos XML utiliza `MtkProtocolOptions.MaximumXmlReadPacketLength` (**64 KiB por defecto**; rango 512 a 2 MiB). Invocaciones cortas de IN continúan dentro del presupuesto de la trama sin añadir ACKs ni reenviar comandos. La escritura utiliza de forma independiente `MaximumXmlWritePacketLength` (2 MiB por defecto).

### Comparativa de Transferencia Oficial y Visualización de Velocidad

En capturas de SP Flash Tool V6, DA2 DOWNLOAD-FILE negocia paquetes de 4 KiB y paquetes de flasheo de 2 MiB, coincidiendo con nuestra arquitectura habitual. Las herramientas oficiales emplean COM; el tamaño en archivos DA no dictamina el tamaño de las peticiones nativas IN bajo LibUsb.

Durante la I/O, la CLI visualiza `Velocidad de transferencia: … (media del proceso)`, calculada a partir de los bytes reportados entre el tiempo transcurrido. La `Velocidad media` definitiva sólo se calcula tras la confirmación de finalización (Completed), incluyendo el ACK final de cierre.

### Preparación y Carga de Extensiones DA

La CLI prepara extensiones únicamente a partir de la imagen DA en subida, sin inferir direcciones de funciones a ciegas; advertencias de fallo incluyen códigos `Reason` (ej. `LoaderNotFound`, `FreeNotFound`).

La carga real exige completar BOOT-TO, recibir EXT-ACK en estado OK y finalizar EXT-DA-CTX para habilitar las capacidades de extensión de la generación actual.

En MT6895 (0x1172) se utiliza la dirección base SEJ 0x1c009000 de la tabla de referencia, y en MT6893 0x1000a000; la opción `--mtk-sej-base` tiene prioridad.

## GPT y Regiones de Arranque

El descubrimiento de particiones no lee GPTs de cabecera/cola en eMMC BOOT1/2 o UFS LU0/LU1; el listado muestra directamente `preloader`, `preloader_backup`, `pgpt` y `sgpt`. Tanto en flasheo individual como por Scatter, el Preloader se canaliza mediante las interfaces de nombres de XFlash v5 (`UPLOAD`/`DOWNLOAD`) o XML v6 (`READ-PARTITION`/`WRITE-PARTITION`), gestionando la cabecera en el DA sin escribir en bruto sobre BOOT.

Scatter→GPT sólo preserva particiones de USER, excluyendo asignaciones auxiliares y calculando límites NEEDRESIZE sobre el área final reservada. La región User en UFS corresponde a wire3 (LU2), y en eMMC a wire8, sin reemplazarse por números de LUN base cero.

Para disposiciones UFS 4K con FirstUsable 34 y particiones válidas a partir de LBA 8, se verifica estrictamente con el final de metadatos en LBA 6.

# MediaTek CLI

## Conexión y Reconexión

```text
geekflash --protocol mtk --loader DA.bin --mtk-preloader preloader.bin
```

Si no se especifica un comando, se entra en modo interactivo y los comandos exitosos mantienen la sesión activa; ante un fallo de operación se conserva el prompt, pero un resultado desconocido en la línea invalidará la sesión anterior, impidiendo continuar utilizando el dispositivo de bloques o las extensiones antiguas. Escribe `exit` para salir. Los comandos individuales explícitos finalizan el proceso tras completarse.

La pre-autenticación se selecciona según la etapa confirmada por el handshake completo, y no únicamente por el USB PID o bits de seguridad. BROM mantiene los requisitos de Auth/certificado y SLA del host; Preloader no envía comandos de pre-autenticación BROM; cuando SBC/DAA está activo, exige un DA1 firmado, y la firma original junto con la región completa se transfieren al dispositivo mediante el comando estándar SEND_DA para su verificación. La existencia de firma no implica autenticación exitosa; si el dispositivo rechaza o hay error de checksum, la conexión se detiene sin degradar ni reintentar. Si SEND_DA solicita explícitamente SLA, o el DA1/DA2 posterior exige autenticación, debe proporcionarse un firmante legítimo. El Auth/certificado de BROM adjunto no se envía en la ruta Preloader; la propiedad de recursos sensibles y las reglas de limpieza a cero permanecen inalteradas.

El inicio interactivo de MTK muestra una línea de estado dinámico y el tiempo transcurrido mientras espera la conexión; se puede cancelar con Esc o Ctrl+C. Los errores nativos de USB en enumeración/apertura o inicialización de enlace (incluyendo timeouts de USB que preservan la causa nativa) liberan la conexión fallida y vuelven a esperar el puerto automáticamente, sin necesidad de escribir `reconnect` manualmente; lo mismo aplica al comando manual `reconnect`. El presupuesto explícito de espera no se reinicia por fallos de USB; la espera ilimitada por defecto sigue siendo cancelable. La ausencia de Loader/autenticación, timeouts de recursos, ambigüedad de dispositivo o errores de configuración no aplican esta política de reintento. Tras cancelación u otro fallo, se mantiene el prompt `geekflash[offline]>`, pudiendo ingresar `reconnect`, `help`, `devices` o `exit`. Los comandos de almacenamiento ya ingresados en modo interactivo no se repiten automáticamente. En entornos redirigidos sólo se imprime una línea estática de espera sin consumir entradas posteriores de archivos DA. Los candidatos USB se enumeran aproximadamente cada 100 ms y la vinculación del controlador se comprueba a lo sumo cada segundo.

La animación de espera se detiene por completo antes de pasar el control a Probe/Loader; la fase de conexión mantiene las notificaciones de etapas del núcleo. La cancelación debe esperar a que la llamada nativa USB acotada retorne, no garantizando una interrupción instantánea. La detección entre protocolos sin selección explícita, los comandos individuales y la salida por error no interactiva mantienen su semántica; las sesiones invalidadas no se reconectan automáticamente en segundo plano.

```text
reconnect
reconnect brom
reconnect da1 xflash
reconnect da2 xml
```

La reconexión vuelve a abrir el USB, priorizando la topología física/número de serie original, el DA original y la instantánea de almacenamiento. Auto realiza una comprobación pasiva de bytes de arranque disponibles y los reserva para protocolos posteriores; C0 continúa la verificación mediante la secuencia de inicialización DA1. Si existe un límite de comando limpio y el dispositivo no presenta nuevas tramas de arranque, se puede continuar a lo largo del DA2 original. Las tramas de arranque XML no distinguen por sí solas DA1 de DA2; el USB PID tampoco constituye prueba de etapa; ante información insuficiente se exige confirmación, sin tantear con distintos dialectos sucesivamente.

DA1 continúa con la inicialización/EMI y la subida de DA2, sin reenviar la subida de BROM; DA2 únicamente valida el protocolo correspondiente, el estado de autenticación y el almacenamiento, sin subir el DA nuevamente. Legacy DA2 debe contar con la geometría de almacenamiento observada en la sesión original y verificar el ACK de USB speed; sin geometría o en presencia de NAND, se debe reiniciar a BROM. Al conectar por primera vez con un DA ya existente, si no es posible consultar la identidad completa del hardware, se solicitará el número de hardware, subcódigo, versión de hardware/software, versión de BROM y security raw; el archivo DA debe coincidir exactamente.

Ante un fallo de lectura/escritura a mitad de camino, los comandos no se reenvían automáticamente. Antes de retomar el control de DA2, se debe confirmar que se ha reiniciado o vuelto al límite de comando inactivo; no se deben enviar tramas de consulta a un dispositivo que todavía espera datos de imagen. La confirmación de cancelación no envía comandos de recuperación. En modo no interactivo, si no se puede confirmar la información faltante, se reporta un error inmediatamente. Una escritura interrumpida puede dejar datos incompletos y debe volver a flashearse desde el principio tras reiniciar y reconectar; no se puede reanudar directamente ni interpretar el porcentaje de progreso como un punto de recuperación seguro.

## Transferencia y Logs

- Los paquetes de escritura XFlash siguen la negociación con el dispositivo, con un límite predeterminado de 2 MiB, sin ser truncados por el buffer de 64 KiB de BROM; la recepción de lectura sigue la longitud real de trama sujeta al `ReadPacketLength` negociado. Los 64 KiB por defecto corresponden a la ventana de solicitud USB individual, no al tamaño de paquete DA; sólo tras recibir y volcar la trama de datos completa se confirma con ACK/estado. Los paquetes cortos nativos se consumen directamente sin emitir lecturas pequeñas adicionales para llenar la ventana; la salida a archivo se agrupa hasta un máximo de 1 MiB, siendo el buffer independiente del tamaño total de la imagen. En el SDK se puede restringir el tamaño de paquete de escritura con `MaximumXFlashWritePacketLength`.
- El progreso se actualiza aproximadamente cada 100 ms, preservando los eventos de inicio, primer avance y finalización; no se envían comandos de protocolo en paralelo.
- Sparse sólo realiza una verificación completa previa si incluye CRC; sin CRC no se escanea la totalidad de la imagen expandida. La escritura ordinaria y la escritura por Scatter del host agrupan regiones contiguas de Raw/Fill en transferencias continuas, jamás cruzando DontCare; los huecos no se escriben. El progreso y la velocidad promedio se calculan sobre los bytes realmente transferidos, mientras que la validación de capacidad y longitud retornada se basan en la longitud expandida; la finalización sólo se reporta tras un ACK/estado exitoso definitivo.
- El presupuesto de operación única de MTK en la CLI es de 30 minutos por defecto, configurable mediante `--mtk-operation-timeout milisegundos`. Una escritura ordinaria incluye todas las regiones contiguas; Scatter Apply incluye las copias de seguridad de GPT necesarias, escritura y readback; la selección interactiva de archivos, confirmaciones y pre-inspecciones independientes no comparten este límite de tiempo. En el SDK el valor por defecto sigue siendo de 120 segundos, manteniéndose los timeouts de lectura/escritura USB individuales; imágenes grandes o lecturas de verificación pueden requerir incrementar el presupuesto de operación, en vez de esperar indefinidamente o reenviar escrituras.
- Por defecto, la consola y el archivo registran niveles de Information en adelante; sólo `--verbose` habilita el registro Debug paquete a paquete. Consultas SLA no soportadas en DA antiguos y límites GPT en UFS antiguos previamente validados se registran sólo como Debug, sin relajar la lógica de validación.
- Los offsets de partición muestran el número de bytes brutos; el sector inicial utiliza las unidades lógicas de bloque reales de dicha región; los números de región MTK en `read/write sector` corresponden al wire ID de DA (UFS USER=3, eMMC USER=8), no a un LUN base cero.

Logs reales de usuario en MT6893/UFS confirman la carga exitosa de extensiones, descubrimiento de particiones y lectura de persist; con Debug por paquete activo, la lectura de 69,697,536 bytes tomó unos 1.78 s (37.28 MiB/s), y tras la optimización de paquetes cortos el usuario reportó 1.670 s (39.8 MiB/s). La versión combinada arrojó 1.675 s (39.67 MiB/s). No se deben prometer multiplicadores basados en transferencias simuladas. Solicitudes USB de 1 MiB anteriores mostraron timeouts al final de trama por causas no comprobadas a nivel bajo; no se agranda la ventana automáticamente ni se reintenta en tramas parciales. La CLI actual utiliza unidades binarias KiB/MiB correctas, mostrando la velocidad de transferencia media durante la I/O (bytes reportados / tiempo transcurrido). La velocidad media final incluye la confirmación END; al alcanzar el 100% de la fuente, se continúa mostrando la espera hasta recibir Completed. El cotejo con el paquete oficial de 2 MiB y el retardo de finalización de ~0.53 s se detalla en el documento de [Diagnóstico MTK](mtk-diagnostics.md).

Pruebas offline con `super.img` confirmaron una longitud expandida de ~8.5 GiB, con Raw/Fill real de ~2.48 GiB sin CRC. La implementación anterior realizaba validación completa de la imagen expandida produciendo 2690 escrituras por región; la planificación por regiones contiguas requiere únicamente 7 operaciones sin materializar imágenes grandes en memoria. La nueva planificación offline toma ~24 ms con asignaciones menores a 1 MiB.

## Comandos y Operaciones de Seguridad

El punto de entrada unificado es `mtk <subcomando>`, manteniendo compatibilidad con `mtk-*` y la sintaxis geométrica explícita. La lista completa está disponible en `help mtk`. Las operaciones comunes no requieren indicar región, offset ni longitud:

```text
mtk capabilities
mtk seccfg unlock
mtk seccfg lock --backup seccfg-original.bin
mtk slot read
mtk slot set a
mtk rpmb info
mtk rpmb read rpmb.bin
mtk rpmb write rpmb.bin --start 0 --count 128 --key-file rpmb-key.bin
mtk rpmb erase --region 0 --start 0 --count 128
mtk rpmb auth rpmb-key.bin
mtk rpmb-lock read
mtk key Rpmb rpmb-key.bin
mtk partition read boot_a boot_a.img
mtk efuse read efuses.bin
```

`seccfg` se localiza a partir de la única partición `seccfg` en USER; `slot` se ubica desde `misc` o `para` en USER. La ausencia o ambigüedad es rechazada de inmediato; se puede usar `--partition` para especificar el nombre real sin recurrir a offset 0. `seccfg` realiza un backup persistente previo, solicita confirmación con `yes`, compara contra los datos originales, escribe con alineación mínima y efectúa un readback completo. `slot` también requiere confirmación y guarda el sector original antes de escribir. Los respaldos automáticos se guardan en la carpeta `backups` del directorio actual con marca de tiempo e identificador aleatorio; las copias explícitas no sobrescriben archivos existentes.

La extensión DA validada para MT6895 utiliza la dirección base SEJ 0x1c009000 de la tabla de chips, mientras que MT6893 utiliza 0x1000a000; la base explícita tiene prioridad. Los chips desconocidos no se infieren. El digest original de seccfg debe coincidir con SHA/SW/hardware para permitir modificaciones. Errores locales de algoritmo/digest tras un READ-FLASH END/ACK preservan la sesión estándar, permitiendo continuar con particiones/lecturas. Un error explícito reportado por el hardware (`ERR`) devuelve fallo tras confirmar los eventos END/progreso, sin simular éxito; tramas parciales, desconexiones o cierres incompletos invalidan la sesión.

RPMB se gestiona de forma independiente a los dispositivos de bloques ordinarios, con bloques fijos de 256 bytes; región 0 y bloque inicial 0 por defecto. Lectura y borrado cubren el rango restante de la capacidad confirmada; la escritura toma el número de bloques del archivo de entrada, requiriendo alineación exacta con `--count`. En eMMC la capacidad se toma del reporte del hardware; en UFS, ante capacidad desconocida, se debe suministrar interactivamente el conteo confirmado o mediante `--mtk-ufs-rpmb-blocks`. No se utilizan capacidades fijas ni se deduce la capacidad a partir del archivo de entrada.

Al omitir `--key-file` se reutiliza la autenticación de la región actual o se solicita el servicio de derivación de RPMB de Penumbra2; la clave derivada reside exclusivamente en memoria y se limpia a cero inmediatamente. Si la clave del fabricante no coincide, debe proporcionarse el archivo de clave de 32 bytes; no se prueban múltiples claves a ciegas. Escrituras/borrados en RPMB y escrituras de rpmb-lock en XML/UFS realizan backup persistente antes de la confirmación; resultados desconocidos no se reintentan.

`mtk partition read|write <nombre> <archivo>` toma el límite superior a partir de la longitud de la partición única; sin tabla de particiones se requiere `--maximum` explícito sin suponer el tamaño total del disco. Operaciones de memoria, registros, relleno y RSC conservan parámetros obligatorios de dirección/rango. La escritura en eFuse requiere backup y confirmación explícita (el quemado puede ser permanente y no es reversible). En modo no interactivo, la falta de capacidad o confirmación genera rechazo.

## Preloader y Extensiones

La conexión Preloader envía inicialmente una señal de activación A0. Si el primer paso no recibe respuesta y continúa recibiendo `READY` completo, se reenvía A0 de forma acotada únicamente al agotarse el buffer de recepción actual (hasta un máximo de 5 veces); no se repiten los pasos subsiguientes del handshake, consultas FD ni subidas de DA. El prefijo de arranque admite hasta 1024 bytes por defecto bajo el mismo límite de tiempo del handshake; en el SDK, `MaximumHandshakePrefix` impone el límite superior estricto. BROM no realiza sincronización READY, conservando su comportamiento habitual de lectura/escritura USB.

```text
read preloader preloader.bin
read preloader_backup preloader-backup.bin
write preloader preloader.bin
write preloader_backup preloader.bin
```

La lista de particiones muestra directamente `preloader`, `preloader_backup`, `pgpt`, `sgpt`; son asignaciones auxiliares, no entradas del GPT de USER; alias como `PreloaderBackup`, `PrimaryGPT`, `BackupGPT` siguen siendo aceptados como entradas.

En XML, las consultas de archivo para escritura nombrada solo acceden a recursos virtuales del comando actual: `EXISTS` para `<partición>.bin` devuelve existencia y `FILE-SIZE` devuelve el tamaño del archivo; un `<partición>.sig` no provisto devuelve `NOT-EXISTS`. No se leen archivos arbitrarios locales ni se cargan firmas adyacentes automáticamente; si el dispositivo exige firma se reporta error, sin degradar a escritura en bruto ni reintentar.

XFlash v5 utiliza nombres de partición `UPLOAD`/`DOWNLOAD`, consultando primero la longitud del paquete y completando según `START_DL_INFO → DOWNLOAD(nombre, longitud) → checksum/data/estado → END_DL_INFO`; XML v6 emplea `READ-PARTITION`/`WRITE-PARTITION` con ciclo completo END/ACK. El encabezado de arranque es procesado por el DA; el host no agrega encabezados ni rellena con ceros. La lectura tiene como límite la capacidad BOOT reportada, extrayendo la longitud exacta devuelta por el DA. En caso de rechazo, no se degrada a regiones sin procesar; Legacy rechaza este punto de entrada al no inferir disposiciones de encabezado.

Tras conectar, se confirma offline si el DA2 incluye cargador de extensiones, resolviendo las direcciones de funciones de Penumbra, rellenando la tabla de punteros embebida y subiendo mediante BOOT-TO para verificar ACK/contexto. Solo si todos los pasos concluyen con éxito se reporta "DA extensiones cargadas". Sin cargador, UART o direcciones válidas, no se realiza la subida, manteniendo las capacidades estándar de almacenamiento. La detección Thumb2 en XFlash DA2 es independiente de la arquitectura ARM de DA1.

La ABI interna por defecto es Penumbra2. Para extensiones antiguas se puede especificar `--mtk-extension-abi legacy`. `RequiresExtension` en `IMtkProtocol.Capabilities` indica necesidad del servicio opcional por parte del núcleo, no el resultado de carga; `mtk-capabilities` utiliza las capacidades verificadas de `MtkDaExtension.Capabilities`. Al reconectar en DA2 con extensiones cargadas previamente, se revalida ACK/contexto sin volver a subir código.

## Scatter

```text
mtk scatter
mtk scatter MT6893_scatter.txt
mtk scatter plan MT6893_scatter.txt
mtk scatter flash MT6893_scatter.txt --partitions boot_a,super
mtk scatter flash MT6893_scatter.txt --verify true
mtk scatter flash MT6893_scatter.txt image-directory --backup backup-directory
mtk scatter update MT6893_scatter.txt
```

Al invocar sin archivo se solicita interactivamente el archivo txt/xml; al indicarlo, se flashea según sus elementos de descarga, resolviendo las imágenes de forma relativa al directorio del Scatter sin obligar a especificar una carpeta de imágenes. El directorio de backup predeterminado se genera automáticamente bajo `backups`; la sintaxis clásica `flash|update archivo directorio_imagenes directorio_backup` sigue siendo compatible. `plan` únicamente inspecciona la disposición sin pre-verificar imágenes ni realizar backups o escrituras. Archivos Scatter mixtos EMMC/UFS de fábrica sólo utilizan el medio confirmado por el dispositivo.

El parser YAML admite asignaciones de particiones planas o escalares en secuencias `description`, en cualquier orden de campos, sin exigir `partition_index`. Registros incompletos reportan error. En YAML/XML, la omisión de `operation_type` toma por defecto `INVISIBLE`. Se aplican límites de longitud, profundidad y cantidad de particiones. `UFS_LU0_LU1` solo asigna LU0 según la ABI de compatibilidad de referencia.

El flasheo solo detalla las particiones cuya disposición no coincida con el dispositivo en lugar de listar todas las particiones idénticas; una vez pre-verificadas todas las imágenes seleccionadas, se compara la disposición y se solicita confirmación con `yes`. `--partitions` filtra exclusivamente particiones descargables del archivo sin alterar el esquema global. En modo no interactivo se cancela si no hay confirmación.

La disposición completa de USER en eMMC/UFS se compara contra el dispositivo por nombre, offset y longitud. Ante diferencias, se muestran las discrepancias y se solicita permiso para respaldar el GPT principal/secundario actual, actualizar la tabla y continuar. Solo tras ingresar `yes` se guardan ambas copias del GPT actual; si el respaldo es exitoso, se escribe el nuevo GPT secundario, GPT principal y las imágenes. Disposiciones idénticas no reconstruyen el GPT, preservando GUIDs y atributos. Al seleccionar imágenes GPT en bruto también se respalda la tabla actual previamente.

Reconstruir el GPT impide descargar simultáneamente imágenes de `pgpt`, `sgpt`, `PrimaryGPT` o `BackupGPT` en USER; este conflicto se rechaza antes de cualquier backup o escritura para evitar sobreescrituras accidentales.

La CLI respalda únicamente la tabla de particiones, no todas las particiones a flashear, ni datos de Protected o BinRegion. Cambios de disposición pueden inutilizar los datos originales; el respaldo de GPT no recupera contenidos sobreescritos (se advierte explícitamente en el prompt). La verificación post-escritura por readback está desactivada por defecto; para activarla vía SHA-256 en streaming por rango, especifica `--verify true`. La actualización nativa XML `FLASH-UPDATE` no admite comparación en el host. Los encabezados de arranque administrados por DA no se comparan byte a byte en el host.

En el SDK, `MtkScatterService.Apply` mantiene su comportamiento original con readback desactivado por defecto; se puede habilitar con `new MtkScatterService(protocol, verifyAfterWrite: true)`. Para adoptar la política ligera de la CLI, se utiliza `ApplyWithBackupPolicy` con `MtkScatterBackupPolicy.PartitionTableOnly`.

El elemento Preloader en Scatter se procesa obligatoriamente mediante la interfaz nombrada sin escribir en bruto sobre BOOT; se conserva el comportamiento de `BOOTLOADERS`. En eMMC, un Preloader de arranque dual se planifica hacia `preloader` y `preloader_backup`. Al hacer backup total en el SDK, el Preloader se lee por nombre guardando la longitud real devuelta.

NAND no utiliza GPT, manteniendo la ruta Scatter nativa XML con `update` explícito sin pre-respaldo de todas las particiones en el host; las peticiones del DA para subir/descargar migraciones deben responderse y guardarse. Esta actualización nativa no admite `--partitions`. Los `BOOTLOADERS` gestionados por DA utilizan la interfaz nombrada nativa y sus estados finales.

Conversión offline sin requerir USB/Loader:

```text
mtk-scatter to-gpt scatter.txt output-prefix ufs 4096 USER-CAPACITY-BYTES
mtk-scatter from-gpt pgpt.bin scatter.txt ufs 4096 USER-CAPACITY-BYTES MT6893
```

El almacenamiento admite `emmc` o `ufs`, bloques lógicos de 512 o 4096, y la capacidad debe ser el número confirmado de bytes de USER. `to-gpt` genera `.pgpt.bin` y `.sgpt.bin` compactos sin materializar el disco entero. `from-gpt` valida estrictamente GPT principal/secundario comprobando CRC, capacidades, límites, nombres y solapamientos.

El GPT resultante solo contiene geometría de particiones ordinarias de USER. Scatter→GPT jamás serializa `preloader`, `preloader_backup`, `pgpt`, `sgpt` ni alias históricos en la tabla GPT para evitar convertirlos en particiones reales; el cálculo de límites NEEDRESIZE utiliza el área final reservada. La exportación establece `file_name: NONE` y `is_download: false`, requiriendo completar las imágenes reales antes de su uso.

## SDK: BeforeDa1 para Completar EMI Faltante

Un `IMtkExploitStrategy.Execute` inyectado explícitamente por el host puede devolver `ReplacementEmi` antes de la subida de DA1, soportado tanto en `Connect` síncrono como en `ConnectAsync` asíncrono:

```csharp
public MtkExploitResult Execute(MtkExploitContext context, CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    if (context.Stage != MtkExploitStage.BeforeDa1 || context.Emi is not null)
        return new(MtkExploitOutcome.NotApplicable);

    // preloaderSource es un IDataSource estable y reabrible provisto por el host.
    var emi = MtkEmiParser.Parse(preloaderSource);
    cancellationToken.ThrowIfCancellationRequested();
    return new(MtkExploitOutcome.Completed) { ReplacementEmi = emi };
}
```

Este código se implementa dentro de una clase de estrategia del host; `MtkEmiParser` se encuentra en `GeekFlashCore.Protocol.Mtk.Loaders` y los contratos en `GeekFlashCore.Protocol.Mtk.Abstractions`. El descriptor debe incluir BeforeDa1 y el modo de arranque/dialecto DA objetivo.

También se puede utilizar directamente el constructor: `return new(MtkExploitOutcome.Completed, ReplacementEmi: emi);`.

El núcleo valida y adopta el candidato sólo cuando se cumple `BeforeDa1 + Completed` y el recurso original `MtkConnectionResources.Emi` es nulo; si ya existía un EMI, tiene prioridad. Estados ilegales o no completados con candidato son rechazados exigiendo reconexión; un valor nulo no descarta recursos existentes y un EMI corrupto no se trata como faltante. `context.Emi` permanece como sólo lectura y caduca tras el callback.

`Completed` finaliza las pruebas de estrategia de esta etapa sin saltarse la autenticación estándar. La fuente de datos pertenece al host y no debe liberarse antes del retorno del callback; el núcleo únicamente libera los streams abiertos por él mismo. Execute se ejecuta sincrónicamente bajo el gate de conexión y el presupuesto total, sin poder reingresar al protocolo ni bloquearse en esperas asíncronas de UI. No se sube ni ejecuta el Preloader completo en el dispositivo.

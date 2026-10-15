# Catálogo de Firmware y Desempaquetado en Streaming

`GeekFlashCore.Firmware` es un módulo independiente para .NET 8. El desempaquetado proporciona un catálogo de sólo lectura donde cada `FirmwareEntry` implementa `IDataSource`; la CLI, las aplicaciones de escritorio y desarrolladores del framework pueden consumir directamente los flujos de datos descifrados del paquete. Actualmente el módulo se centra exclusivamente en el desempaquetado.

## CLI

Los comandos offline no requieren conexión con ningún dispositivo:

```text
geekflash firmware list "D:\ROM\firmware.ofp"
geekflash firmware extract "D:\ROM\firmware.ofp" "boot.img" "D:\Output\boot.img"
```

`extract` únicamente exporta la entrada especificada a un archivo nuevo, sin sobrescribir archivos existentes. El catálogo preserva mayúsculas/minúsculas y rutas relativas completas; entradas con nombres idénticos pueden leerse mediante `Entries[index]`, rechazándose accesos ambiguos por nombre.

Para conexiones SPRD, se puede ejecutar `geekflash --pac "firmware.pac" --non-interactive connect`, obteniendo automáticamente ambas etapas de FDL y sus direcciones de carga a partir de los roles del PAC y el XML de BMAConfig, leyendo en streaming sin extraer archivos intermedios en disco; detalles en [Configuración PAC en SPRD](sprd.md#configuración-automática-de-loader-vía-pac). El módulo de firmware no ejecuta comandos directos sobre el hardware.

`list` no analiza el payload de un Super particionado virtual, mostrando su tamaño como "resolución bajo demanda". En el SDK se utiliza `FirmwareEntry.KnownLength` para consultar longitudes conocidas sin detonar operaciones de I/O; si se requiere la longitud precisa se invoca `GetLength(ct)` o `Length`, lo cual crea y valida el mapeo en su primer uso.

Para sesiones activas de Qualcomm, la CLI interactiva admite:

```text
rawprogram "D:\ROM\firmware.ofp::rawprogram.xml"
patch "D:\ROM\firmware.ofp::patch.xml"
rawprogram "D:\ROM\firmware.zip::images/rawprogram*.xml"
```

En llamadas no interactivas se conservan las configuraciones de Loader, conexión y autenticación originales:

```text
geekflash --port COM73 --loader programmer_firehose.mbn rawprogram "D:\ROM\firmware.ofp::rawprogram.xml"
geekflash --port COM73 --loader programmer_firehose.mbn patch "D:\ROM\firmware.ofp::patch.xml"
```

Usa previamente `firmware list` para confirmar los nombres de scripts. Los ProgramList/PatchList de OFP Qualcomm generan `rawprogram.xml` y `patch.xml`, mientras que Super puede generar `rawprogram_super.xml`; en OPS, ProgramN/PatchN generan `rawprogramN.xml` y `patchN.xml`. Si el paquete ya contiene dichos archivos, tienen prioridad los existentes. Los scripts virtuales conservan únicamente los atributos estándar de Firehose, descartando metadatos de posicionamiento del contenedor. El framework no ejecuta el paquete completo ni parches posteriores de forma automática.

Las rutas de imágenes en los scripts se resuelven relativas a la carpeta del script dentro del paquete (por ejemplo, `images/rawprogram0.xml` que referencia `boot.img` buscará `images/boot.img`). Se rechazan rutas absolutas, unidades de disco, caracteres de control y `.` o `..`. La falta de imágenes falla inmediatamente sin recurrir a archivos locales homónimos. Se admite un máximo de 1024 scripts por lote.

## Uso por Desarrolladores

```csharp
using GeekFlashCore.Firmware;
using GeekFlashCore.Protocol.Qcom.Abstractions;

// qcom es una instancia de IQcomProtocol ya configurada y conectada, propiedad del host.
using FirmwarePackage package = FirmwareUnpacker.Open("firmware.ofp",
    cancellationToken: cancellationToken);

FirmwareEntry rawprogram = package.GetEntry("rawprogram.xml");
FirehoseScriptResult written = qcom.ExecuteRawProgram(rawprogram,
    name => package.ResolveEntry(rawprogram.Name, name),
    progress, cancellationToken);

FirehoseScriptResult patched = qcom.ExecutePatchFile(
    package.GetEntry("patch.xml"), progress, cancellationToken);
```

También es posible usar `FirmwareUnpacker.Open(IDataSource, options, ct)` para conectar fuentes de datos estables, posicionables y reabribles del host. No existen descargas HTTP ni dependencias ocultas dentro del módulo Firmware.

`FirmwareEntry.ResourceId` conserva el File-ID declarado en el catálogo PAC (ej. `FDL`, `FDL2`, `XML`; en otros formatos es `null`). Es independiente de `Name` y representa metadatos inmutables del contenedor para selección de roles.

```csharp
using Stream image = package.GetEntry("boot.img").OpenStream(cancellationToken);
image.Seek(1024, SeekOrigin.Begin);
image.ReadExactly(buffer);

// Streams independientes y reabribles; exportar únicamente si el host requiere un archivo.
using var output = File.Create("boot.img");
package.GetEntry("boot.img").CopyTo(output, cancellationToken: cancellationToken);
```

El anidamiento de contenedores es explícito:

```csharp
using var ota = FirmwareUnpacker.Open("ota.zip");
using var payload = FirmwareUnpacker.Open(ota.GetEntry("payload.bin"));
IDataSource boot = payload.GetEntry("boot.img");

using var kdz = FirmwareUnpacker.Open("firmware.kdz");
using var dz = FirmwareUnpacker.Open(kdz.GetEntry("firmware.dz"));
```

Un OZIP original devuelve una entrada `decrypted.zip`, sobre la cual se puede invocar `Open`. Archivos ZIP con entradas cifradas entregan los archivos ya descifrados directamente. El payload dentro de un ZIP no se expande implícitamente.

Archivos Super fragmentados en OFP se mapean según NVList a un `super.img` virtual en formato Sparse. `Open` también acepta directorios ya descomprimidos; si la CLI local carece de `super.img`, utilizará los fragmentos del mismo directorio, admitiendo rutas anidadas `ZIP::OFP::entry.xml`. Más detalles en [Lectura directa de Super fragmentado](ofp-sparse-super.md).

Para particiones lógicas en paquetes dispersos de Oplus se utiliza `FirmwareSuperImagePlan`: lee `super_def` y metadatos LP para escribir primero los metadatos y luego consumir directamente el Sparse de cada partición, sin escanear el payload completo por defecto. `firmware super-info` y `write super "paquete::META/super_def.*.json"` de la CLI siguen esta vía. Ver [Super en paquetes dispersos Oplus](oplus-loose-super.md).

## Formatos y Límites

| Formato | Comportamiento Implementado | Límites Definidos |
| --- | --- | --- |
| ZIP | Stored, Deflate, BZip2; reapertura y posicionamiento independiente | No soporta contraseñas, volúmenes divididos u otros métodos de compresión |
| OZIP | ECB por bloques original, archivos cifrados dentro de ZIP | Sólo utiliza claves candidatas fijas de referencia; claves desconocidas reportan error |
| OFP Qcom | Fin de página 512/4096, metadatos CFB128, prefijo cifrado y cola en texto plano, XML virtual y Super Sparse fragmentado | Secuencias de fragmentos NV requieren selección explícita |
| OFP MTK | Catálogo con shuffle al final, prefijo CFB128 en cada entrada | No interactúa con autenticación ni ejecución en dispositivos MTK |
| OPS | Tres tipos de MBox, descifrado por retroalimentación, scripts virtuales | Conserva alineación física a 4 bytes según el formato |
| PAC | Catálogo UTF-16, offsets de directorio y rangos de 64 bits | No ejecuta secuencias de flasheo en dispositivos |
| KDZ/DZ | Subcontenedor KDZ, bloques zlib DZ | DZ entrega chunks independientes sin fusionar particiones automáticamente |
| UPDATE.APP | Registros de 98 bytes, HeaderSize, alineación a 4 bytes | Estructura a partir del offset 92 |
| Android payload v2 | REPLACE, REPLACE_BZ, REPLACE_XZ, extensión ZSTD, ZERO/DISCARD; todos los extents | Rechaza operaciones incrementales dependientes de imágenes antiguas; DISCARD se exporta como ceros |
| Directorios Descomprimidos | Entradas IDataSource de sólo lectura, `super.img` Sparse virtual ante secuencias continuas `super.N.hash.img` | No sigue reparse points ni infiere variantes con índices duplicados |

Los contratos públicos no exponen tipos de terceros. Límites por defecto: 32 MiB de metadatos, 65536 entradas de catálogo, 262144 operaciones/extents o descripciones OZIP, buffer de copia de 64 KiB. El diccionario XZ o ventana Zstandard en payload no excede 64 MiB por defecto (ajustable de forma acotada vía `FirmwareOpenOptions`). Relleno de ceros solo permitido en la última porción menor a un bloque completo; descompresiones excesivas o truncadas reportan fallo.

Flujos Raw, OFP/OPS y ECB se posicionan directamente. Flujos comprimidos emulan `Seek` mediante reapertura y descompresión secuencial.

Los flujos de entradas ZIP cuentan con una ventana fija en pool de 128 KiB para lecturas hacia atrás cercanas; fuera de la ventana se reinicia el flujo.

## Ciclo de Vida y Propiedad de Recursos

El `IDataSource` de entrada pertenece al host; el módulo sólo libera los streams que él mismo abre. Cada flujo de entrada individual es liberado por el llamador, pudiendo abrir múltiples flujos independientes sobre la misma entrada. Al liberar el paquete, ni las entradas ni los flujos abiertos pueden continuar utilizándose; los paquetes anidados deben utilizarse mientras el paquete contenedor siga siendo válido. Claves criptográficas internas y buffers de descifrado en pool se limpian a cero al liberarse.

`OpenStreamAsync` es una adaptación para fuentes de datos síncronas offline y no efectúa esperas asíncronas de recursos externos. El desempaquetado exitoso no certifica firmas criptográficas de payload, hashes de imagen o autorizaciones de hardware; tales comprobaciones competen a capas superiores.

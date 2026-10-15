# Lectura Directa y Flasheo de Super Fragmentado en OFP

Paquetes como PGT110 que contienen `super_def`, metadatos LP e imágenes de particiones lógicas independientes utilizan la [ruta de escritura prioritaria de metadatos](oplus-loose-super.md), sin generar un índice Sparse de todo el paquete por defecto. Este documento describe la superposición de fragmentos `super.N.*.img` con idéntica geometría.

Archivos como `super.0.*.img`, `super.1.*.img`, etc., pueden declarar de forma independiente el tamaño lógico del Super completo. Sus áreas DontCare indican que ese fragmento no transporta dicha región; las zonas Raw/Fill pueden solaparse. El framework superpone los datos en estricto orden de fragmentos: datos posteriores sobrescriben los anteriores, DontCare preserva lo escrito por fragmentos previos y las posiciones no cubiertas por ninguna entrada permanecen como DontCare.

La salida es una codificación virtual de Sparse 1.0. El framework únicamente almacena rangos de chunks, offsets de origen y valores Fill, generando la cabecera bajo demanda y leyendo directamente el payload del fragmento original, sin crear archivos combinados, imágenes Raw intermedias ni buffers que crezcan con el tamaño de la partición. Qualcomm utiliza la planificación Sparse estándar transmitiendo únicamente las zonas reales de datos.

Al abrir el catálogo sólo se comprueban nombres de fragmentos, NV y rangos; el mapeo Sparse completo se difiere hasta invocar `GetLength(ct)` / `Length` o `OpenStream(ct)` sobre la entrada virtual. `firmware list` usa `KnownLength`, mostrando la longitud desconocida de un `super.img` virtual como "resolución bajo demanda".

## Directorios Descomprimidos

`FirmwareUnpacker.Open(directory)` genera un catálogo de carpeta, añadiendo automáticamente un `super.img` virtual para secuencias continuas de `super.N.hash.img`. Si existe un `super.img` físico, éste tiene prioridad; fragmentos faltantes o índices repetidos causan fallo.

El directorio provisto por el usuario puede utilizarse directamente en sesiones CLI activas:

```text
rawprogram "D:\ROM\PEHM00domestic_11_A.17_2022031122400000\rawprogram0.xml"
```

Archivos ordinarios referenciados en el XML local se leen con normalidad; un `super.img` faltante se resuelve mediante el catálogo Firmware del mismo directorio sin generar archivos en disco. También se admite la sintaxis explícita de paquete:

```text
firmware list "D:\ROM\PEHM00domestic_11_A.17_2022031122400000"
rawprogram "D:\ROM\PEHM00domestic_11_A.17_2022031122400000::rawprogram0.xml"
```

Los desarrolladores utilizan la misma interfaz `IDataSource`:

```csharp
using var firmware = FirmwareUnpacker.Open(unpackedDirectory,
    cancellationToken: cancellationToken);
var script = firmware.GetEntry("rawprogram0.xml");
qcom.ExecuteRawProgram(script,
    name => firmware.ResolveEntry(script.Name, name),
    progress, cancellationToken);
```

## OFP Original y OFP dentro de ZIP

OFP utiliza `super0/super1/...` de la NVList para seleccionar fragmentos ordenados. Si múltiples configuraciones NV referencian la misma secuencia se mapea automáticamente; ante secuencias distintas se debe indicar `FirmwareOpenOptions.OfpSuperNvId`. Sin NVList, únicamente se aceptan secuencias continuas confirmadas en Super.

Archivos físicos existentes como `rawprogram0.xml` se mantienen inalterados. Si en ProgramList el filename de la línea de Super está vacío, el `rawprogram.xml` generado referenciará al `super.img` virtual sin alterar el inicio, sectores ni tamaño.

La CLI admite hasta ocho niveles de anidamiento de contenedores preservando mayúsculas y minúsculas:

```text
firmware list "D:\ROM\PEHM00domestic_11_A.17_2022031122400000.zip::PEHM00domestic_11_A.17_2022031122400000/PEHM00domestic_11_A.17_2022031122400000.ofp"
rawprogram "D:\ROM\PEHM00domestic_11_A.17_2022031122400000.zip::PEHM00domestic_11_A.17_2022031122400000/PEHM00domestic_11_A.17_2022031122400000.ofp::rawprogram0.xml"
```

En código, los desarrolladores abren explícitamente paquetes anidados manteniendo el contenedor padre activo:

```csharp
using var zip = FirmwareUnpacker.Open(zipPath, cancellationToken: cancellationToken);
using var ofp = FirmwareUnpacker.Open(zip.GetEntry(ofpEntryName),
    new FirmwareOpenOptions { OfpSuperNvId = selectedNvId }, cancellationToken);
var script = ofp.GetEntry("rawprogram0.xml");
qcom.ExecuteRawProgram(script,
    name => ofp.ResolveEntry(script.Name, name), progress, cancellationToken);
```

Flujos ZIP Deflate emulan `Seek` reiniciando la descompresión. Stored/Deflate utiliza decodificadores de .NET y BZip2 adaptadores internos; CFB descifra en ventanas fijas de 64 KiB para evitar retrocesos continuos en descompresión.

Las entradas ZIP cuentan con una ventana de readback en pool de 128 KiB para consultas cercanas de metadatos o footers. Al liberar el stream, la memoria se limpia a cero y vuelve al pool.

## API Genérica de Sparse y Límites

`GeekFlashCore.Android.Sparse.SparseImageComposer.Compose` acepta una lista ordenada de `Func<CancellationToken, Stream>` y devuelve un `SparseImageComposition` inmutable con `EncodedLength`, `ExpandedLength`, `BlockSize`, `TotalBlocks`, `ChunkCount` y `OpenStream(ct)`:

```csharp
SparseImageComposition image = SparseImageComposer.Compose(
    new Func<CancellationToken, Stream>[]
    {
        ct => first.OpenStream(ct),
        ct => second.OpenStream(ct),
        ct => third.OpenStream(ct)
    }, cancellationToken: cancellationToken);
using Stream sparse = image.OpenStream(cancellationToken);
```

La geometría de entrada debe ser homogénea. Compose gestiona la apertura y cierre de sus flujos de análisis; el flujo resultante es liberado por el llamador. Las fuentes deben ser estables y reabribles, comprobando su longitud al reabrirse.

Límites por defecto: hasta 16 orígenes, 262144 chunks de entrada/salida y un presupuesto de mapeo de 32 MiB. Cada flujo de salida mantiene en caché hasta cuatro flujos origen, con chunks Raw limitados por codificación `uint32`.

# Escritura Prioritaria de Metadatos para Super en Paquetes Dispersos de Oplus

Paquetes como PGT110 suministran `META/super_def.*.json`, un blob compacto de metadatos LP y múltiples imágenes de particiones lógicas. Éstas componen el Super mediante extents de LP, a diferencia de la superposición con geometría homogénea de `super.0.*.img`.

El punto de entrada por defecto `FirmwareSuperImagePlan.Open` sólo lee la configuración, metadatos LP, referencias de rutas y cabeceras fijas de cada imagen. Preserva el tamaño de metadatos, slots, Virtual A/B, grupos, particiones B vacías y extents declarados en el blob válido dentro del paquete; si no existe el blob, se genera a partir del JSON (por defecto: metadatos de 65536, 2 slots, bloques de 4096 bytes, alineación a 1 MiB y particiones de sólo lectura). Si existen múltiples `super_def`, se debe seleccionar uno explícitamente sin adivinar el NV.

La escritura transmite primero la geometría compacta y los metadatos principales/secundarios de cada slot, abriendo a continuación las imágenes de partición de forma secuencial. La biblioteca Sparse lee chunks de forma secuencial hacia adelante; Raw/Fill se proyectan a la ubicación destino mediante extents de LP y se envían de forma directa, mientras que DontCare se omite. El flujo de cada partición se consume una única vez, sin crear índices globales de chunks, archivos combinados, imágenes Raw en disco ni arrays de partición completa en memoria.

## CLI

Inspección offline de configuración sin escanear el payload:

```text
geekflash firmware super-info "D:\firmware\PGT110domestic_11_14.0.0.550CN01_2024090919340129.zip::META/super_def.10010111.json"
```

Escritura directa de Super en una sesión activa de Qualcomm o MTK:

```text
write super "D:\firmware\PGT110domestic_11_14.0.0.550CN01_2024090919340129.zip::META/super_def.10010111.json"
```

Se puede añadir `[lun]` al final si el destino se determina de forma unívoca y cuenta con capacidad suficiente. Paquetes de carpetas o contenedores anidados emplean la misma sintaxis `::`. Imágenes individuales dentro de un paquete admiten `write boot_a "rom.zip::IMAGES/boot.img"`.

La CLI utiliza comandos Raw Program explícitos en Qualcomm y Write original en MTK, evitando la pre-inspección global de Sparse del protocolo. "Raw" refiere a una ventana acotada de datos transmitida al dispositivo, no a una imagen Raw completa generada.

## SDK

El host conecta, autentica y valida el inicio, LUN, capacidad y alineación del Super destino antes de ejecutar el callback de escritura síncrona. Ejemplo en Qualcomm:

```csharp
using GeekFlashCore.Firmware;
using GeekFlashCore.Protocol.Qcom.Abstractions;

using var package = FirmwareUnpacker.Open(zipPath,
    cancellationToken: cancellationToken);
var plan = FirmwareSuperImagePlan.Open(package,
    "META/super_def.10010111.json", cancellationToken);

long sent = plan.Write((region, source) =>
{
    qcom.Program(new FirehoseProgramRequest
    {
        Source = source,
        Format = FirehoseProgramFormat.Raw,
        PhysicalPartitionNumber = lun,
        StartSector = superStartSector + region.OutputOffset / sectorSize,
        SectorCount = region.Length / sectorSize,
        SectorSizeInBytes = sectorSize,
        Label = "super"
    }, cancellationToken: cancellationToken);
}, cancellationToken);
```

El callback en MTK utiliza el mismo plan:

```csharp
plan.Write((region, source) =>
{
    using Stream input = source.OpenStream();
    mtk.Write(new MtkFlashRange(regionId,
        superStartByte + region.OutputOffset, region.Length),
        input, cancellationToken);
}, cancellationToken);
```

La entrada del callback es un `IDataSource` temporal en Raw explícito diseñado para una única lectura secuencial hacia adelante; tras retornar el callback, la fuente y el stream quedan invalidados.

Si se requiere una pre-inspección exhaustiva y un stream Sparse posicionable, se utiliza explícitamente `plan.CreateSparseImage(ct)` o `FirmwareSuperImage.Open(package, definition, ct)`. Esta vía escanea todos los chunks (descomprimiendo por completo una vez en ZIP Deflate y leyendo nuevamente al escribir), adecuada para resolvers de rawprogram:

```csharp
var image = plan.CreateSparseImage(cancellationToken);
qcom.ExecuteRawProgram(script,
    name => name == "super.img" ? image : package.ResolveEntry(script.Name, name),
    progress, cancellationToken);
```

Puntos de entrada genéricos en LP: `LpSuperImageLayout.ReadMetadataBlob` / `Create` / `OpenMetadataStream` / `CreateSparseImage`. Para mapeo de posiciones en Sparse: `SparseImageComposer.ComposeLayout`; para lectura secuencial: `SparseSequentialReader.TryReadHeader` / `ReadData`. Las APIs públicas no exponen decodificadores ni tipos CRC de terceros.

## Límites y Evidencia

Soporta dispositivos únicos, idéntico tamaño de bloque entre Sparse y LP, y extents lineales alineados por bloques. Las rutas deben residir dentro del paquete, con un máximo de ocho referencias. Rango de chunks, longitud del payload y CRC acumulado se verifican conforme se consumen los datos.

En un paquete real PGT110, la creación del plan se redujo de 44.63 s (en la ruta de indexación completa) a 0.004 s, con el primer callback de metadatos en ~0.3 ms. La lectura secuencial de 11,339,939,840 bytes tomó ~42.14 s (256.63 MiB/s) con asignaciones acumuladas de sólo ~5.7 MiB en memoria, sin volcar imágenes a disco.

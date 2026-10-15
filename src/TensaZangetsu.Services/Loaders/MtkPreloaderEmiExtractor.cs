using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using TensaZangetsu.Firmware;
using TensaZangetsu.Protocol.Mtk.Abstractions;
using TensaZangetsu.Protocol.Mtk.Exploits.Penumbra;
using TensaZangetsu.Protocol.Mtk.Loaders;

namespace TensaZangetsu.Services.Loaders;

public sealed record ExtractedEmiResult(
    MtkEmiImage EmiImage,
    uint EmiVersion,
    int PayloadLength,
    string SourcePreloaderName,
    string DetectionMethod
);

public static class MtkPreloaderEmiExtractor
{
    private static ReadOnlySpan<byte> BloaderMagic => "MTK_BLOADER_INFO_v"u8;
    private static ReadOnlySpan<byte> GfhMagic => [0x4D, 0x4D, 0x4D, 0x01, 0x38, 0x00, 0x00, 0x00]; // "MMM\x01"
    private static ReadOnlySpan<byte> MtkBinMagic => "MTK_BIN"u8;

    /// <summary>
    /// Extrae el bloque EMI/DRAM desde un archivo preloader.bin o un volcado crudo de boot0.
    /// </summary>
    public static ExtractedEmiResult ExtractFromPreloaderFile(string preloaderPath)
    {
        if (!File.Exists(preloaderPath))
            throw new FileNotFoundException("No se encontró el archivo Preloader especificado.", preloaderPath);

        byte[] rawBytes = File.ReadAllBytes(preloaderPath);
        return ExtractFromPreloaderBytes(rawBytes, Path.GetFileName(preloaderPath));
    }

    /// <summary>
    /// Abre una ROM (.ofp, .ops, .pac, payload.bin) en streaming, localiza la partición
    /// preloader sin descomprimir el resto de la ROM y extrae su bloque EMI/DRAM en menos de 1 segundo.
    /// </summary>
    public static ExtractedEmiResult ExtractFromRomPackageInStreaming(string romPackagePath)
    {
        using var archive = FirmwareUnpacker.Open(romPackagePath);

        var preloaderEntry = archive.Entries.FirstOrDefault(e =>
            e.Name.Contains("preloader", StringComparison.OrdinalIgnoreCase));

        if (preloaderEntry is null)
            throw new InvalidDataException($"El paquete ROM '{Path.GetFileName(romPackagePath)}' no contiene una entrada 'preloader'.");

        using var stream = preloaderEntry.OpenStream();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);

        string entryLabel = preloaderEntry.Name;

        return ExtractFromPreloaderBytes(ms.ToArray(), entryLabel);
    }

    /// <summary>
    /// Analiza un buffer de Preloader o boot0 en memoria y devuelve el MtkEmiImage listo para DA1.
    /// </summary>
    public static ExtractedEmiResult ExtractFromPreloaderBytes(ReadOnlySpan<byte> data, string sourceName = "preloader.bin")
    {
        if (data.Length < 256)
            throw new InvalidDataException("El archivo Preloader es demasiado pequeño para contener tablas EMI/DRAM.");

        // 1. Si viene de un volcado crudo de boot0 (EMMC_BOOT1 / BRLYT), saltar hasta GFH_FILE_INFO ("MMM\x01")
        ReadOnlySpan<byte> workingSpan = StripBoot0HeaderIfPresent(data);

        // 2. Método Primario: Buscar el descriptor "MTK_BLOADER_INFO_vXX" (Cubre la mayoría de MTK Legacy, V5 y V6)
        int bloaderIdx = workingSpan.IndexOf(BloaderMagic);
        if (bloaderIdx >= 0)
        {
            return ParseBloaderInfoBlock(workingSpan[bloaderIdx..], sourceName);
        }

        // 3. Método Secundario: Buscar el ancla "MTK_BIN" dentro de preloaders unificados y extraer la tabla EMI adyacente
        int mtkBinIdx = workingSpan.IndexOf(MtkBinMagic);
        if (mtkBinIdx >= 0)
        {
            return ParseEmbeddedMtkBinEmi(workingSpan, mtkBinIdx, sourceName);
        }

        throw new InvalidDataException(
            $"No se encontró una estructura 'MTK_BLOADER_INFO_vXX' ni 'MTK_BIN' válida dentro de '{sourceName}'.");
    }

    private static ReadOnlySpan<byte> StripBoot0HeaderIfPresent(ReadOnlySpan<byte> raw)
    {
        // Si empieza directamente con "MTK_BLOADER_INFO_v", no recortar nada
        if (raw.StartsWith(BloaderMagic))
            return raw;

        // Buscar la firma GFH_FILE_INFO ("MMM\x01\x38\x00\x00\x00") que suele estar en 0x0000, 0x0800 o 0x1000
        int gfhOffset = raw.IndexOf(GfhMagic);
        if (gfhOffset > 0 && gfhOffset <= 0x4000)
        {
            return raw[gfhOffset..];
        }

        return raw;
    }

    /// <summary>
    /// Parsea un bloque "MTK_BLOADER_INFO_vXX":
    /// - Offset 0x00..0x1B (27 bytes): "MTK_BLOADER_INFO_vXX\0..."
    /// - Offset 0x1B..0x57 (61 bytes): Nombre interno del archivo preloader (ej. "preloader_k65v1_64_bsp.bin")
    /// - Offset 0x58 (88): Inicio de las estructuras EMI_SETTINGS o cabecera extendida
    /// - Últimos 4 bytes del contenedor: uint32 totalSize
    /// </summary>
    private static ExtractedEmiResult ParseBloaderInfoBlock(ReadOnlySpan<byte> bloaderSpan, string sourceName)
    {
        if (bloaderSpan.Length < 0x6C)
            throw new InvalidDataException("Bloque MTK_BLOADER_INFO truncado.");

        // Leer los dígitos de versión después de "MTK_BLOADER_INFO_v" (offset 18, 2 bytes ASCII)
        string verAscii = Encoding.ASCII.GetString(bloaderSpan.Slice(18, 2));
        if (!uint.TryParse(verAscii, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint parsedVer))
        {
            uint.TryParse(verAscii, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedVer);
        }

        // Extraer el nombre interno de la placa grabado en el preloader (offset 0x1B, hasta 61 bytes)
        string internalPreloaderName = Encoding.ASCII
            .GetString(bloaderSpan.Slice(0x1B, 61))
            .Split('\0')[0]
            .Trim();

        if (string.IsNullOrWhiteSpace(internalPreloaderName))
            internalPreloaderName = sourceName;

        // En los contenedores MTK_BLOADER_INFO:
        // Offset 0x58 (88) contiene m_emi_hdr; dependiendo de la versión, los datos EMI empiezan en 0x58 o 0x6C.
        // El tamaño total declarado en los últimos 4 bytes o en el campo de longitud nos delimita el bloque exacto.
        uint declaredTotalSize = BinaryPrimitives.ReadUInt32LittleEndian(bloaderSpan.Slice(bloaderSpan.Length - 4, 4));

        int emiDataStart = 0x58;
        int emiLength;

        if (declaredTotalSize > 0x58 && declaredTotalSize <= bloaderSpan.Length)
        {
            // Si el preloader completo termina con el trailer del BLOADER_INFO:
            emiLength = (int)declaredTotalSize - emiDataStart;
        }
        else
        {
            // Si "MTK_BLOADER_INFO_vXX" está al inicio y el código ARM ("MMM\x01") viene después:
            int nextGfh = bloaderSpan[0x58..].IndexOf(GfhMagic);
            if (nextGfh > 0)
            {
                emiLength = nextGfh - 4; // Excluir el uint32 de tamaño final previo a GFH
            }
            else
            {
                emiLength = bloaderSpan.Length - emiDataStart - 4;
            }
        }

        if (emiLength <= 0 || emiDataStart + emiLength > bloaderSpan.Length)
            throw new InvalidDataException($"Longitud de tabla EMI inválida ({emiLength} bytes) en {internalPreloaderName}.");

        byte[] emiPayload = bloaderSpan.Slice(emiDataStart, emiLength).ToArray();

        var emiImage = new MtkEmiImage(
            Source: new PenumbraBytesSource(emiPayload),
            Version: parsedVer);

        return new ExtractedEmiResult(
            EmiImage: emiImage,
            EmiVersion: parsedVer,
            PayloadLength: emiPayload.Length,
            SourcePreloaderName: internalPreloaderName,
            DetectionMethod: $"MTK_BLOADER_INFO_v{verAscii}");
    }

    private static ExtractedEmiResult ParseEmbeddedMtkBinEmi(ReadOnlySpan<byte> span, int mtkBinOffset, string sourceName)
    {
        // En preloaders con firma "MTK_BIN" sin cabecera BLOADER externa, la tabla de entradas DRAM
        // se alinea después del bloque descriptor MTK_BIN.
        int searchStart = mtkBinOffset + 16;
        if (searchStart + 512 > span.Length)
            throw new InvalidDataException("Bloque MTK_BIN sin espacio suficiente para registros EMI.");

        // Tomamos la ventana de configuración EMI alineada a 4 bytes (máximo 16 KB para cubrir múltiples MCPs)
        int maxWindow = Math.Min(16 * 1024, span.Length - searchStart);
        byte[] emiPayload = span.Slice(searchStart, maxWindow).ToArray();

        // Versión 0 indica a la sesión enviar el bloque con autodetección (uint.MaxValue en Legacy o directo en XFlash)
        var emiImage = new MtkEmiImage(
            Source: new PenumbraBytesSource(emiPayload),
            Version: 0);

        return new ExtractedEmiResult(
            EmiImage: emiImage,
            EmiVersion: 0,
            PayloadLength: emiPayload.Length,
            SourcePreloaderName: sourceName,
            DetectionMethod: "MTK_BIN Heuristic Window");
    }
}

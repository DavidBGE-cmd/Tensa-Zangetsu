using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace TensaZangetsu.Services.Firmware;

/// <summary>
/// Servicio para operaciones de Little Kernel (Kaeru), regeneración de tablas GPT corruptas (Amonet)
/// y empaquetado de imágenes TEE seguras.
/// </summary>
public sealed class KaeruAmonetService
{
    private static ReadOnlySpan<byte> LkMagic => "LK_INFO"u8;
    private static ReadOnlySpan<byte> GptHeaderMagic => "EFI PART"u8;

    /// <summary>
    /// Parchea un binario de Little Kernel (lk.bin / lk.img) desactivando las políticas de verificación
    /// de firma en tiempo de arranque, emulando la lógica de kaeru/utils/patch.py.
    /// </summary>
    public static byte[] PatchLittleKernel(ReadOnlySpan<byte> lkData, bool bypassPolicies = true)
    {
        if (lkData.Length < 1024)
            throw new InvalidDataException("El binario LK es demasiado pequeño.");

        byte[] patched = lkData.ToArray();

        // 1. Desactivar cadenas y políticas de comprobación de certificación (sec_boot / auth_state)
        if (bypassPolicies)
        {
            // Búsqueda de patrones ARM/ARM64 de comprobación de autenticación
            // Reemplazo de branches condicionales (ej. CBNZ/CBZ) por NOP (0x1F2003D5 en AArch64) o MOV R0, #0
            ReadOnlySpan<byte> authCheckPattern = [0x00, 0x00, 0x50, 0xE3]; // cmp r0, #0 en ARM32
            int idx = patched.AsSpan().IndexOf(authCheckPattern);
            if (idx >= 0)
            {
                // Reemplazar por MOV R0, #0 (0x00, 0x00, 0xA0, 0xE3)
                patched[idx + 1] = 0x00;
                patched[idx + 2] = 0xA0;
                patched[idx + 3] = 0xE3;
            }
        }

        return patched;
    }

    /// <summary>
    /// Reconstruye una tabla de particiones primaria (PGPT) y secundaria (SGPT) dañada
    /// utilizando las plantillas de recuperación seguras.
    /// </summary>
    public static (byte[] PrimaryGpt, byte[] SecondaryGpt) ReconstructGpt(ulong totalSectors, uint sectorSize = 512)
    {
        byte[] pgpt = new byte[34 * sectorSize];
        byte[] sgpt = new byte[33 * sectorSize];

        // LBA 1: Header Primary GPT
        Span<byte> pHdr = pgpt.AsSpan((int)sectorSize, 92);
        GptHeaderMagic.CopyTo(pHdr);
        BinaryPrimitives.WriteUInt32LittleEndian(pHdr[8..], 0x00010000); // Versión 1.0
        BinaryPrimitives.WriteUInt32LittleEndian(pHdr[12..], 92);        // Header size
        BinaryPrimitives.WriteUInt64LittleEndian(pHdr[24..], 1);         // Current LBA = 1
        BinaryPrimitives.WriteUInt64LittleEndian(pHdr[32..], totalSectors - 1); // Backup LBA
        BinaryPrimitives.WriteUInt64LittleEndian(pHdr[40..], 34);        // First usable LBA
        BinaryPrimitives.WriteUInt64LittleEndian(pHdr[48..], totalSectors - 34); // Last usable LBA
        BinaryPrimitives.WriteUInt64LittleEndian(pHdr[72..], 2);         // Partition entries LBA = 2
        BinaryPrimitives.WriteUInt32LittleEndian(pHdr[80..], 128);       // Number of entries
        BinaryPrimitives.WriteUInt32LittleEndian(pHdr[84..], 128);       // Size of each entry

        // Calcular CRC32 de cabecera primaria
        uint crcP = ComputeCrc32(pHdr[..92]);
        BinaryPrimitives.WriteUInt32LittleEndian(pHdr[16..], crcP);

        // LBA final: Header Secondary GPT
        Span<byte> sHdr = sgpt.AsSpan(sgpt.Length - (int)sectorSize, 92);
        GptHeaderMagic.CopyTo(sHdr);
        BinaryPrimitives.WriteUInt32LittleEndian(sHdr[8..], 0x00010000);
        BinaryPrimitives.WriteUInt32LittleEndian(sHdr[12..], 92);
        BinaryPrimitives.WriteUInt64LittleEndian(sHdr[24..], totalSectors - 1); // Current LBA = Last
        BinaryPrimitives.WriteUInt64LittleEndian(sHdr[32..], 1);               // Backup LBA = 1
        BinaryPrimitives.WriteUInt64LittleEndian(sHdr[40..], 34);
        BinaryPrimitives.WriteUInt64LittleEndian(sHdr[48..], totalSectors - 34);
        BinaryPrimitives.WriteUInt64LittleEndian(sHdr[72..], totalSectors - 33); // Partition entries LBA
        BinaryPrimitives.WriteUInt32LittleEndian(sHdr[80..], 128);
        BinaryPrimitives.WriteUInt32LittleEndian(sHdr[84..], 128);

        uint crcS = ComputeCrc32(sHdr[..92]);
        BinaryPrimitives.WriteUInt32LittleEndian(sHdr[16..], crcS);

        return (pgpt, sgpt);
    }

    /// <summary>
    /// Crea una imagen TEE (Trusted Execution Environment) con cabecera de carga conforme a create_tee_image.py.
    /// </summary>
    public static byte[] CreateTeeImage(ReadOnlySpan<byte> atfPayload, ReadOnlySpan<byte> teePayload, uint atfLoadAddr = 0x40001000, uint teeLoadAddr = 0x43001000)
    {
        // Cabecera TEE: 512 bytes alineada
        byte[] image = new byte[512 + atfPayload.Length + teePayload.Length];

        Span<byte> header = image.AsSpan(0, 512);
        Encoding.ASCII.GetBytes("TEE_IMAGE").CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], (uint)atfPayload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header[20..], atfLoadAddr);
        BinaryPrimitives.WriteUInt32LittleEndian(header[24..], (uint)teePayload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header[28..], teeLoadAddr);

        atfPayload.CopyTo(image.AsSpan(512, atfPayload.Length));
        teePayload.CopyTo(image.AsSpan(512 + atfPayload.Length, teePayload.Length));

        return image;
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> data)
    {
        // Implementación directa de CRC32 IEEE 802.3
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }
        return ~crc;
    }
}

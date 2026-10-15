using System.Buffers.Binary;
using System.Text;

namespace TensaZangetsu.Services.Firmware;

/// <summary>
/// Representa una sub-partición empaquetada dentro de una imagen LK (Little Kernel) de MediaTek.
/// </summary>
public sealed record MtkLkPartition(
    string Name,
    uint DataSize,
    uint LoadAddress,
    byte[] Header,
    byte[] Data);

/// <summary>
/// Motor nativo en C# para inspeccionar, extraer y reemplazar sub-particiones (por ejemplo, bl2_ext de sprig)
/// en imágenes LK de MediaTek sin depender de Python ni librerías externas.
/// </summary>
public sealed class MtkLkImageInjector
{
    public const uint MtkHeaderMagic1 = 0x58881688;
    public const uint MtkHeaderMagic2 = 0x58891689;
    public const int HeaderSize = 512;
    public const int Alignment = 16;

    /// <summary>
    /// Lee e indexa todas las sub-particiones presentes en un archivo de imagen LK.
    /// </summary>
    public List<MtkLkPartition> ReadPartitions(string lkImagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lkImagePath);
        if (!File.Exists(lkImagePath))
            throw new FileNotFoundException("No se encontró el archivo de imagen LK.", lkImagePath);

        using var fs = File.OpenRead(lkImagePath);
        return ReadPartitions(fs);
    }

    /// <summary>
    /// Lee e indexa todas las sub-particiones desde un stream binario de LK.
    /// </summary>
    public List<MtkLkPartition> ReadPartitions(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var partitions = new List<MtkLkPartition>();
        byte[] headerBuffer = new byte[HeaderSize];

        while (true)
        {
            int bytesRead = stream.Read(headerBuffer, 0, HeaderSize);
            if (bytesRead == 0)
                break;

            if (bytesRead < HeaderSize)
                break;

            uint magic1 = BinaryPrimitives.ReadUInt32LittleEndian(headerBuffer.AsSpan(0, 4));
            if (magic1 != MtkHeaderMagic1)
                break;

            uint dataSize = BinaryPrimitives.ReadUInt32LittleEndian(headerBuffer.AsSpan(4, 4));
            string name = Encoding.ASCII.GetString(headerBuffer.AsSpan(8, 32)).TrimEnd('\0');
            uint loadAddress = BinaryPrimitives.ReadUInt32LittleEndian(headerBuffer.AsSpan(40, 4));

            byte[] headerCopy = new byte[HeaderSize];
            Array.Copy(headerBuffer, headerCopy, HeaderSize);

            byte[] data = new byte[dataSize];
            int readTotal = 0;
            while (readTotal < dataSize)
            {
                int r = stream.Read(data, readTotal, (int)(dataSize - readTotal));
                if (r <= 0)
                    throw new EndOfStreamException($"Fin prematuro de stream leyendo partición '{name}' (leídos {readTotal} de {dataSize} bytes).");
                readTotal += r;
            }

            int padding = (Alignment - (int)(dataSize % Alignment)) % Alignment;
            if (padding > 0)
            {
                byte[] padBuf = new byte[padding];
                _ = stream.Read(padBuf, 0, padding);
            }

            partitions.Add(new MtkLkPartition(name, dataSize, loadAddress, headerCopy, data));
        }

        return partitions;
    }

    /// <summary>
    /// Inyecta un payload en una partición específica (ej. 'bl2_ext') y genera una nueva imagen LK empaquetada.
    /// </summary>
    public void InjectPartition(string inputLkPath, string partitionName, byte[] newPayload, string outputLkPath, bool preserveOriginalSize = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputLkPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionName);
        ArgumentNullException.ThrowIfNull(newPayload);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputLkPath);

        long originalLength = new FileInfo(inputLkPath).Length;
        var partitions = ReadPartitions(inputLkPath);
        int targetIndex = partitions.FindIndex(p => string.Equals(p.Name, partitionName, StringComparison.OrdinalIgnoreCase));

        if (targetIndex < 0)
        {
            throw new KeyNotFoundException($"Partición objetivo '{partitionName}' no encontrada en la imagen LK '{inputLkPath}'. " +
                                           $"Particiones disponibles: {string.Join(", ", partitions.Select(p => p.Name))}");
        }

        var oldPart = partitions[targetIndex];
        byte[] newHeader = (byte[])oldPart.Header.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(newHeader.AsSpan(4, 4), (uint)newPayload.Length);

        partitions[targetIndex] = new MtkLkPartition(
            Name: oldPart.Name,
            DataSize: (uint)newPayload.Length,
            LoadAddress: oldPart.LoadAddress,
            Header: newHeader,
            Data: newPayload);

        string? dir = Path.GetDirectoryName(outputLkPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        Span<byte> zeroPad = stackalloc byte[Alignment];
        zeroPad.Clear();

        using var outFs = File.Create(outputLkPath);
        foreach (var part in partitions)
        {
            outFs.Write(part.Header, 0, HeaderSize);
            outFs.Write(part.Data, 0, part.Data.Length);

            int padding = (Alignment - (part.Data.Length % Alignment)) % Alignment;
            if (padding > 0)
            {
                outFs.Write(zeroPad[..padding]);
            }
        }

        if (preserveOriginalSize && outFs.Length < originalLength)
        {
            long remaining = originalLength - outFs.Length;
            byte[] block = new byte[Math.Min(remaining, 64 * 1024)];
            while (remaining > 0)
            {
                int toWrite = (int)Math.Min(remaining, block.Length);
                outFs.Write(block, 0, toWrite);
                remaining -= toWrite;
            }
        }
    }

    /// <summary>
    /// Extrae los datos de una sub-partición indicada hacia un archivo en disco.
    /// </summary>
    public void ExtractPartition(string inputLkPath, string partitionName, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputLkPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var partitions = ReadPartitions(inputLkPath);
        var match = partitions.FirstOrDefault(p => string.Equals(p.Name, partitionName, StringComparison.OrdinalIgnoreCase));

        if (match == null)
        {
            throw new KeyNotFoundException($"Partición objetivo '{partitionName}' no encontrada en la imagen LK '{inputLkPath}'.");
        }

        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllBytes(outputPath, match.Data);
    }
}

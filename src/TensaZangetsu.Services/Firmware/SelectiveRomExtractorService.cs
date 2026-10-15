using TensaZangetsu.Firmware;
using TensaZangetsu.Protocol.Abstractions;

namespace TensaZangetsu.Services.Firmware;

public sealed record RomPackageInfo(
    string Format,
    int TotalEntries,
    long TotalEstimatedBytes,
    IReadOnlyList<RomEntryInfo> Entries);

public sealed record RomEntryInfo(
    string Name,
    long SizeBytes,
    string FormattedSize,
    bool IsLoader);

/// <summary>
/// Servicio de extracción selectiva en streaming de paquetes ROM (.ofp, .ops, .pac, .ozip, .zip).
/// Permite inspeccionar contenidos y extraer loaders en menos de 1 segundo sin descomprimir toda la ROM.
/// </summary>
public sealed class SelectiveRomExtractorService
{
    /// <summary>
    /// Inspecciona el paquete ROM y devuelve el formato y la lista de entradas contenidas.
    /// </summary>
    public RomPackageInfo InspectRomPackage(string romPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(romPath);
        if (!File.Exists(romPath))
            throw new FileNotFoundException("No se encontró el paquete ROM especificado.", romPath);

        using var package = FirmwareUnpacker.Open(romPath, cancellationToken: ct);

        var entries = new List<RomEntryInfo>(package.Entries.Count);
        long totalBytes = 0;

        foreach (var entry in package.Entries)
        {
            ct.ThrowIfCancellationRequested();
            long size = entry.Length;
            totalBytes += size;

            bool isLoader = IsLoaderEntry(entry.Name);

            entries.Add(new RomEntryInfo(
                Name: entry.Name,
                SizeBytes: size,
                FormattedSize: FormatSize((ulong)Math.Max(0, size)),
                IsLoader: isLoader));
        }

        return new RomPackageInfo(
            Format: package.Format.ToString(),
            TotalEntries: package.Entries.Count,
            TotalEstimatedBytes: totalBytes,
            Entries: entries);
    }

    /// <summary>
    /// Extrae exclusivamente los archivos de loaders (Preloader, Firehose, FDL1/FDL2, Auth)
    /// directamente a disco sin descomprimir el resto de las particiones pesadas.
    /// </summary>
    public async Task<IReadOnlyList<string>> ExtractLoadersOnlyAsync(
        string romPath,
        string outputDirectory,
        IProgress<ProgressRecord>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(romPath);
        Directory.CreateDirectory(outputDirectory);

        using var package = FirmwareUnpacker.Open(romPath, cancellationToken: ct);

        var loaderEntries = package.Entries.Where(e => IsLoaderEntry(e.Name)).ToList();
        var extractedFiles = new List<string>();

        int count = loaderEntries.Count;
        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var entry = loaderEntries[i];

            string safeFileName = Path.GetFileName(entry.Name);
            string targetPath = Path.Combine(outputDirectory, safeFileName);

            progress?.Report(new ProgressRecord(
                Total: count,
                Current: i + 1,
                Label: $"Extrayendo loader: {entry.Name} ({i + 1}/{count})"));

            await using (var inStream = entry.OpenStream())
            await using (var outFs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                await inStream.CopyToAsync(outFs, ct);
            }

            extractedFiles.Add(targetPath);
        }

        return extractedFiles;
    }

    /// <summary>
    /// Extrae selectivamente una o más particiones indicadas por nombre desde el paquete ROM.
    /// </summary>
    public async Task<IReadOnlyList<string>> ExtractPartitionsAsync(
        string romPath,
        IEnumerable<string> partitionNames,
        string outputDirectory,
        IProgress<ProgressRecord>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(romPath);
        Directory.CreateDirectory(outputDirectory);

        var targets = new HashSet<string>(partitionNames, StringComparer.OrdinalIgnoreCase);
        using var package = FirmwareUnpacker.Open(romPath, cancellationToken: ct);

        var matchingEntries = package.Entries
            .Where(e => targets.Contains(e.Name) || targets.Contains(Path.GetFileNameWithoutExtension(e.Name)))
            .ToList();

        var extractedFiles = new List<string>();
        int count = matchingEntries.Count;

        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var entry = matchingEntries[i];

            string safeFileName = Path.GetFileName(entry.Name);
            string targetPath = Path.Combine(outputDirectory, safeFileName);

            progress?.Report(new ProgressRecord(
                Total: count,
                Current: i + 1,
                Label: $"Extrayendo partición: {entry.Name} ({i + 1}/{count})"));

            await using (var inStream = entry.OpenStream())
            await using (var outFs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true))
            {
                await inStream.CopyToAsync(outFs, ct);
            }

            extractedFiles.Add(targetPath);
        }

        return extractedFiles;
    }

    private static bool IsLoaderEntry(string name)
    {
        string lower = name.ToLowerInvariant();
        return lower.Contains("preloader") ||
               lower.Contains("prog_firehose") ||
               lower.EndsWith(".elf") ||
               lower.EndsWith(".mbn") ||
               lower.EndsWith(".melf") ||
               lower.Contains("fdl1") ||
               lower.Contains("fdl2") ||
               lower.EndsWith(".auth") ||
               lower.Contains("sec.auth") ||
               lower.Contains("da.bin");
    }

    private static string FormatSize(ulong bytes)
    {
        return bytes switch
        {
            >= 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB",
            >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
            >= 1024 => $"{bytes / 1024.0:F0} KB",
            _ => $"{bytes} B"
        };
    }
}

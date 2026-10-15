using GeekFlashCore.Protocol.Abstractions;
using GeekFlashCore.Protocol.Mtk.Exploits.Penumbra;
using GeekFlashCore.Protocol.Mtk.Loaders;

namespace GeekFlashCore.Services.Loaders;

/// <summary>
/// Servicio de auto-resolución y emparejamiento de Loaders (Firehose, DA, Preloader, FDL1/FDL2).
/// Inspecciona metadatos y encabezados binarios en disco para emparejar con el hardware detectado.
/// </summary>
public sealed class AutoLoaderResolverService
{
    private readonly string _baseDirectory;

    public AutoLoaderResolverService(string? baseDirectory = null)
    {
        _baseDirectory = baseDirectory ?? ResolveAssetsDirectory();
    }

    private static string ResolveAssetsDirectory()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        if (Directory.Exists(Path.Combine(baseDir, "ExternalAssets")) || Directory.Exists(Path.Combine(baseDir, "Assets", "Loaders")))
            return baseDir;

        // Buscar en directorios superiores si se ejecuta desde bin/Release o bin/Debug
        var current = new DirectoryInfo(baseDir);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "ExternalAssets")))
                return current.FullName;

            string candidate = Path.Combine(current.FullName, "Assets");
            if (Directory.Exists(Path.Combine(candidate, "Loaders")))
                return current.FullName;

            string srcCandidate = Path.Combine(current.FullName, "src", "GeekFlashCore.App");
            if (Directory.Exists(Path.Combine(srcCandidate, "Assets", "Loaders")))
                return srcCandidate;

            current = current.Parent;
        }

        return baseDir;
    }

    /// <summary>
    /// Obtiene todas las carpetas existentes que coincidan con la ruta relativa en Assets y ExternalAssets.
    /// </summary>
    private List<string> GetProbeDirectories(string subPath)
    {
        var list = new List<string>();

        string[] roots = [
            _baseDirectory,
            Path.Combine(_baseDirectory, "..", "..", ".."),
            Path.Combine(_baseDirectory, "..", "..", "..", ".."),
            AppDomain.CurrentDomain.BaseDirectory
        ];

        foreach (var r in roots)
        {
            try
            {
                string fullRoot = Path.GetFullPath(r);
                string p1 = Path.Combine(fullRoot, "ExternalAssets", subPath);
                if (Directory.Exists(p1) && !list.Contains(p1)) list.Add(p1);

                string p2 = Path.Combine(fullRoot, "Assets", subPath);
                if (Directory.Exists(p2) && !list.Contains(p2)) list.Add(p2);

                string p3 = Path.Combine(fullRoot, "src", "GeekFlashCore.App", "Assets", subPath);
                if (Directory.Exists(p3) && !list.Contains(p3)) list.Add(p3);
            }
            catch { }
        }

        return list;
    }

    /// <summary>
    /// Resuelve y devuelve el paquete de loaders coincidente para la huella de hardware dada.
    /// </summary>
    public ResolvedLoaderBundle ResolveForDevice(DeviceHardwareFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);

        return fingerprint.Platform.ToUpperInvariant() switch
        {
            "QCOM" or "QUALCOMM" => ResolveQcom(fingerprint),
            "MTK" or "MEDIATEK" => ResolveMtk(fingerprint),
            "SPRD" or "UNISOC" => ResolveSprd(fingerprint),
            _ => new ResolvedLoaderBundle()
        };
    }

    private ResolvedLoaderBundle ResolveQcom(DeviceHardwareFingerprint fp)
    {
        var qcomDirs = GetProbeDirectories("Loaders/Qcom");
        if (qcomDirs.Count == 0)
            return new ResolvedLoaderBundle();

        string? hwId = fp.QcomMsmHwId?.TrimStart('0', 'x', 'X');
        string? pkHash = fp.QcomOemPkHash;

        string? matchedFirehose = null;

        foreach (var qcomDir in qcomDirs)
        {
            // 1. Buscar en subcarpeta específica HW_{hwId}
            if (!string.IsNullOrEmpty(hwId))
            {
                string hwPrefix = hwId.Length >= 8 ? hwId[..8] : hwId;
                var subDirs = Directory.GetDirectories(qcomDir, $"*{hwPrefix}*", SearchOption.AllDirectories);
                foreach (var dir in subDirs)
                {
                    matchedFirehose = FindFirehoseInDirectory(dir);
                    if (matchedFirehose != null) break;
                }
            }

            if (matchedFirehose != null) break;

            // 2. Si no encontró en subcarpetas, buscar recursivamente por nombre
            var files = Directory.GetFiles(qcomDir, "*.*", SearchOption.AllDirectories)
                .Where(f => IsFirehoseExtension(f))
                .ToList();

            if (!string.IsNullOrEmpty(hwId))
            {
                matchedFirehose = files.FirstOrDefault(f =>
                    Path.GetFileName(f).Contains(hwId, StringComparison.OrdinalIgnoreCase) ||
                    (hwId.Length >= 8 && Path.GetFileName(f).Contains(hwId[..8], StringComparison.OrdinalIgnoreCase)));
            }

            matchedFirehose ??= files.FirstOrDefault();
            if (matchedFirehose != null) break;
        }

        string? authPath = FindAuthFile();

        return new ResolvedLoaderBundle(
            QcomFirehosePath: matchedFirehose,
            AuthFilePath: authPath);
    }

    private ResolvedLoaderBundle ResolveMtk(DeviceHardwareFingerprint fp)
    {
        var mtkDirs = GetProbeDirectories("Loaders/Mtk");
        string? matchedDa = null;
        string? matchedPreloader = null;
        ushort? targetHwCode = fp.MtkHwCode;

        foreach (var mtkDir in mtkDirs)
        {
            var daFiles = Directory.GetFiles(mtkDir, "*.bin", SearchOption.AllDirectories)
                .Where(f => !f.Contains("Preloader", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToList();

            if (targetHwCode.HasValue)
            {
                foreach (var daFile in daFiles)
                {
                    if (DaSupportsHwCode(daFile, targetHwCode.Value))
                    {
                        matchedDa = daFile;
                        break;
                    }
                }
            }

            matchedDa ??= daFiles.FirstOrDefault(f => Path.GetFileName(f).Contains("AllInOne", StringComparison.OrdinalIgnoreCase))
                ?? daFiles.FirstOrDefault();

            if (matchedDa != null) break;
        }

        // Buscar preloader en Preloaders y Loaders/Mtk
        var preloaderDirs = GetProbeDirectories("Preloaders");
        preloaderDirs.AddRange(GetProbeDirectories("Loaders/Mtk/Preloaders"));

        if (targetHwCode.HasValue)
        {
            string[] candidates = [
                $"MT{targetHwCode.Value:X4}",
                $"MT{targetHwCode.Value:D4}",
                $"MT{targetHwCode.Value:X}",
                $"MT{targetHwCode.Value}",
                $"{targetHwCode.Value}"
            ];

            foreach (var pDir in preloaderDirs)
            {
                foreach (var cand in candidates)
                {
                    string subDir = Path.Combine(pDir, cand);
                    if (Directory.Exists(subDir))
                    {
                        matchedPreloader = Directory.GetFiles(subDir, "*.bin", SearchOption.AllDirectories).FirstOrDefault();
                        if (matchedPreloader != null) break;
                    }

                    // Buscar por nombre de archivo
                    var matchFile = Directory.GetFiles(pDir, $"*{cand}*.bin", SearchOption.AllDirectories).FirstOrDefault();
                    if (matchFile != null)
                    {
                        matchedPreloader = matchFile;
                        break;
                    }
                }
                if (matchedPreloader != null) break;
            }
        }

        if (matchedPreloader == null)
        {
            foreach (var pDir in preloaderDirs)
            {
                matchedPreloader = Directory.GetFiles(pDir, "preloader*.bin", SearchOption.AllDirectories).FirstOrDefault();
                if (matchedPreloader != null) break;
            }
        }

        string? authPath = FindAuthFile();

        return new ResolvedLoaderBundle(
            MtkDaPath: matchedDa,
            MtkPreloaderPath: matchedPreloader,
            AuthFilePath: authPath);
    }

    private ResolvedLoaderBundle ResolveSprd(DeviceHardwareFingerprint fp)
    {
        var sprdDirs = GetProbeDirectories("Loaders/Sprd");
        if (sprdDirs.Count == 0)
            return new ResolvedLoaderBundle();

        string? socId = fp.SprdSocId;
        var fdlFiles = new List<string>();
        foreach (var dir in sprdDirs)
        {
            fdlFiles.AddRange(Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".loader", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".pac", StringComparison.OrdinalIgnoreCase)));
        }

        string? fdl1 = fdlFiles.FirstOrDefault(f => Path.GetFileName(f).Contains("fdl1", StringComparison.OrdinalIgnoreCase));
        string? fdl2 = fdlFiles.FirstOrDefault(f => Path.GetFileName(f).Contains("fdl2", StringComparison.OrdinalIgnoreCase));

        // Direcciones típicas UNISOC BSL RAM
        ulong fdl1Address = 0x5500;
        ulong fdl2Address = 0x9f000000;

        if (!string.IsNullOrEmpty(socId))
        {
            var matchInFolder = fdlFiles.FirstOrDefault(f => f.Contains(socId, StringComparison.OrdinalIgnoreCase));
            if (matchInFolder != null && fdl1 == null)
            {
                fdl1 = matchInFolder;
            }

            if (socId.Contains("9863", StringComparison.OrdinalIgnoreCase) || socId.Contains("SC9863A", StringComparison.OrdinalIgnoreCase))
            {
                fdl1Address = 0x5500;
                fdl2Address = 0x9f000000;
            }
            else if (socId.Contains("T606", StringComparison.OrdinalIgnoreCase) || socId.Contains("T610", StringComparison.OrdinalIgnoreCase) ||
                     socId.Contains("T612", StringComparison.OrdinalIgnoreCase) || socId.Contains("T616", StringComparison.OrdinalIgnoreCase) ||
                     socId.Contains("UMS9230", StringComparison.OrdinalIgnoreCase) || socId.Contains("Tiger", StringComparison.OrdinalIgnoreCase))
            {
                fdl1Address = 0x6500;
                fdl2Address = 0xb0000000;
            }
        }

        fdl1 ??= fdlFiles.FirstOrDefault();

        return new ResolvedLoaderBundle(
            SprdFdl1Path: fdl1,
            SprdFdl2Path: fdl2,
            SprdFdl1Address: fdl1Address,
            SprdFdl2Address: fdl2Address);
    }

    private static bool DaSupportsHwCode(string daFilePath, ushort targetHwCode)
    {
        try
        {
            byte[] fileBytes = File.ReadAllBytes(daFilePath);
            IDataSource source = new PenumbraBytesSource(fileBytes);
            var entries = MtkDaParser.Parse(source);
            return entries.Any(e => e.HardwareCode == targetHwCode);
        }
        catch
        {
            return false;
        }
    }

    private static string? FindFirehoseInDirectory(string dir)
    {
        return Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(f => IsFirehoseExtension(f));
    }

    private static bool IsFirehoseExtension(string path)
    {
        return path.EndsWith(".elf", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".mbn", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".melf", StringComparison.OrdinalIgnoreCase);
    }

    private string? FindAuthFile()
    {
        var authDirs = GetProbeDirectories("Auth");
        authDirs.AddRange(GetProbeDirectories("Loaders/Mtk/Auth"));

        foreach (var dir in authDirs)
        {
            var match = Directory.GetFiles(dir, "*.auth", SearchOption.AllDirectories).FirstOrDefault()
                ?? Directory.GetFiles(dir, "*auth*.bin", SearchOption.AllDirectories).FirstOrDefault();
            if (match != null) return match;
        }

        return null;
    }
}

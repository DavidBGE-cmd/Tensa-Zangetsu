using System.Text.Json;

namespace TensaZangetsu.Services.Loaders;

public sealed record ManifestDownloadAgentItem(
    string RelativePath,
    string FileName,
    long Size,
    string Extension,
    string Category,
    string Brand,
    string Soc,
    string? Bit
);

public sealed record ManifestPreloaderItem(
    string RelativePath,
    string FileName,
    long Size,
    string Brand,
    string Soc,
    bool IsUnifiedDb
);

public sealed record ManifestScatterItem(
    string RelativePath,
    string FileName,
    long Size,
    string Extension,
    string Soc,
    string Brand
);

public sealed record ManifestPayloadItem(
    string RelativePath,
    string FileName,
    long Size,
    string Soc,
    string Category
);

public sealed class AssetsManifestData
{
    public string GeneratedAt { get; set; } = string.Empty;
    public int TotalIndexedFiles { get; set; }
    public List<ManifestDownloadAgentItem> DownloadAgents { get; set; } = [];
    public List<ManifestPreloaderItem> Preloaders { get; set; } = [];
    public List<ManifestScatterItem> Scatters { get; set; } = [];
    public List<ManifestPayloadItem> Payloads { get; set; } = [];
}

/// <summary>
/// Servicio de consulta y extracción rápida del catálogo indexado AssetsManifest.json.
/// Permite recuperar en tiempo real DAs (.mtk, V5, V6 XML), Preloaders con tablas EMI,
/// archivos de autenticación y Payloads de bypass para el chipset y modelo conectado.
/// </summary>
public sealed class AssetsManifestService
{
    private static readonly object _syncLock = new();
    private static AssetsManifestData? _cachedData;
    private static string? _resolvedRootDir;
    private static string? _resolvedManifestPath;
    private static bool _attemptedLoad;

    public string? ManifestPath => _resolvedManifestPath;
    public string? RootAssetsDirectory => _resolvedRootDir;
    public bool IsLoaded => _cachedData != null;
    public AssetsManifestData? Data => _cachedData;

    public AssetsManifestService(string? customManifestPath = null)
    {
        if (!string.IsNullOrEmpty(customManifestPath) && File.Exists(customManifestPath))
        {
            LoadManifest(customManifestPath);
        }
        else
        {
            EnsureLoaded();
        }
    }

    public void EnsureLoaded()
    {
        if (_cachedData != null || _attemptedLoad) return;

        lock (_syncLock)
        {
            if (_cachedData != null || _attemptedLoad) return;
            _attemptedLoad = true;

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidatePaths = [
                Path.Combine(baseDir, "AssetsManifest.json"),
                Path.Combine(baseDir, "mt-katool-FutureAssets", "AssetsManifest.json"),
                Path.Combine(baseDir, "..", "..", "..", "mt-katool-FutureAssets", "AssetsManifest.json"),
                Path.Combine(baseDir, "..", "..", "..", "..", "mt-katool-FutureAssets", "AssetsManifest.json"),
                @"c:\Users\dbge_admin\TensaZangetsu\mt-katool-FutureAssets\AssetsManifest.json"
            ];

            foreach (var candidate in candidatePaths)
            {
                try
                {
                    string full = Path.GetFullPath(candidate);
                    if (File.Exists(full))
                    {
                        LoadManifest(full);
                        return;
                    }
                }
                catch { }
            }
        }
    }

    private static void LoadManifest(string fullPath)
    {
        try
        {
            string json = File.ReadAllText(fullPath);
            var data = JsonSerializer.Deserialize<AssetsManifestData>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (data != null)
            {
                _cachedData = data;
                _resolvedManifestPath = fullPath;
                _resolvedRootDir = Path.GetDirectoryName(fullPath);
            }
        }
        catch { }
    }

    public string? ResolveFullPath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        if (Path.IsPathRooted(relativePath) && File.Exists(relativePath)) return relativePath;
        if (string.IsNullOrEmpty(_resolvedRootDir)) return null;

        string combined = Path.Combine(_resolvedRootDir, relativePath);
        return File.Exists(combined) ? combined : null;
    }

    /// <summary>
    /// Busca el Download Agent más apropiado para el código de hardware (ej. 0x0707 -> MT6768),
    /// modelo o marca especificados en el índice AssetsManifest.json.
    /// </summary>
    public ManifestDownloadAgentItem? FindBestDownloadAgent(
        ushort? hwCode,
        string? modelOrCodename = null,
        string? brand = null,
        string? bit = null)
    {
        EnsureLoaded();
        if (_cachedData == null || _cachedData.DownloadAgents.Count == 0) return null;

        var daList = _cachedData.DownloadAgents;
        string? hwStr = hwCode.HasValue ? $"MT{hwCode.Value:X4}" : null;

        // 1. Coincidencia exacta por modelo y bit si aplica (ej. Samsung A15 BIT 5)
        if (!string.IsNullOrWhiteSpace(modelOrCodename))
        {
            var modelMatches = daList.Where(d =>
                d.RelativePath.Contains(modelOrCodename, StringComparison.OrdinalIgnoreCase) ||
                d.FileName.Contains(modelOrCodename, StringComparison.OrdinalIgnoreCase)).ToList();

            if (modelMatches.Count > 0)
            {
                if (!string.IsNullOrWhiteSpace(bit))
                {
                    var bitMatch = modelMatches.FirstOrDefault(d =>
                        !string.IsNullOrEmpty(d.Bit) && d.Bit.Contains(bit, StringComparison.OrdinalIgnoreCase));
                    if (bitMatch != null) return bitMatch;
                }
                return modelMatches.First();
            }
        }

        // 2. Coincidencia por SoC (ej. MT6768)
        if (hwStr != null)
        {
            var socMatch = daList.FirstOrDefault(d =>
                (!string.IsNullOrEmpty(d.Soc) && d.Soc.Contains(hwStr, StringComparison.OrdinalIgnoreCase)) ||
                d.FileName.Contains(hwStr, StringComparison.OrdinalIgnoreCase) ||
                d.RelativePath.Contains(hwStr, StringComparison.OrdinalIgnoreCase));

            if (socMatch != null) return socMatch;
        }

        // 3. Fallback a Download Agents All-In-One (V5 o V6)
        return daList.FirstOrDefault(d => d.FileName.Contains("AllInOne", StringComparison.OrdinalIgnoreCase))
            ?? daList.FirstOrDefault(d => d.Category == "DownloadAgentBin");
    }

    /// <summary>
    /// Busca el Preloader EMI correspondiente para inicializar DRAM.
    /// </summary>
    public ManifestPreloaderItem? FindBestPreloader(ushort? hwCode, string? brand = null)
    {
        EnsureLoaded();
        if (_cachedData == null || _cachedData.Preloaders.Count == 0) return null;

        string? hwStr = hwCode.HasValue ? $"MT{hwCode.Value:X4}" : null;

        if (hwStr != null)
        {
            var match = _cachedData.Preloaders.FirstOrDefault(p =>
                (!string.IsNullOrEmpty(p.Soc) && p.Soc.Contains(hwStr, StringComparison.OrdinalIgnoreCase)) ||
                p.FileName.Contains(hwStr, StringComparison.OrdinalIgnoreCase) ||
                p.RelativePath.Contains(hwStr, StringComparison.OrdinalIgnoreCase));

            if (match != null) return match;
        }

        return _cachedData.Preloaders.FirstOrDefault(p => p.IsUnifiedDb)
            ?? _cachedData.Preloaders.FirstOrDefault();
    }

    /// <summary>
    /// Busca un payload de bypass de BROM para Kamakiri / Fenrir / SLA-DAA.
    /// </summary>
    public ManifestPayloadItem? FindBestPayload(ushort? hwCode)
    {
        EnsureLoaded();
        if (_cachedData == null || _cachedData.Payloads.Count == 0) return null;

        string? hwStr = hwCode.HasValue ? $"MT{hwCode.Value:X4}" : null;
        if (hwStr != null)
        {
            var match = _cachedData.Payloads.FirstOrDefault(p =>
                (!string.IsNullOrEmpty(p.Soc) && p.Soc.Contains(hwStr, StringComparison.OrdinalIgnoreCase)) ||
                p.FileName.Contains(hwStr, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        return _cachedData.Payloads.FirstOrDefault(p => p.Category == "BromPayload");
    }
}

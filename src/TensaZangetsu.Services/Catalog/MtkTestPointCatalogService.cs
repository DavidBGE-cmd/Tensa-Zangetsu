namespace TensaZangetsu.Services.Catalog;

public sealed record TestPointEntry(
    string ModelName,
    string Brand,
    string FilePath,
    string ImageFormat,
    long FileSizeBytes
);

/// <summary>
/// Catálogo y buscador de diagramas de puntos de prueba de hardware (TestPoints)
/// con más de 200 modelos indexados (Huawei, Honor, Xiaomi, Motorola, etc.).
/// </summary>
public sealed class MtkTestPointCatalogService
{
    private readonly string _testpointsDir;
    private readonly List<TestPointEntry> _entries = [];
    private bool _indexed;

    public MtkTestPointCatalogService(string? customDir = null)
    {
        _testpointsDir = customDir ?? ResolveTestPointsDirectory();
    }

    public string TestPointsDirectory => _testpointsDir;

    private static string ResolveTestPointsDirectory()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidates = [
            Path.Combine(baseDir, "ExternalAssets", "TESTPOINTS"),
            Path.Combine(baseDir, "mt-katool-FutureAssets", "DA FILES MTK 2026 v22", "DA FILES MTK 2026", "Huawei Honor 2025", "TESTPOINTS"),
            Path.Combine(baseDir, "..", "..", "..", "mt-katool-FutureAssets", "DA FILES MTK 2026 v22", "DA FILES MTK 2026", "Huawei Honor 2025", "TESTPOINTS"),
            Path.Combine(baseDir, "..", "..", "..", "..", "mt-katool-FutureAssets", "DA FILES MTK 2026 v22", "DA FILES MTK 2026", "Huawei Honor 2025", "TESTPOINTS")
        ];

        foreach (var c in candidates)
        {
            try
            {
                string full = Path.GetFullPath(c);
                if (Directory.Exists(full)) return full;
            }
            catch { }
        }

        var current = new DirectoryInfo(baseDir);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, "mt-katool-FutureAssets", "DA FILES MTK 2026 v22", "DA FILES MTK 2026", "Huawei Honor 2025", "TESTPOINTS");
            if (Directory.Exists(candidate)) return candidate;

            current = current.Parent;
        }

        return baseDir;
    }

    public IReadOnlyList<TestPointEntry> GetAll()
    {
        EnsureIndexed();
        return _entries;
    }

    public IEnumerable<TestPointEntry> Search(string query)
    {
        EnsureIndexed();
        if (string.IsNullOrWhiteSpace(query))
            return _entries;

        string clean = query.Trim().ToUpperInvariant();
        return _entries.Where(e =>
            e.ModelName.ToUpperInvariant().Contains(clean) ||
            e.Brand.ToUpperInvariant().Contains(clean));
    }

    public TestPointEntry? FindForModel(string model)
    {
        EnsureIndexed();
        string clean = model.Trim().ToUpperInvariant();
        return _entries.FirstOrDefault(e => e.ModelName.ToUpperInvariant().Contains(clean));
    }

    private void EnsureIndexed()
    {
        if (_indexed) return;
        _entries.Clear();

        var directoriesToScan = new List<string>();
        if (Directory.Exists(_testpointsDir)) directoriesToScan.Add(_testpointsDir);

        // Buscar directorios adicionales con diagramas y testpoints (Moto Tiger T606, Oppo Qualcomm, Samsung)
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] searchRoots = [
            Path.Combine(baseDir, "ExternalAssets"),
            Path.Combine(baseDir, "mt-katool-FutureAssets"),
            Path.Combine(baseDir, "..", "..", "..", "mt-katool-FutureAssets"),
            Path.Combine(baseDir, "..", "..", "..", "..", "mt-katool-FutureAssets")
        ];

        foreach (var root in searchRoots)
        {
            try
            {
                if (!Directory.Exists(root)) continue;

                // LOADER TIGER T606_64_MOTO
                foreach (var d in Directory.GetDirectories(root, "*T606*", SearchOption.AllDirectories))
                {
                    if (!directoriesToScan.Contains(d)) directoriesToScan.Add(d);
                }

                // Qualcomm Loader OPPO
                string qcomOppo = Path.Combine(root, "Qualcomm Loader", "OPPO");
                if (Directory.Exists(qcomOppo))
                {
                    foreach (var d in Directory.GetDirectories(qcomOppo, "*", SearchOption.AllDirectories))
                    {
                        if (!directoriesToScan.Contains(d)) directoriesToScan.Add(d);
                    }
                }
            }
            catch { }
        }

        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in directoriesToScan)
        {
            try
            {
                if (!Directory.Exists(dir)) continue;

                var files = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase));

                foreach (var file in files)
                {
                    if (!seenFiles.Add(file)) continue;

                    string fileName = Path.GetFileNameWithoutExtension(file);
                    string ext = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
                    var fi = new FileInfo(file);

                    string brand = "Generic";
                    string modelDisplay = fileName;

                    if (fileName.Contains("Huawei", StringComparison.OrdinalIgnoreCase) || file.Contains("Huawei", StringComparison.OrdinalIgnoreCase))
                    {
                        brand = "Huawei";
                    }
                    else if (fileName.Contains("Honor", StringComparison.OrdinalIgnoreCase) || file.Contains("Honor", StringComparison.OrdinalIgnoreCase))
                    {
                        brand = "Honor";
                    }
                    else if (fileName.Contains("CC9", StringComparison.OrdinalIgnoreCase) || fileName.Contains("Xiaomi", StringComparison.OrdinalIgnoreCase) || fileName.Contains("Redmi", StringComparison.OrdinalIgnoreCase))
                    {
                        brand = "Xiaomi";
                    }
                    else if (file.Contains("T606", StringComparison.OrdinalIgnoreCase) || fileName.Contains("T606", StringComparison.OrdinalIgnoreCase) || fileName.Contains("MOTO", StringComparison.OrdinalIgnoreCase))
                    {
                        brand = "Motorola";
                        modelDisplay = $"Motorola Tiger T606 ({fileName})";
                    }
                    else if (file.Contains("OPPO", StringComparison.OrdinalIgnoreCase) || fileName.Contains("Reno", StringComparison.OrdinalIgnoreCase))
                    {
                        brand = "Oppo";
                        if (file.Contains("Reno 4", StringComparison.OrdinalIgnoreCase)) modelDisplay = "Oppo Reno 4 (EDL 9008 TestPoint)";
                        else if (file.Contains("Reno 5", StringComparison.OrdinalIgnoreCase)) modelDisplay = "Oppo Reno 5 (EDL 9008 TestPoint)";
                        else if (file.Contains("Reno 6", StringComparison.OrdinalIgnoreCase)) modelDisplay = "Oppo Reno 6 (EDL 9008 TestPoint)";
                    }
                    else if (file.Contains("Samsung", StringComparison.OrdinalIgnoreCase) || fileName.StartsWith("A125", StringComparison.OrdinalIgnoreCase) || fileName.Contains("Samsung", StringComparison.OrdinalIgnoreCase))
                    {
                        brand = "Samsung";
                    }

                    _entries.Add(new TestPointEntry(
                        ModelName: modelDisplay,
                        Brand: brand,
                        FilePath: file,
                        ImageFormat: ext,
                        FileSizeBytes: fi.Length
                    ));
                }
            }
            catch { }
        }

        // Ordenar alfabéticamente por marca y modelo
        _entries.Sort((a, b) =>
        {
            int c = string.Compare(a.Brand, b.Brand, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.Compare(a.ModelName, b.ModelName, StringComparison.OrdinalIgnoreCase);
        });

        _indexed = true;
    }
}

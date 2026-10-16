using System.Security.Cryptography;
using System.Text.RegularExpressions;
using TensaZangetsu.Protocol.Abstractions;
using TensaZangetsu.Protocol.Mtk.Exploits.Penumbra;
using TensaZangetsu.Protocol.Mtk.Loaders;

namespace TensaZangetsu.Services.Loaders;

/// <summary>
/// Servicio de auto-resolución y emparejamiento masivo de Loaders (Firehose, DA V5/V6, Preloader, FDL1/FDL2).
/// Soporta catálogo Multi-DA por modelo/SoC (sin descartar variantes con SHA-256 diferente),
/// resolución de punteros .link, indexación de Samsung por BIT y SoC, paquetes firmados de Xiaomi/Poco,
/// variantes RPMB y HW_ID de Huawei/Honor, perfiles Oppo/Realme, Motorola, Vivo, Transsion y base unificada de Preloaders.
/// </summary>
public sealed class AutoLoaderResolverService
{
    private readonly string _baseDirectory;
    private readonly List<string> _rootSearchPaths = [];
    private readonly List<MtkDaVariant> _indexedVariants = [];
    private readonly object _indexLock = new();
    private bool _variantsIndexed = false;

    public AutoLoaderResolverService(string? baseDirectory = null)
    {
        _baseDirectory = baseDirectory ?? ResolveAssetsDirectory();
        InitializeSearchPaths();
    }

    public string BaseDirectory => _baseDirectory;

    private void InitializeSearchPaths()
    {
        string[] candidates = [
            _baseDirectory,
            Path.Combine(_baseDirectory, "..", "..", ".."),
            Path.Combine(_baseDirectory, "..", "..", "..", ".."),
            AppDomain.CurrentDomain.BaseDirectory
        ];

        foreach (var c in candidates)
        {
            try
            {
                string full = Path.GetFullPath(c);
                if (Directory.Exists(full) && !_rootSearchPaths.Contains(full))
                    _rootSearchPaths.Add(full);
            }
            catch { }
        }
    }

    private static string ResolveAssetsDirectory()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        if (Directory.Exists(Path.Combine(baseDir, "ExternalAssets")) ||
            Directory.Exists(Path.Combine(baseDir, "Assets", "Loaders")) ||
            Directory.Exists(Path.Combine(baseDir, "mt-katool-FutureAssets")))
            return baseDir;

        var current = new DirectoryInfo(baseDir);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "mt-katool-FutureAssets")))
                return current.FullName;

            if (Directory.Exists(Path.Combine(current.FullName, "ExternalAssets")))
                return current.FullName;

            string candidate = Path.Combine(current.FullName, "Assets");
            if (Directory.Exists(Path.Combine(candidate, "Loaders")))
                return current.FullName;

            string srcCandidate = Path.Combine(current.FullName, "src", "TensaZangetsu.App");
            if (Directory.Exists(Path.Combine(srcCandidate, "Assets", "Loaders")))
                return srcCandidate;

            current = current.Parent;
        }

        return baseDir;
    }

    /// <summary>
    /// Obtiene todas las carpetas existentes que coincidan con la ruta relativa en Assets, ExternalAssets y mt-katool-FutureAssets.
    /// </summary>
    public List<string> GetProbeDirectories(string subPath)
    {
        var list = new List<string>();

        foreach (var r in _rootSearchPaths)
        {
            try
            {
                string pFuture = Path.Combine(r, "mt-katool-FutureAssets", subPath);
                if (Directory.Exists(pFuture) && !list.Contains(pFuture)) list.Add(pFuture);

                string p1 = Path.Combine(r, "ExternalAssets", subPath);
                if (Directory.Exists(p1) && !list.Contains(p1)) list.Add(p1);

                string p2 = Path.Combine(r, "Assets", subPath);
                if (Directory.Exists(p2) && !list.Contains(p2)) list.Add(p2);

                string p3 = Path.Combine(r, "src", "TensaZangetsu.App", "Assets", subPath);
                if (Directory.Exists(p3) && !list.Contains(p3)) list.Add(p3);

                string pDirect = Path.Combine(r, subPath);
                if (Directory.Exists(pDirect) && !list.Contains(pDirect)) list.Add(pDirect);
            }
            catch { }
        }

        return list;
    }

    #region Multi-DA Catalog & Classification

    /// <summary>
    /// Garantiza la indexación y clasificación de todas las variantes DA disponibles en el catálogo.
    /// </summary>
    public void EnsureVariantsIndexed()
    {
        if (_variantsIndexed) return;
        lock (_indexLock)
        {
            if (_variantsIndexed) return;

            var daDirs = GetProbeDirectories("CUSTOM DA & PRELOADER EMI/CUSTOM DA");
            daDirs.AddRange(GetProbeDirectories("DA FILES MTK 2026 v22/DA FILES MTK 2026"));
            daDirs.AddRange(GetProbeDirectories("Loaders/Mtk"));

            var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dir in daDirs)
            {
                if (!Directory.Exists(dir)) continue;

                var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
                    .Where(f => (f.EndsWith(".mtk", StringComparison.OrdinalIgnoreCase) ||
                                 f.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)) &&
                                !Path.GetFileName(f).StartsWith("preloader", StringComparison.OrdinalIgnoreCase) &&
                                !f.Contains("TESTPOINTS", StringComparison.OrdinalIgnoreCase));

                foreach (var file in files)
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.Length < 1024 || fi.Length > 30 * 1024 * 1024) continue;

                        string sha = ComputeFileSha256(file);
                        if (seenHashes.Contains(sha)) continue;
                        seenHashes.Add(sha);

                        var variant = ClassifyDaFile(file, sha);
                        _indexedVariants.Add(variant);
                    }
                    catch { }
                }
            }

            _variantsIndexed = true;
        }
    }

    /// <summary>
    /// Clasifica un archivo DA individual extrayendo metadatos: SecurityPatchOrOs, BinaryBit, OperationRole y ConnectionStage.
    /// </summary>
    private static MtkDaVariant ClassifyDaFile(string filePath, string sha256)
    {
        string fileName = Path.GetFileName(filePath);
        string nameUpper = fileName.ToUpperInvariant();
        string pathUpper = filePath.ToUpperInvariant();

        string brand = DetectBrand(filePath);
        string model = ExtractModelOrCodename(fileName, filePath);
        string? soc = ExtractSoc(fileName, filePath);

        // 1. Clasificación SecurityPatchOrOs y rango de versión
        string os = "Legacy / Stock";
        int osRank = 0;

        if (nameUpper.Contains("OS2.0") || nameUpper.Contains("ANDROID15") || nameUpper.Contains("ANDROID_15") || nameUpper.Contains("ANDROID-15"))
        {
            os = "HyperOS 2.0 / Android 15";
            osRank = 15;
        }
        else if (nameUpper.Contains("OS1.0") || nameUpper.Contains("ANDROID14") || nameUpper.Contains("ANDROID_14") || nameUpper.Contains("V816.0"))
        {
            os = "HyperOS 1.0 / Android 14";
            osRank = 14;
        }
        else if (nameUpper.Contains("V14.0") || nameUpper.Contains("ANDROID_13") || nameUpper.Contains("ANDROID13"))
        {
            os = "MIUI 14 / Android 13";
            osRank = 13;
        }
        else if (nameUpper.Contains("13.0") || nameUpper.Contains("14.X"))
        {
            os = "Android 13 / 14";
            osRank = 13;
        }

        // 2. Clasificación BinaryBit
        string? bit = null;
        int numericBit = 0;
        var bitMatch = Regex.Match(fileName, @"(?:BIT[_\-\s]*([0-9A-Fa-f]+)|\.B([0-9A-Fa-f]+))", RegexOptions.IgnoreCase);
        if (bitMatch.Success)
        {
            string rawBit = bitMatch.Groups[1].Success ? bitMatch.Groups[1].Value : bitMatch.Groups[2].Value;
            bit = $"BIT {rawBit.ToUpperInvariant()}";
            if (int.TryParse(rawBit, out int nb)) numericBit = nb;
            else if (rawBit.Equals("A", StringComparison.OrdinalIgnoreCase)) numericBit = 10;
            else if (rawBit.Equals("B", StringComparison.OrdinalIgnoreCase)) numericBit = 11;
            else if (rawBit.Equals("C", StringComparison.OrdinalIgnoreCase)) numericBit = 12;
            else if (rawBit.Equals("D", StringComparison.OrdinalIgnoreCase)) numericBit = 13;
        }

        // 3. Clasificación OperationRole
        MtkDaOperationRole role = MtkDaOperationRole.StandardFlash;
        if (nameUpper.Contains("_RPMB") || nameUpper.Contains("RPMB"))
        {
            role = MtkDaOperationRole.RpmbService;
        }
        else if (nameUpper.Contains("_HW_ID") || nameUpper.Contains("HW_ID"))
        {
            role = MtkDaOperationRole.HwIdRepair;
        }
        else if (nameUpper.Contains("ENG") || nameUpper.Contains("NOSEC") || nameUpper.Contains("PATCH"))
        {
            role = MtkDaOperationRole.NoSecPatched;
        }

        // 4. Clasificación ConnectionStage
        MtkDaConnectionStage stage = MtkDaConnectionStage.BROM;
        if (nameUpper.Contains("V6") || pathUpper.Contains("V6") || File.Exists(Path.Combine(Path.GetDirectoryName(filePath)!, "flash.xml")))
        {
            stage = MtkDaConnectionStage.V6_XML;
        }
        else if (nameUpper.Contains("SWSEC") || nameUpper.Contains("_DA_BR") || nameUpper.Contains("SIG"))
        {
            stage = MtkDaConnectionStage.SwSecCrypto20;
        }
        else if (nameUpper.Contains("PRELOADER") || pathUpper.Contains("PRELOADER"))
        {
            stage = MtkDaConnectionStage.Preloader;
        }

        // 5. Etiqueta amigable para la Interfaz Gráfica (UI)
        string bitPart = bit != null ? $" [{bit}]" : "";
        string osPart = os != "Legacy / Stock" ? $" - {os}" : "";
        string rolePart = role != MtkDaOperationRole.StandardFlash ? $" [{role}]" : "";
        string label = $"{model}{bitPart}{osPart}{rolePart} ({stage})";

        string? dir = Path.GetDirectoryName(filePath);
        string? authPath = dir != null ? Directory.GetFiles(dir, "*.auth", SearchOption.TopDirectoryOnly).FirstOrDefault() : null;
        string? xmlPath = dir != null ? Directory.GetFiles(dir, "flash.xml", SearchOption.TopDirectoryOnly).FirstOrDefault() : null;

        return new MtkDaVariant(
            FilePath: filePath,
            FileName: fileName,
            Sha256: sha256,
            Brand: brand,
            ModelOrCodename: model,
            Soc: soc,
            SecurityPatchOrOs: os,
            BinaryBit: bit,
            NumericBit: numericBit,
            OsVersionRank: osRank,
            OperationRole: role,
            ConnectionStage: stage,
            DisplayLabel: label,
            AssociatedAuthPath: authPath,
            AssociatedXmlPath: xmlPath
        );
    }

    /// <summary>
    /// Devuelve todas las variantes DA clasificadas para un modelo o codename específico,
    /// ordenadas con prioridad: mayor BIT, SO más reciente, y rol StandardFlash primero.
    /// </summary>
    public IReadOnlyList<MtkDaVariant> GetVariantsForModel(string modelOrCodename)
    {
        EnsureVariantsIndexed();
        if (string.IsNullOrWhiteSpace(modelOrCodename)) return Array.Empty<MtkDaVariant>();

        string target = modelOrCodename.Trim().ToUpperInvariant()
            .Replace("SAMSUNG_", "")
            .Replace("SM-", "")
            .Replace("-", "");

        return _indexedVariants
            .Where(v => v.ModelOrCodename.ToUpperInvariant().Contains(target) ||
                        target.Contains(v.ModelOrCodename.ToUpperInvariant()))
            .OrderByDescending(v => v.NumericBit)
            .ThenByDescending(v => v.OsVersionRank)
            .ThenBy(v => v.OperationRole == MtkDaOperationRole.StandardFlash ? 0 : 1)
            .ToList();
    }

    /// <summary>
    /// Devuelve todas las variantes DA que coincidan con un código de procesador (ej. 0x0707 para MT6768).
    /// </summary>
    public IReadOnlyList<MtkDaVariant> GetVariantsForHwCode(ushort hwCode)
    {
        EnsureVariantsIndexed();
        string hwStr = $"MT{hwCode:X4}";

        return _indexedVariants
            .Where(v => (v.Soc != null && v.Soc.Contains(hwStr, StringComparison.OrdinalIgnoreCase)) ||
                        v.FileName.Contains(hwStr, StringComparison.OrdinalIgnoreCase) ||
                        v.FilePath.Contains(hwStr, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v.NumericBit)
            .ThenByDescending(v => v.OsVersionRank)
            .ThenBy(v => v.OperationRole == MtkDaOperationRole.StandardFlash ? 0 : 1)
            .ToList();
    }

    /// <summary>
    /// Devuelve la lista de modelos de dispositivos conocidos que cuentan con DAs dedicados en el catálogo.
    /// </summary>
    public IReadOnlyList<string> GetKnownDeviceModels()
    {
        EnsureVariantsIndexed();
        return _indexedVariants
            .Select(v => v.ModelOrCodename)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(m => m)
            .ToList();
    }

    #endregion

    #region Device Resolution (Automatic with Multi-DA fallback support)

    /// <summary>
    /// Resuelve y devuelve el paquete de loaders coincidente para la huella de hardware dada.
    /// En modo automático, selecciona el DA con el BIT o versión de SO más reciente
    /// y llena la lista de fallbacks con las variantes restantes del mismo modelo.
    /// Si se especifica un preferredDaPath (elegido manualmente en la UI), se utiliza dicho DA directamente.
    /// </summary>
    public ResolvedLoaderBundle ResolveForDevice(DeviceHardwareFingerprint fingerprint, string? preferredDaPath = null)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);

        return fingerprint.Platform.ToUpperInvariant() switch
        {
            "QCOM" or "QUALCOMM" => ResolveQcom(fingerprint),
            "MTK" or "MEDIATEK" => ResolveMtk(fingerprint, preferredDaPath),
            "SPRD" or "UNISOC" => ResolveSprd(fingerprint),
            _ => new ResolvedLoaderBundle()
        };
    }

    private ResolvedLoaderBundle ResolveMtk(DeviceHardwareFingerprint fp, string? preferredDaPath = null)
    {
        EnsureVariantsIndexed();

        ushort? targetHwCode = fp.MtkHwCode;
        List<MtkDaVariant> candidates = [];

        // 1. Si el usuario seleccionó manualmente una variante en la UI
        if (!string.IsNullOrEmpty(preferredDaPath))
        {
            var preferred = _indexedVariants.FirstOrDefault(v => v.FilePath.Equals(preferredDaPath, StringComparison.OrdinalIgnoreCase));
            if (preferred != null)
            {
                candidates.Add(preferred);
            }
        }

        // 2. Si no hay preferido o no se encontró, buscar candidatos por código de hardware
        if (candidates.Count == 0 && targetHwCode.HasValue)
        {
            candidates.AddRange(GetVariantsForHwCode(targetHwCode.Value));
        }

        // 3. Fallback a archivos DA genéricos o AllInOne
        if (candidates.Count == 0)
        {
            var mtkDirs = GetProbeDirectories("Loaders/Mtk");
            mtkDirs.AddRange(GetProbeDirectories("DA FILES MTK 2026 v22/DA FILES MTK 2026"));

            foreach (var mtkDir in mtkDirs)
            {
                var daFiles = Directory.GetFiles(mtkDir, "*.*", SearchOption.AllDirectories)
                    .Where(f => (f.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".mtk", StringComparison.OrdinalIgnoreCase)) &&
                                !f.Contains("Preloader", StringComparison.OrdinalIgnoreCase))
                    .Distinct();

                foreach (var daFile in daFiles)
                {
                    if (targetHwCode.HasValue && DaSupportsHwCode(daFile, targetHwCode.Value))
                    {
                        candidates.Add(ClassifyDaFile(daFile, ComputeFileSha256(daFile)));
                        break;
                    }
                }
                if (candidates.Count > 0) break;
            }
        }

        MtkDaVariant? selected = candidates.FirstOrDefault();
        string? matchedDa = selected?.FilePath;

        // Lista de fallbacks: variantes alternativas ordenadas por prioridad para reintentar si el handshake falla
        var fallbackPaths = candidates.Skip(1).Select(c => c.FilePath).ToList();

        // Resolver Preloader y Auth correspondientes
        string? matchedPreloader = ResolveUnifiedPreloader(selected?.ModelOrCodename, targetHwCode);
        string? authPath = selected?.AssociatedAuthPath ?? FindAuthFile();

        return new ResolvedLoaderBundle(
            MtkDaPath: matchedDa,
            MtkPreloaderPath: matchedPreloader,
            AuthFilePath: authPath,
            ModelMatch: selected?.ModelOrCodename,
            SelectedVariant: selected,
            CandidateDaVariants: candidates,
            CandidateDaFallbacks: fallbackPaths);
    }

    #endregion

    #region Qualcomm Resolution

    private ResolvedLoaderBundle ResolveQcom(DeviceHardwareFingerprint fp)
    {
        var qcomDirs = GetProbeDirectories("Loaders/Qcom");
        qcomDirs.AddRange(GetProbeDirectories("Qualcomm Loader"));

        if (qcomDirs.Count == 0)
            return new ResolvedLoaderBundle();

        string? hwId = fp.QcomMsmHwId?.TrimStart('0', 'x', 'X');
        string? matchedFirehose = null;

        foreach (var qcomDir in qcomDirs)
        {
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

            var files = Directory.GetFiles(qcomDir, "*.*", SearchOption.AllDirectories)
                .Where(IsFirehoseExtension)
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

    #endregion

    #region Link File Resolution (.link)

    public string? ResolveLinkDirectory(string linkFilePath)
    {
        if (!File.Exists(linkFilePath)) return null;

        string fileName = Path.GetFileName(linkFilePath);
        if (!fileName.EndsWith(".link", StringComparison.OrdinalIgnoreCase)) return null;

        string stem = fileName[..^5];
        int dot = stem.IndexOf('.');
        if (dot <= 0) return null;

        string folder1 = stem[..dot];
        string folder2 = stem[(dot + 1)..];

        string? parent = Path.GetDirectoryName(linkFilePath);
        while (parent != null)
        {
            string candidate = Path.Combine(parent, folder1, folder2);
            if (Directory.Exists(candidate)) return candidate;

            if (Path.GetFileName(parent).Equals("DA FILES MTK 2026", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(parent).Equals("DA FILES MTK 2026 v22", StringComparison.OrdinalIgnoreCase))
            {
                string c2 = Path.Combine(parent, "DA FILES MTK 2026", folder1, folder2);
                if (Directory.Exists(c2)) return c2;
            }

            parent = Path.GetDirectoryName(parent);
        }

        var daBaseDirs = GetProbeDirectories("DA FILES MTK 2026 v22/DA FILES MTK 2026");
        foreach (var root in daBaseDirs)
        {
            string candidate = Path.Combine(root, folder1, folder2);
            if (Directory.Exists(candidate)) return candidate;
        }

        return null;
    }

    public string? FindDaInLinkTarget(string directoryPath)
    {
        if (!Directory.Exists(directoryPath)) return null;

        var bin = Directory.GetFiles(directoryPath, "*.bin", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(f => !Path.GetFileName(f).StartsWith("preloader", StringComparison.OrdinalIgnoreCase));
        if (bin != null) return bin;

        var mtk = Directory.GetFiles(directoryPath, "*.mtk", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (mtk != null) return mtk;

        var link = Directory.GetFiles(directoryPath, "*.link", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (link != null)
        {
            string? target = ResolveLinkDirectory(link);
            if (target != null) return FindDaInLinkTarget(target);
        }

        return null;
    }

    #endregion

    #region Preloaders & Scatters Indexing

    public string? ResolveUnifiedPreloader(string? modelOrBoard, ushort? hwCode = null)
    {
        var preloaderDirs = GetProbeDirectories("UNIFIED_PRELOADERS_DB");
        preloaderDirs.AddRange(GetProbeDirectories("CUSTOM DA & PRELOADER EMI/CUSTOM PRELOADER EMI"));
        preloaderDirs.AddRange(GetProbeDirectories("DA FILES MTK 2026 v22/New Devices  preloader 2026"));
        preloaderDirs.AddRange(GetProbeDirectories("Preloaders"));

        if (!string.IsNullOrEmpty(modelOrBoard))
        {
            string clean = modelOrBoard.Trim();
            foreach (var pDir in preloaderDirs)
            {
                var match = Directory.GetFiles(pDir, $"*{clean}*.bin", SearchOption.AllDirectories).FirstOrDefault();
                if (match != null) return match;
            }
        }

        if (hwCode.HasValue)
        {
            string[] patterns = [
                $"MT{hwCode.Value:X4}",
                $"MT{hwCode.Value:D4}",
                $"{hwCode.Value:X4}",
                $"{hwCode.Value}"
            ];

            foreach (var pDir in preloaderDirs)
            {
                foreach (var pat in patterns)
                {
                    var match = Directory.GetFiles(pDir, $"*preloader*{pat}*.bin", SearchOption.AllDirectories).FirstOrDefault();
                    if (match != null) return match;
                }
            }
        }

        foreach (var pDir in preloaderDirs)
        {
            var match = Directory.GetFiles(pDir, "preloader*.bin", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (match != null) return match;
        }

        return null;
    }

    public string? ResolveScatter(string? socOrModel)
    {
        if (string.IsNullOrWhiteSpace(socOrModel)) return null;

        var scatterDirs = GetProbeDirectories("DA FILES MTK 2026 v22/DA FILES MTK 2026");
        scatterDirs.AddRange(GetProbeDirectories("CUSTOM DA & PRELOADER EMI"));
        scatterDirs.AddRange(GetProbeDirectories("DA FILES MTK 2026 v22/DA FILES MTK 2026/Huawei Honor 2025/mtk_scatter"));

        string clean = socOrModel.Trim();

        foreach (var dir in scatterDirs)
        {
            var scatters = Directory.GetFiles(dir, "*scatter*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

            var match = scatters.FirstOrDefault(f => f.Contains(clean, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        return null;
    }

    #endregion

    #region Helpers & Sprd

    private ResolvedLoaderBundle ResolveSprd(DeviceHardwareFingerprint fp)
    {
        var sprdDirs = GetProbeDirectories("Loaders/Sprd");
        sprdDirs.AddRange(GetProbeDirectories("LOADER-TIGER-T606_64_MOTO_Needrom"));

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

        ulong fdl1Address = 0x5500;
        ulong fdl2Address = 0x9f000000;

        if (!string.IsNullOrEmpty(socId))
        {
            var matchInFolder = fdlFiles.FirstOrDefault(f => f.Contains(socId, StringComparison.OrdinalIgnoreCase));
            if (matchInFolder != null && fdl1 == null) fdl1 = matchInFolder;

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
            .FirstOrDefault(IsFirehoseExtension);
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
        authDirs.AddRange(GetProbeDirectories("DA FILES MTK 2026 v22/SP_Flash_Tool_v6_v5/auth_sv5"));
        authDirs.AddRange(GetProbeDirectories("DA FILES MTK 2026 v22/SP_Flash_Tool_v6_v5/auth_sv6"));

        foreach (var dir in authDirs)
        {
            var match = Directory.GetFiles(dir, "*.auth", SearchOption.AllDirectories).FirstOrDefault()
                ?? Directory.GetFiles(dir, "*auth*.bin", SearchOption.AllDirectories).FirstOrDefault();
            if (match != null) return match;
        }

        return null;
    }

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    private static string DetectBrand(string path)
    {
        string pUpper = path.ToUpperInvariant();
        if (pUpper.Contains("SAMSUNG")) return "Samsung";
        if (pUpper.Contains("XIAOMI") || pUpper.Contains("REDMI") || pUpper.Contains("POCO")) return "Xiaomi";
        if (pUpper.Contains("OPPO") || pUpper.Contains("REALME")) return "Oppo";
        if (pUpper.Contains("HUAWEI") || pUpper.Contains("HONOR")) return "Huawei";
        if (pUpper.Contains("MOTO") || pUpper.Contains("MOTOROLA")) return "Motorola";
        if (pUpper.Contains("VIVO") || pUpper.Contains("BBK")) return "Vivo";
        if (pUpper.Contains("INFINIX") || pUpper.Contains("TECNO")) return "Transsion";
        if (pUpper.Contains("TCL") || pUpper.Contains("ALCATEL")) return "Tcl";
        return "Generic";
    }

    private static string ExtractModelOrCodename(string fileName, string filePath)
    {
        // Limpieza de prefijos comunes
        string clean = Path.GetFileNameWithoutExtension(fileName)
            .Replace("Samsung_", "", StringComparison.OrdinalIgnoreCase)
            .Replace("SAMSUNG_", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Redmi_", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Poco_", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Motorola_", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Mototola_", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Huawei_Honor_", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Oppo_Realme_", "", StringComparison.OrdinalIgnoreCase);

        // Si contiene codename conocido (lake, gale, moon, tides, gold, duchamp, spark, flare)
        string[] codenames = ["lake", "gale", "moon", "tides", "gold", "duchamp", "spark", "flare"];
        foreach (var code in codenames)
        {
            if (clean.Contains(code, StringComparison.OrdinalIgnoreCase))
                return code.ToUpperInvariant();
        }

        // Extraer identificador de modelo (ej. A155F, A055F, A165M, X820F, Note_13_5G)
        var match = Regex.Match(clean, @"([A-Z0-9]+(?:-[A-Z0-9]+)?)", RegexOptions.IgnoreCase);
        if (match.Success && match.Value.Length >= 3)
            return match.Value.ToUpperInvariant();

        return clean;
    }

    private static string? ExtractSoc(string fileName, string filePath)
    {
        var match = Regex.Match(fileName + " " + filePath, @"(MT\d{4}[A-Z]?|SM\d{4}|T606|T610|T612|T616|UMS\d{4})", RegexOptions.IgnoreCase);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }

    #endregion
}

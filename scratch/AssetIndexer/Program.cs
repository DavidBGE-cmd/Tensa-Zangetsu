using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AssetIndexer;

class Program
{
    static void Main(string[] args)
    {
        string rootDir = @"c:\Users\dbge_admin\TensaZangetsu\mt-katool-FutureAssets";
        string outputJson = Path.Combine(rootDir, "AssetsManifest.json");

        Console.WriteLine($"Starting indexing of {rootDir}...");

        var manifest = new
        {
            GeneratedAt = DateTime.UtcNow.ToString("o"),
            RootDirectory = rootDir,
            Stats = new Dictionary<string, int>(),
            DownloadAgents = new List<object>(),
            Links = new List<object>(),
            Preloaders = new List<object>(),
            Scatters = new List<object>(),
            TestPoints = new List<object>(),
            NativeTools = new List<object>(),
            Payloads = new List<object>()
        };

        // 1. Index .link files and their target resolutions
        Console.WriteLine("Indexing .link files...");
        var linkFiles = Directory.GetFiles(rootDir, "*.link", SearchOption.AllDirectories);
        var resolvedLinks = new List<object>();

        string daBaseDir = Path.Combine(rootDir, @"DA FILES MTK 2026 v22\DA FILES MTK 2026");

        foreach (var linkFile in linkFiles)
        {
            string linkName = Path.GetFileName(linkFile);
            string linkRelPath = Path.GetRelativePath(rootDir, linkFile);
            string targetFolder = ResolveLinkTarget(linkName, daBaseDir);
            bool targetExists = !string.IsNullOrEmpty(targetFolder) && Directory.Exists(targetFolder);

            resolvedLinks.Add(new
            {
                LinkFile = linkRelPath,
                LinkName = linkName,
                TargetFolder = targetFolder != null ? Path.GetRelativePath(rootDir, targetFolder) : null,
                TargetExists = targetExists
            });
        }

        // 2. Index Download Agents (.bin, .mtk, .auth, flash.xml)
        Console.WriteLine("Indexing Download Agents...");
        var allFiles = Directory.GetFiles(rootDir, "*.*", SearchOption.AllDirectories);
        var daList = new List<object>();

        foreach (var file in allFiles)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            string fileName = Path.GetFileName(file);
            string relPath = Path.GetRelativePath(rootDir, file);

            if (relPath.Contains(".git") || relPath.Contains("UNIFIED_PRELOADERS_DB") || relPath.Contains("TESTPOINTS"))
                continue;

            bool isDa = false;
            string brand = DetectBrand(relPath);
            string soc = DetectSoc(fileName, relPath);
            string bit = DetectBit(fileName);
            string category = "Other";

            if (ext == ".mtk")
            {
                isDa = true;
                category = "CustomDA";
            }
            else if (ext == ".auth")
            {
                isDa = true;
                category = "AuthenticationKey";
            }
            else if (ext == ".bin")
            {
                if (fileName.Contains("DA", StringComparison.OrdinalIgnoreCase) ||
                    relPath.Contains("DA FILES", StringComparison.OrdinalIgnoreCase) ||
                    relPath.Contains("CUSTOM DA", StringComparison.OrdinalIgnoreCase) ||
                    relPath.Contains("Samsung DA-AUTH", StringComparison.OrdinalIgnoreCase))
                {
                    if (!fileName.Contains("preloader", StringComparison.OrdinalIgnoreCase))
                    {
                        isDa = true;
                        category = "DownloadAgentBin";
                    }
                }
            }
            else if (fileName.Equals("flash.xml", StringComparison.OrdinalIgnoreCase) ||
                     relPath.Contains("download_agent", StringComparison.OrdinalIgnoreCase))
            {
                isDa = true;
                category = "DaV6XmlPackage";
            }

            if (isDa)
            {
                var fi = new FileInfo(file);
                daList.Add(new
                {
                    RelativePath = relPath,
                    FileName = fileName,
                    Size = fi.Length,
                    Extension = ext,
                    Category = category,
                    Brand = brand,
                    Soc = soc,
                    Bit = bit
                });
            }
        }

        // 3. Index Preloaders
        Console.WriteLine("Indexing Preloaders...");
        var preloaderList = new List<object>();
        foreach (var file in allFiles)
        {
            string fileName = Path.GetFileName(file);
            string ext = Path.GetExtension(file).ToLowerInvariant();
            string relPath = Path.GetRelativePath(rootDir, file);

            if ((ext == ".bin" || ext == ".img") &&
                (fileName.StartsWith("preloader", StringComparison.OrdinalIgnoreCase) ||
                 fileName.Contains("preloader", StringComparison.OrdinalIgnoreCase) ||
                 relPath.Contains("PRELOADER", StringComparison.OrdinalIgnoreCase)))
            {
                if (relPath.Contains(".git")) continue;

                var fi = new FileInfo(file);
                string brand = DetectBrand(relPath);
                string soc = DetectSoc(fileName, relPath);

                preloaderList.Add(new
                {
                    RelativePath = relPath,
                    FileName = fileName,
                    Size = fi.Length,
                    Brand = brand,
                    Soc = soc,
                    IsUnifiedDb = relPath.Contains("UNIFIED_PRELOADERS_DB")
                });
            }
        }

        // 4. Index Scatters
        Console.WriteLine("Indexing Scatters...");
        var scatterList = new List<object>();
        foreach (var file in allFiles)
        {
            string fileName = Path.GetFileName(file);
            string ext = Path.GetExtension(file).ToLowerInvariant();
            string relPath = Path.GetRelativePath(rootDir, file);

            if (fileName.Contains("scatter", StringComparison.OrdinalIgnoreCase) &&
                (ext == ".txt" || ext == ".xml"))
            {
                var fi = new FileInfo(file);
                string soc = DetectSoc(fileName, relPath);
                string brand = DetectBrand(relPath);

                scatterList.Add(new
                {
                    RelativePath = relPath,
                    FileName = fileName,
                    Size = fi.Length,
                    Extension = ext,
                    Soc = soc,
                    Brand = brand
                });
            }
        }

        // 5. Index TestPoints
        Console.WriteLine("Indexing TestPoints...");
        var tpList = new List<object>();
        foreach (var file in allFiles)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            string relPath = Path.GetRelativePath(rootDir, file);

            if ((ext == ".jpg" || ext == ".jpeg" || ext == ".png") &&
                (relPath.Contains("TESTPOINTS", StringComparison.OrdinalIgnoreCase) ||
                 relPath.Contains("LOADER-TIGER-T606", StringComparison.OrdinalIgnoreCase)))
            {
                var fi = new FileInfo(file);
                string fileName = Path.GetFileNameWithoutExtension(file);
                tpList.Add(new
                {
                    RelativePath = relPath,
                    Model = fileName,
                    Size = fi.Length,
                    Format = ext.TrimStart('.')
                });
            }
        }

        // 6. Index Payloads (BROM, Fenrir, etc.)
        Console.WriteLine("Indexing Payloads...");
        var payloadList = new List<object>();
        foreach (var file in allFiles)
        {
            string relPath = Path.GetRelativePath(rootDir, file);
            string fileName = Path.GetFileName(file);

            if (relPath.Contains(@"MTK Flash TOOL\payloads") ||
                relPath.Contains(@"exploits_collection-master\payloads") ||
                relPath.Contains(@"fenrir\bin") ||
                fileName.EndsWith("_payload.bin", StringComparison.OrdinalIgnoreCase) ||
                fileName == "da_x.bin" || fileName == "pl.bin" || fileName == "stage2.bin")
            {
                var fi = new FileInfo(file);
                string soc = DetectSoc(fileName, relPath);
                payloadList.Add(new
                {
                    RelativePath = relPath,
                    FileName = fileName,
                    Size = fi.Length,
                    Soc = soc,
                    Category = relPath.Contains(@"fenrir\bin") ? "FenrirTargetFirmware" : "BromPayload"
                });
            }
        }

        // 7. Index Native Tools
        Console.WriteLine("Indexing Native Tools...");
        var nativeTools = new List<object>();
        foreach (var file in allFiles)
        {
            string relPath = Path.GetRelativePath(rootDir, file);
            string ext = Path.GetExtension(file).ToLowerInvariant();

            if (relPath.Contains(@"\ADB\") || relPath.Contains(@"SP_Flash_Tool_v6_v5\"))
            {
                if (ext == ".exe" || ext == ".dll" || ext == ".xsd" || ext == ".xml" || ext == ".auth")
                {
                    var fi = new FileInfo(file);
                    nativeTools.Add(new
                    {
                        RelativePath = relPath,
                        FileName = Path.GetFileName(file),
                        Size = fi.Length,
                        Extension = ext,
                        ToolSuite = relPath.Contains(@"\ADB\") ? "QualcommAdbEdl" : "SpFlashTool"
                    });
                }
            }
        }

        var fullManifest = new
        {
            GeneratedAt = DateTime.UtcNow.ToString("o"),
            TotalIndexedFiles = daList.Count + preloaderList.Count + scatterList.Count + tpList.Count + payloadList.Count + nativeTools.Count + resolvedLinks.Count,
            Summary = new
            {
                DownloadAgentsCount = daList.Count,
                PreloadersCount = preloaderList.Count,
                ScatterFilesCount = scatterList.Count,
                LinkFilesCount = resolvedLinks.Count,
                TestPointsCount = tpList.Count,
                BromAndFenrirPayloadsCount = payloadList.Count,
                NativeToolComponentsCount = nativeTools.Count
            },
            DownloadAgents = daList,
            LinkResolutions = resolvedLinks,
            Preloaders = preloaderList,
            Scatters = scatterList,
            TestPoints = tpList,
            Payloads = payloadList,
            NativeTools = nativeTools
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        string json = JsonSerializer.Serialize(fullManifest, options);
        File.WriteAllText(outputJson, json);
        Console.WriteLine($"Manifest written to {outputJson} ({json.Length} bytes).");
        Console.WriteLine($"Summary: DAs={daList.Count}, Preloaders={preloaderList.Count}, Links={resolvedLinks.Count}, Scatters={scatterList.Count}, TestPoints={tpList.Count}, Payloads={payloadList.Count}, Tools={nativeTools.Count}");
    }

    static string ResolveLinkTarget(string linkName, string daBaseDir)
    {
        // Example link names:
        // "0_base.base_v2112.link" -> "0_base\base_v2112"
        // "Alcatel.4034.link" -> "Alcatel\4034"
        // "Huawei.SEC_BASE.link" -> "Huawei\SEC_BASE"
        // "vivo.v2026.link" -> "vivo\v2026"
        // "0_rare.MT6570_repair.link" -> "0_rare\MT6570_repair"
        if (!linkName.EndsWith(".link", StringComparison.OrdinalIgnoreCase))
            return null;

        string stem = linkName.Substring(0, linkName.Length - 5);
        int dotIndex = stem.IndexOf('.');
        if (dotIndex > 0)
        {
            string part1 = stem.Substring(0, dotIndex);
            string part2 = stem.Substring(dotIndex + 1);

            string candidate1 = Path.Combine(daBaseDir, part1, part2);
            if (Directory.Exists(candidate1)) return candidate1;

            // Try case variations
            string[] matchingDirs = Directory.Exists(Path.Combine(daBaseDir, part1))
                ? Directory.GetDirectories(Path.Combine(daBaseDir, part1), part2, SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();
            if (matchingDirs.Length > 0) return matchingDirs[0];

            return candidate1;
        }

        return null;
    }

    static string DetectBrand(string path)
    {
        string pUpper = path.ToUpperInvariant();
        if (pUpper.Contains("SAMSUNG")) return "Samsung";
        if (pUpper.Contains("XIAOMI") || pUpper.Contains("REDMI") || pUpper.Contains("POCO")) return "Xiaomi";
        if (pUpper.Contains("OPPO") || pUpper.Contains("REALME")) return "OppoRealme";
        if (pUpper.Contains("HUAWEI") || pUpper.Contains("HONOR")) return "HuaweiHonor";
        if (pUpper.Contains("MOTO") || pUpper.Contains("MOTOROLA")) return "Motorola";
        if (pUpper.Contains("VIVO") || pUpper.Contains("BBK")) return "Vivo";
        if (pUpper.Contains("INFINIX") || pUpper.Contains("TECNO")) return "Transsion";
        if (pUpper.Contains("TCL") || pUpper.Contains("ALCATEL")) return "TclAlcatel";
        if (pUpper.Contains("NOKIA")) return "Nokia";
        if (pUpper.Contains("ONEPLUS")) return "OnePlus";
        if (pUpper.Contains("LG")) return "LG";
        if (pUpper.Contains("ASUS")) return "Asus";
        if (pUpper.Contains("ACER")) return "Acer";
        if (pUpper.Contains("MEIZU")) return "Meizu";
        if (pUpper.Contains("ZTE")) return "ZTE";
        if (pUpper.Contains("LENOVO")) return "Lenovo";
        return "Generic";
    }

    static string DetectSoc(string fileName, string path)
    {
        var match = Regex.Match(fileName + " " + path, @"(MT\d{4}[A-Z]?|SM\d{4}|T606|T610|T612|T616|UMS\d{4}|Helio\s*G\d+)", RegexOptions.IgnoreCase);
        if (match.Success)
            return match.Value.ToUpperInvariant().Replace(" ", "");
        return "Unknown";
    }

    static string DetectBit(string fileName)
    {
        var match = Regex.Match(fileName, @"(?:BIT[_\-\s]*([0-9A-Fa-f]+)|\.B([0-9A-Fa-f]+))", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Success ? match.Groups[1].Value.ToUpperInvariant() : match.Groups[2].Value.ToUpperInvariant();
        }
        return null;
    }
}

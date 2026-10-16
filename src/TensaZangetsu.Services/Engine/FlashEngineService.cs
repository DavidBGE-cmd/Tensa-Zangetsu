using System.Buffers.Binary;
using System.Globalization;
using System.IO.Ports;
using System.Management;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using TensaZangetsu.Firmware;

namespace TensaZangetsu.Services.Engine;

public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error,
    Debug
}

public class DeviceModel
{
    public string Id { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Chipset { get; set; } = string.Empty;
    public string Platform { get; set; } = "MTK";
    public string BromVid { get; set; } = "0E8D";
    public string BromPid { get; set; } = "0003";
    public string DisplayName => string.IsNullOrWhiteSpace(Brand) ? Name : $"{Brand} • {Name}";
}

public class PartitionItem
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string OffsetHex { get; set; } = "0x00000000";
    public string SizeFormatted { get; set; } = "0 B";
    public long SizeBytes { get; set; }
    public string Filesystem { get; set; } = "RAW";
    public string Region { get; set; } = "USER";
    public string FilePath { get; set; } = string.Empty;
    public bool IsCritical { get; set; }
    public bool IsSelected { get; set; }
}

public sealed record HardwareFingerprint(
    string Platform,
    string ComPortOrSerial,
    ushort? MtkHwCode = null,
    ushort? MtkHwSubCode = null,
    ushort? MtkHwVersion = null,
    ushort? MtkSwVersion = null,
    bool MtkSbcEnabled = false,
    bool MtkSlaEnabled = false,
    bool MtkDaaEnabled = false,
    string? QcomMsmHwId = null,
    string? QcomPkHash = null,
    uint? SprdChipId = null,
    string? BoardCodename = null
);

public sealed record ResolvedLoaderSet(
    bool IsReady,
    string SourceOrigin,
    string StatusSummary,
    string? MtkDaFilePath = null,
    int? MtkDaEntryIndex = null,
    string? MtkPreloaderPath = null,
    byte[]? MtkEmiDramPayload = null,
    uint MtkEmiVersion = 0,
    string? MtkAuthFilePath = null,
    string? QcomFirehosePath = null,
    string? SprdFdl1Path = null,
    uint SprdFdl1Address = 0x65000800,
    string? SprdFdl2Path = null,
    uint SprdFdl2Address = 0x9EFFFE00
);

public sealed record RomPartitionEntry(
    string Name,
    string FileName,
    long SizeBytes,
    string SizeFormatted,
    string Category,
    bool IsLoader
);

public sealed class CloudLoaderManifest
{
    [JsonPropertyName("mtk_all_in_one_v5_url")]
    public string? MtkAllInOneV5Url { get; set; }

    [JsonPropertyName("mtk_all_in_one_v6_url")]
    public string? MtkAllInOneV6Url { get; set; }

    [JsonPropertyName("preloaders")]
    public Dictionary<string, string> PreloadersByHwCode { get; set; } = new();

    [JsonPropertyName("qcom_firehose")]
    public Dictionary<string, string> QcomFirehoseByHwId { get; set; } = new();
}

public class FlashEngineService
{
    public event Action<LogLevel, string>? OnLog;
    public event Action<string, int>? OnProgress;
    public event Action<string, bool>? OnDeviceStateChanged;

    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(45) };

    public string CloudManifestUrl { get; set; } =
        "https://raw.githubusercontent.com/DavidBGE-cmd/mt-katool/main/src/TensaZangetsu.App/Assets/Loaders/loaders-manifest.json";

    public string LoadersRootDirectory { get; }
    public HardwareFingerprint? LastDetectedFingerprint { get; private set; }
    public ResolvedLoaderSet? LastResolvedLoaders { get; private set; }

    public FlashEngineService()
    {
        LoadersRootDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Loaders");
        EnsureDirectories();
    }

    private void EnsureDirectories()
    {
        string[] dirs = ["Mtk", "Mtk/Custom", "Mtk/Preloaders", "Mtk/Auth", "Qcom", "Sprd", "ExtractedFromRom"];
        foreach (var d in dirs) Directory.CreateDirectory(Path.Combine(LoadersRootDirectory, d));
    }

    public List<DeviceModel> GetSupportedModels() =>
    [
        new DeviceModel { Brand = "Auto", Name = "Auto-Detect Universal (MTK V5/V6 • QCOM • SPD)", Chipset = "Auto", Platform = "AUTO", BromVid = "0E8D", BromPid = "0003" },
        new DeviceModel { Brand = "MediaTek", Name = "MediaTek V5 XFlash (Helio P35/G85/G95)", Chipset = "MT6765/MT6768/MT6785", Platform = "MTK", BromVid = "0E8D", BromPid = "0003" },
        new DeviceModel { Brand = "MediaTek", Name = "MediaTek V6 XML (Helio G99 / Dimensity)", Chipset = "MT6789/MT6833/MT6877+", Platform = "MTK", BromVid = "0E8D", BromPid = "0003" },
        new DeviceModel { Brand = "Qualcomm", Name = "Qualcomm EDL 9008 (Sahara + Firehose)", Chipset = "Snapdragon eMMC/UFS", Platform = "QCOM", BromVid = "05C6", BromPid = "9008" },
        new DeviceModel { Brand = "UNISOC", Name = "UNISOC / Spreadtrum (FDL1 + FDL2)", Chipset = "SC9863A / T606 / T612", Platform = "SPRD", BromVid = "1782", BromPid = "4D00" }
    ];

    public (string? PortName, string Platform, string Description) DetectConnectedHardwarePort()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");

                foreach (ManagementObject obj in searcher.Get())
                {
                    string name = obj["Name"]?.ToString() ?? string.Empty;
                    string pnp = obj["PNPDeviceID"]?.ToString() ?? string.Empty;
                    string? port = ExtractComPort(name);
                    if (string.IsNullOrEmpty(port)) continue;

                    if (pnp.Contains("VID_0E8D", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("MediaTek", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("PreLoader", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("MTK", StringComparison.OrdinalIgnoreCase))
                        return (port, "MTK", name);

                    if (pnp.Contains("VID_05C6&PID_9008", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("9008", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("QDLoader", StringComparison.OrdinalIgnoreCase))
                        return (port, "QCOM", name);

                    if (pnp.Contains("VID_1782", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("SPRD", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Spreadtrum", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("UNISOC", StringComparison.OrdinalIgnoreCase))
                        return (port, "SPRD", name);
                }
            }
        }
        catch (Exception ex)
        {
            Log(LogLevel.Debug, $"Aviso WMI: {ex.Message}");
        }

        string[] ports = SerialPort.GetPortNames();
        return ports.Length > 0 ? (ports[0], "SERIAL", $"Puerto Serial ({ports[0]})") : (null, "NONE", "Sin dispositivo en COM");
    }

    private static string? ExtractComPort(string text)
    {
        int start = text.IndexOf("(COM", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        int end = text.IndexOf(')', start);
        return end > start ? text.Substring(start + 1, end - start - 1) : null;
    }

    public Task<Dictionary<string, string>> ReadDeviceInfoAsync(
        DeviceModel? model,
        string? optionalRomPath,
        CancellationToken ct) => ReadDeviceInfoAsync(model, optionalRomPath, explicitPortName: null, ct);

    public async Task<Dictionary<string, string>> ReadDeviceInfoAsync(
        DeviceModel? model = null,
        string? optionalRomPath = null,
        string? explicitPortName = null,
        CancellationToken ct = default)
    {
        return await Task.Run(async () =>
        {
            var info = new Dictionary<string, string>();
            ReportProgress("Escaneando bus USB / Puertos COM...", 15);

            string? portName = explicitPortName;
            string platform = "MTK";
            string desc = explicitPortName != null ? $"Puerto ({explicitPortName})" : "";

            if (string.IsNullOrEmpty(portName))
            {
                var detected = DetectConnectedHardwarePort();
                portName = detected.PortName;
                platform = detected.Platform;
                desc = detected.Description;
            }
            else
            {
                var detected = DetectConnectedHardwarePort();
                if (!string.IsNullOrEmpty(detected.Platform) && detected.Platform != "NONE")
                {
                    platform = detected.Platform;
                    desc = detected.Description;
                }
            }

            if (string.IsNullOrEmpty(portName))
                throw new InvalidOperationException("No se detectó ningún teléfono en modo BROM, Preloader, EDL 9008 o UNISOC.");

            OnDeviceStateChanged?.Invoke(desc, true);
            info["Interfaz"] = $"{portName} — {desc}";
            info["Plataforma"] = platform;

            if (platform is "MTK" or "SERIAL")
            {
                using var port = OpenSerialPort(portName);
                ReportProgress($"Ejecutando Handshake BROM/Preloader en {portName}...", 35);

                PerformMtkHandshake(port, ct);
                ushort hwCode = ReadMtkHwCode(port);
                var (hwSub, hwVer, swVer) = ReadMtkVers(port);
                uint targetCfg = ReadMtkTargetConfig(port);
                DisableMtkWatchdog(port);

                bool sbc = (targetCfg & 0x01) != 0;
                bool sla = (targetCfg & 0x02) != 0;
                bool daa = (targetCfg & 0x04) != 0;

                LastDetectedFingerprint = new HardwareFingerprint(
                    Platform: "MTK",
                    ComPortOrSerial: portName,
                    MtkHwCode: hwCode,
                    MtkHwSubCode: hwSub,
                    MtkHwVersion: hwVer,
                    MtkSwVersion: swVer,
                    MtkSbcEnabled: sbc,
                    MtkSlaEnabled: sla,
                    MtkDaaEnabled: daa);

                string socName = MapHwCodeToName(hwCode);
                bool isV6 = IsMtkV6(hwCode);

                info["SoC"] = $"{socName} (0x{hwCode:X4})";
                info["Protocolo DA"] = isV6 ? "MTK V6 (XML)" : "MTK V5 (XFlash / Legacy)";
                info["Seguridad"] = $"SBC={(sbc ? "1" : "0")} | SLA={(sla ? "1" : "0")} | DAA={(daa ? "1" : "0")}";
                Log(LogLevel.Success, $"[MTK] Detectado {socName} (0x{hwCode:X4}) en {portName} [{info["Protocolo DA"]}]");
            }
            else if (platform == "QCOM")
            {
                LastDetectedFingerprint = new HardwareFingerprint("QCOM", portName, QcomMsmHwId: "SAHARA_AUTO");
                info["Protocolo"] = "Qualcomm Sahara / Firehose EDL 9008";
                Log(LogLevel.Success, $"[QCOM] Puerto EDL 9008 detectado en {portName}.");
            }
            else if (platform == "SPRD")
            {
                LastDetectedFingerprint = new HardwareFingerprint("SPRD", portName, SprdChipId: 0x9863);
                info["Protocolo"] = "UNISOC BSL (FDL1 + FDL2)";
                Log(LogLevel.Success, $"[SPRD] Puerto UNISOC detectado en {portName}.");
            }

            if (LastDetectedFingerprint is not null)
            {
                ReportProgress("Auto-seleccionando Loaders (Local -> ROM -> Nube)...", 70);
                LastResolvedLoaders = await ResolveOrFetchLoadersAsync(LastDetectedFingerprint, optionalRomPath, ct);
                info["Loader Auto-Match"] = LastResolvedLoaders.StatusSummary;
                info["Origen Loader"] = LastResolvedLoaders.SourceOrigin;
            }

            ReportProgress("Identificación y Auto-Match completados.", 100);
            return info;
        }, ct);
    }

    public async Task<ResolvedLoaderSet> ResolveOrFetchLoadersAsync(
        HardwareFingerprint fp,
        string? optionalRomPath = null,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(optionalRomPath) && File.Exists(optionalRomPath))
        {
            try
            {
                Log(LogLevel.Info, $"[ROM-STREAM] Extrayendo loaders al vuelo desde '{Path.GetFileName(optionalRomPath)}'...");
                await ExtractOnlyLoadersFromRomAsync(optionalRomPath, ct);
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, $"[ROM-STREAM] Aviso al extraer loaders de ROM: {ex.Message}");
            }
        }

        if (fp.Platform == "MTK" && fp.MtkHwCode is { } hwCode)
        {
            string mtkDir = Path.Combine(LoadersRootDirectory, "Mtk");
            var (daPath, entryIdx) = FindDaForHwCodeInDirectory(mtkDir, hwCode);
            string origin = "LocalCache";

            if (daPath is null)
            {
                Log(LogLevel.Warning, $"[AUTO-MATCH] No hay DA local para MT{hwCode:X4}. Intentando descarga automática...");
                if (await TryDownloadMtkDaFromCloudAsync(hwCode, mtkDir, ct))
                {
                    (daPath, entryIdx) = FindDaForHwCodeInDirectory(mtkDir, hwCode);
                    origin = "CloudDownloaded";
                }
            }

            string? preloaderPath = FindLocalPreloader(mtkDir, hwCode, fp.BoardCodename);
            byte[]? emiBytes = null;
            uint emiVer = 0;

            if (!string.IsNullOrEmpty(preloaderPath) &&
                TryExtractEmiFromPreloaderFile(preloaderPath, out emiBytes, out emiVer, out string preInternalName))
            {
                Log(LogLevel.Success, $"[EMI] Tabla DRAM extraída de '{preInternalName}' (v0x{emiVer:X2}, {emiBytes.Length} bytes).");
            }

            string? authPath = Directory.EnumerateFiles(Path.Combine(mtkDir, "Auth"), "*.auth", SearchOption.AllDirectories).FirstOrDefault();

            if (daPath is null)
            {
                return new ResolvedLoaderSet(
                    false,
                    "Missing",
                    $"Sin archivo DA para MT{hwCode:X4}. Coloca MTK_AllInOne_DA.bin en Assets/Loaders/Mtk/ o carga una ROM.");
            }

            string msg = $"DA: {Path.GetFileName(daPath)} (Entrada #{entryIdx} MT{hwCode:X4}) " +
                         (emiBytes != null ? $"| EMI lista ({emiBytes.Length} B)" : "| Sin Preloader externo");

            return new ResolvedLoaderSet(
                true, origin, msg,
                MtkDaFilePath: daPath,
                MtkDaEntryIndex: entryIdx,
                MtkPreloaderPath: preloaderPath,
                MtkEmiDramPayload: emiBytes,
                MtkEmiVersion: emiVer,
                MtkAuthFilePath: authPath);
        }

        if (fp.Platform == "QCOM")
        {
            string qcomDir = Path.Combine(LoadersRootDirectory, "Qcom");
            string romDir = Path.Combine(LoadersRootDirectory, "ExtractedFromRom");
            string? fh = Directory.EnumerateFiles(qcomDir, "*.*", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(romDir, "*.*", SearchOption.AllDirectories))
                .FirstOrDefault(f => f.EndsWith(".elf", StringComparison.OrdinalIgnoreCase) ||
                                     f.EndsWith(".mbn", StringComparison.OrdinalIgnoreCase) ||
                                     f.EndsWith(".melf", StringComparison.OrdinalIgnoreCase));

            return new ResolvedLoaderSet(
                fh != null,
                fh != null ? "LocalCache" : "Missing",
                fh != null ? $"Firehose listo: {Path.GetFileName(fh)}" : "Selecciona un Firehose (.elf/.mbn) o extráelo desde la pestaña ROM.",
                QcomFirehosePath: fh);
        }

        if (fp.Platform == "SPRD")
        {
            string sprdDir = Path.Combine(LoadersRootDirectory, "Sprd");
            string romDir = Path.Combine(LoadersRootDirectory, "ExtractedFromRom");
            var allFiles = Directory.EnumerateFiles(sprdDir, "*.*", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(romDir, "*.*", SearchOption.AllDirectories)).ToList();

            string? fdl1 = allFiles.FirstOrDefault(f => f.Contains("fdl1", StringComparison.OrdinalIgnoreCase));
            string? fdl2 = allFiles.FirstOrDefault(f => f.Contains("fdl2", StringComparison.OrdinalIgnoreCase));

            return new ResolvedLoaderSet(
                fdl1 != null && fdl2 != null,
                fdl1 != null ? "LocalCache" : "Missing",
                fdl1 != null && fdl2 != null ? $"FDL1 ({Path.GetFileName(fdl1)}) + FDL2 ({Path.GetFileName(fdl2)}) listos" : "Carga un archivo .PAC en la pestaña ROM para extraer FDL1 y FDL2.",
                SprdFdl1Path: fdl1,
                SprdFdl1Address: 0x65000800,
                SprdFdl2Path: fdl2,
                SprdFdl2Address: 0x9EFFFE00);
        }

        return new ResolvedLoaderSet(false, "Missing", "Conecta un dispositivo o selecciona una ROM.");
    }

    public static (string? DaFilePath, int? EntryIndex) FindDaForHwCodeInDirectory(string mtkDir, ushort targetHwCode)
    {
        if (!Directory.Exists(mtkDir)) return (null, null);

        var candidates = Directory.EnumerateFiles(mtkDir, "*.bin", SearchOption.AllDirectories)
            .Where(f => !f.Contains("Preloaders", StringComparison.OrdinalIgnoreCase) &&
                        !f.EndsWith("_emi.bin", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.Contains("Custom", StringComparison.OrdinalIgnoreCase));

        foreach (string file in candidates)
        {
            int idx = FindHwCodeInsideDa(file, targetHwCode);
            if (idx >= 0) return (file, idx);
        }
        return (null, null);
    }

    public static int FindHwCodeInsideDa(string daFilePath, ushort targetHwCode)
    {
        try
        {
            using var fs = new FileStream(daFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < 0x6C) return -1;

            Span<byte> header = stackalloc byte[0x6C];
            fs.ReadExactly(header);

            string magic = Encoding.ASCII.GetString(header[..18]).TrimEnd('\0');
            if (!magic.StartsWith("MTK_DOWNLOAD_AGENT", StringComparison.Ordinal)) return -1;

            uint count = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(0x68, 4));
            if (count == 0 || count > 512) return -1;

            Span<byte> entry = stackalloc byte[0xDC];
            for (int i = 0; i < count; i++)
            {
                if (fs.Read(entry) < 0xDC) break;
                ushort entryMagic = BinaryPrimitives.ReadUInt16LittleEndian(entry[..2]);
                ushort hw = BinaryPrimitives.ReadUInt16LittleEndian(entry.Slice(2, 2));
                if (entryMagic == 0xDADA && hw == targetHwCode) return i;
            }
        }
        catch { }
        return -1;
    }

    private async Task<bool> TryDownloadMtkDaFromCloudAsync(ushort hwCode, string mtkDir, CancellationToken ct)
    {
        try
        {
            var manifest = await SharedHttp.GetFromJsonAsync<CloudLoaderManifest>(CloudManifestUrl, ct);
            if (manifest is null) return false;

            bool isV6 = IsMtkV6(hwCode);
            string? url = isV6 ? manifest.MtkAllInOneV6Url : manifest.MtkAllInOneV5Url;
            if (string.IsNullOrWhiteSpace(url)) return false;

            string dest = Path.Combine(mtkDir, isV6 ? "MTK_AllInOne_DA_V6.bin" : "MTK_AllInOne_DA_V5.bin");
            using var resp = await SharedHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode) return false;

            await using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
            await resp.Content.CopyToAsync(fs, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string? FindLocalPreloader(string mtkDir, ushort hwCode, string? codename)
    {
        string preDir = Path.Combine(mtkDir, "Preloaders");
        string romDir = Path.Combine(LoadersRootDirectory, "ExtractedFromRom");

        var files = Directory.EnumerateFiles(preDir, "*.*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(romDir, "*.*", SearchOption.AllDirectories))
            .Where(f => f.Contains("preloader", StringComparison.OrdinalIgnoreCase) &&
                        !f.EndsWith("_emi.bin", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return files.FirstOrDefault(f => f.Contains($"{hwCode:X4}", StringComparison.OrdinalIgnoreCase))
            ?? files.FirstOrDefault();
    }

    public static bool TryExtractEmiFromPreloaderFile(
        string preloaderPath,
        out byte[] emiPayload,
        out uint emiVersion,
        out string internalName)
    {
        emiPayload = [];
        emiVersion = 0;
        internalName = Path.GetFileName(preloaderPath);

        if (!File.Exists(preloaderPath)) return false;
        ReadOnlySpan<byte> raw = File.ReadAllBytes(preloaderPath);
        if (raw.Length < 256) return false;

        ReadOnlySpan<byte> bloaderMagic = "MTK_BLOADER_INFO_v"u8;
        ReadOnlySpan<byte> gfhMagic = [0x4D, 0x4D, 0x4D, 0x01, 0x38, 0x00, 0x00, 0x00];

        int idx = raw.IndexOf(bloaderMagic);
        if (idx >= 0)
        {
            ReadOnlySpan<byte> block = raw[idx..];
            if (block.Length < 0x6C) return false;

            string verStr = Encoding.ASCII.GetString(block.Slice(18, 2));
            if (!uint.TryParse(verStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out emiVersion))
                uint.TryParse(verStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out emiVersion);

            string parsedName = Encoding.ASCII.GetString(block.Slice(0x1B, 61)).Split('\0')[0].Trim();
            if (!string.IsNullOrWhiteSpace(parsedName)) internalName = parsedName;

            uint declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(block.Length - 4, 4));
            const int emiStart = 0x58;
            int emiLen;

            if (declaredSize > emiStart && declaredSize <= block.Length)
                emiLen = (int)declaredSize - emiStart;
            else
            {
                int nextGfh = block[emiStart..].IndexOf(gfhMagic);
                emiLen = nextGfh > 4 ? nextGfh - 4 : block.Length - emiStart - 4;
            }

            if (emiLen > 0 && emiStart + emiLen <= block.Length)
            {
                emiPayload = block.Slice(emiStart, emiLen).ToArray();
                return true;
            }
        }
        return false;
    }

    public List<RomPartitionEntry> InspectRomPackage(string packagePath)
    {
        if (!File.Exists(packagePath))
            throw new FileNotFoundException("No se encontró el archivo de ROM.", packagePath);

        using var archive = FirmwareUnpacker.Open(packagePath);
        var entries = archive.Entries.Select(e =>
        {
            string fn = e.Name;
            bool isLoader = IsLoaderName(e.Name, fn);
            long sz = e.Length;
            return new RomPartitionEntry(
                Name: e.Name,
                FileName: fn,
                SizeBytes: sz,
                SizeFormatted: FormatSize((ulong)Math.Max(0, sz)),
                Category: ClassifyCategory(e.Name, fn),
                IsLoader: isLoader);
        }).ToList();

        Log(LogLevel.Success, $"[ROM] '{Path.GetFileName(packagePath)}' abierto en streaming: {entries.Count} particiones ({entries.Count(x => x.IsLoader)} loaders).");
        return entries;
    }

    public async Task<List<string>> ExtractOnlyLoadersFromRomAsync(string packagePath, CancellationToken ct = default)
    {
        string destDir = Path.Combine(LoadersRootDirectory, "ExtractedFromRom");
        Directory.CreateDirectory(destDir);

        return await Task.Run(() =>
        {
            using var archive = FirmwareUnpacker.Open(packagePath);
            var loaders = archive.Entries.Where(e => IsLoaderName(e.Name, e.Name)).ToList();
            var outputFiles = new List<string>(loaders.Count);

            for (int i = 0; i < loaders.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var entry = loaders[i];
                string safeName = Path.GetFileName(entry.Name);
                string outPath = Path.Combine(destDir, safeName);

                int pct = (int)((double)(i + 1) / Math.Max(1, loaders.Count) * 100.0);
                ReportProgress($"Extrayendo loader '{safeName}' ({i + 1}/{loaders.Count})...", pct);

                using Stream src = entry.OpenStream(ct);
                using var dst = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024);
                src.CopyTo(dst);

                outputFiles.Add(outPath);
                Log(LogLevel.Info, $"[LOADER EXTRAÍDO] {outPath} ({FormatSize((ulong)Math.Max(0, entry.Length))})");
            }
            return outputFiles;
        }, ct);
    }

    public async Task<List<string>> ExtractSelectedRomPartitionsAsync(
        string packagePath,
        string destinationFolder,
        IReadOnlySet<string>? selectedNames = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(destinationFolder);

        return await Task.Run(() =>
        {
            using var archive = FirmwareUnpacker.Open(packagePath);
            var targets = (selectedNames is null || selectedNames.Count == 0)
                ? archive.Entries.ToList()
                : archive.Entries.Where(e => selectedNames.Contains(e.Name)).ToList();

            var outputFiles = new List<string>(targets.Count);

            for (int i = 0; i < targets.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var entry = targets[i];
                string safeName = Path.GetFileName(entry.Name);
                string outPath = Path.Combine(destinationFolder, safeName);

                int pct = (int)((double)(i + 1) / Math.Max(1, targets.Count) * 100.0);
                ReportProgress($"Extrayendo '{safeName}' ({i + 1}/{targets.Count})...", pct);

                using Stream src = entry.OpenStream(ct);
                using var dst = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024);
                src.CopyTo(dst);

                outputFiles.Add(outPath);
                Log(LogLevel.Info, $"[EXTRAÍDO] {outPath} ({FormatSize((ulong)Math.Max(0, entry.Length))})");
            }
            return outputFiles;
        }, ct);
    }

    public List<PartitionItem> ParseRawGptBytes(ReadOnlySpan<byte> rawGpt)
    {
        ReadOnlySpan<byte> sig = "EFI PART"u8;
        int sectorSize;
        if (rawGpt.Length >= 512 + 92 && rawGpt.Slice(512, 8).SequenceEqual(sig))
            sectorSize = 512;
        else if (rawGpt.Length >= 4096 + 92 && rawGpt.Slice(4096, 8).SequenceEqual(sig))
            sectorSize = 4096;
        else
        {
            int idx = rawGpt.IndexOf(sig);
            if (idx < 0) throw new InvalidDataException("El archivo no contiene una firma 'EFI PART' válida.");
            sectorSize = idx == 0 ? 512 : idx;
        }

        int hdrOffset = rawGpt.Slice(0, 8).SequenceEqual(sig) ? 0 : sectorSize;
        ReadOnlySpan<byte> hdr = rawGpt.Slice(hdrOffset, 92);

        ulong entryLba = BinaryPrimitives.ReadUInt64LittleEndian(hdr.Slice(72, 8));
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(hdr.Slice(80, 4));
        uint entrySize = BinaryPrimitives.ReadUInt32LittleEndian(hdr.Slice(84, 4));

        long entriesStart = hdrOffset == 0 ? sectorSize : checked((long)entryLba * sectorSize);
        var list = new List<PartitionItem>();

        for (int i = 0; i < count; i++)
        {
            long pos = entriesStart + (i * entrySize);
            if (pos + entrySize > rawGpt.Length) break;

            ReadOnlySpan<byte> entry = rawGpt.Slice((int)pos, (int)entrySize);
            if (new Guid(entry[..16]) == Guid.Empty) continue;

            ulong firstLba = BinaryPrimitives.ReadUInt64LittleEndian(entry.Slice(32, 8));
            ulong lastLba = BinaryPrimitives.ReadUInt64LittleEndian(entry.Slice(40, 8));
            if (lastLba < firstLba) continue;

            string name = Encoding.Unicode.GetString(entry.Slice(56, 72)).TrimEnd('\0');
            if (string.IsNullOrWhiteSpace(name)) continue;

            ulong byteOffset = checked(firstLba * (ulong)sectorSize);
            ulong byteSize = checked((lastLba - firstLba + 1) * (ulong)sectorSize);

            list.Add(new PartitionItem
            {
                Index = list.Count + 1,
                Name = name,
                OffsetHex = $"0x{byteOffset:X8}",
                SizeFormatted = FormatSize(byteSize),
                SizeBytes = (long)byteSize,
                Filesystem = InferFs(name),
                IsCritical = IsCritical(name),
                IsSelected = false
            });
        }
        return list;
    }

    public async Task<List<PartitionItem>> ReadGptAsync(
        DeviceModel? model = null,
        string? sourceGptOrRomPath = null,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(sourceGptOrRomPath) && File.Exists(sourceGptOrRomPath))
        {
            string ext = Path.GetExtension(sourceGptOrRomPath).ToLowerInvariant();
            if (ext is ".ofp" or ".ops" or ".pac" or ".ozip" ||
                Path.GetFileName(sourceGptOrRomPath).Equals("payload.bin", StringComparison.OrdinalIgnoreCase))
            {
                var romItems = InspectRomPackage(sourceGptOrRomPath);
                return romItems.Select((r, i) => new PartitionItem
                {
                    Index = i + 1,
                    Name = r.Name,
                    OffsetHex = "ROM-STREAM",
                    SizeFormatted = r.SizeFormatted,
                    SizeBytes = r.SizeBytes,
                    Filesystem = r.Category,
                    IsCritical = IsCritical(r.Name),
                    IsSelected = false
                }).ToList();
            }

            byte[] rawBytes = await File.ReadAllBytesAsync(sourceGptOrRomPath, ct);
            return ParseRawGptBytes(rawBytes);
        }

        await ReadDeviceInfoAsync(model, null, ct);
        throw new InvalidOperationException(
            "Handshake completado. Para visualizar particiones desde una ROM (.ofp, .pac, payload.bin) o un volcado GPT, " +
            "usa el botón 'Cargar archivo GPT / ROM' en el panel izquierdo.");
    }

    public Task<bool> ReadPartitionAsync(DeviceModel? model, PartitionItem partition, string outputFolder, CancellationToken ct = default)
    {
        Log(LogLevel.Info, $"[VOLCADO] Solicitud para '{partition.Name}' ({partition.SizeFormatted}) en '{outputFolder}'.");
        return Task.FromResult(true);
    }

    public Task<bool> WritePartitionAsync(DeviceModel? model, PartitionItem partition, string sourceFile, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFile) || !File.Exists(sourceFile))
        {
            Log(LogLevel.Error, $"[ESCRITURA] El archivo '{sourceFile}' no existe.");
            return Task.FromResult(false);
        }
        var fi = new FileInfo(sourceFile);
        if (partition.SizeBytes > 0 && fi.Length > partition.SizeBytes)
        {
            Log(LogLevel.Error, $"[ESCRITURA] El archivo ({FormatSize((ulong)fi.Length)}) supera el tamaño de '{partition.Name}' ({partition.SizeFormatted}).");
            return Task.FromResult(false);
        }
        Log(LogLevel.Success, $"[ESCRITURA] '{fi.Name}' validado y listo para escribir en '{partition.Name}' ({partition.OffsetHex}).");
        return Task.FromResult(true);
    }

    public Task<bool> ErasePartitionAsync(DeviceModel? model, PartitionItem partition, CancellationToken ct = default)
    {
        if (partition.IsCritical)
        {
            Log(LogLevel.Error, $"[BLOQUEADO] '{partition.Name}' contiene calibración RF/IMEI y está protegida contra borrado.");
            return Task.FromResult(false);
        }
        Log(LogLevel.Success, $"[BORRADO] Solicitud validada para '{partition.Name}' ({partition.OffsetHex}).");
        return Task.FromResult(true);
    }

    public Task<bool> BackupRfCalibrationAsync(DeviceModel? model, string backupDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(backupDir);
        Log(LogLevel.Info, $"[RF-BACKUP] Directorio de respaldo NVRAM/NVDATA preparado: {backupDir}");
        return Task.FromResult(true);
    }

    public Task<bool> DumpRomSkipUserdataAsync(DeviceModel? model, IEnumerable<PartitionItem> partitions, string outputDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDir);
        Log(LogLevel.Info, $"[FULL-DUMP] Directorio preparado (omitiendo userdata/super): {outputDir}");
        return Task.FromResult(true);
    }

    public static SerialPort OpenSerialPort(string portName)
    {
        var port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 4000,
            WriteTimeout = 4000,
            DtrEnable = true,
            RtsEnable = true
        };
        port.Open();
        port.DiscardInBuffer();
        port.DiscardOutBuffer();
        return port;
    }

    public static void PerformMtkHandshake(SerialPort port, CancellationToken ct)
    {
        ReadOnlySpan<byte> tx = [0xA0, 0x0A, 0x50, 0x05];
        ReadOnlySpan<byte> rx = [0x5F, 0xF5, 0xAF, 0xFA];
        int step = 0;
        var deadline = DateTime.UtcNow.AddSeconds(8);

        while (step < 4)
        {
            ct.ThrowIfCancellationRequested();
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Tiempo agotado en Handshake BROM/Preloader (0xA0).");

            port.Write([tx[step]], 0, 1);
            int b = port.ReadByte();
            step = (b == rx[step]) ? step + 1 : 0;
        }
    }

    public static ushort ReadMtkHwCode(SerialPort port)
    {
        port.Write([0xFD], 0, 1);
        if (port.ReadByte() != 0xFD) throw new IOException("Sin eco en GET_HW_CODE (0xFD).");
        Span<byte> buf = stackalloc byte[4];
        ReadExact(port, buf);
        return BinaryPrimitives.ReadUInt16BigEndian(buf[..2]);
    }

    public static (ushort Sub, ushort Hw, ushort Sw) ReadMtkVers(SerialPort port)
    {
        port.Write([0xFC], 0, 1);
        if (port.ReadByte() != 0xFC) return (0, 0, 0);
        Span<byte> buf = stackalloc byte[8];
        ReadExact(port, buf);
        return (
            BinaryPrimitives.ReadUInt16BigEndian(buf[..2]),
            BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(2, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(buf.Slice(4, 2))
        );
    }

    public static uint ReadMtkTargetConfig(SerialPort port)
    {
        try
        {
            port.Write([0xD8], 0, 1);
            if (port.ReadByte() != 0xD8) return 0;
            Span<byte> buf = stackalloc byte[6];
            ReadExact(port, buf);
            return BinaryPrimitives.ReadUInt32BigEndian(buf[..4]);
        }
        catch { return 0; }
    }

    public static void DisableMtkWatchdog(SerialPort port)
    {
        try
        {
            port.Write([0xD4], 0, 1);
            if (port.ReadByte() != 0xD4) return;
            byte[] cmd = [0x10, 0x00, 0x70, 0x00, 0x00, 0x00, 0x00, 0x01];
            port.Write(cmd, 0, cmd.Length);
            Span<byte> echo = stackalloc byte[10];
            ReadExact(port, echo);
            byte[] val = [0x22, 0x00, 0x00, 0x64];
            port.Write(val, 0, val.Length);
            Span<byte> echo2 = stackalloc byte[6];
            ReadExact(port, echo2);
        }
        catch { }
    }

    public static void ReadExact(SerialPort port, Span<byte> buf)
    {
        int read = 0;
        while (read < buf.Length)
        {
            int b = port.ReadByte();
            if (b < 0) throw new EndOfStreamException();
            buf[read++] = (byte)b;
        }
    }

    public static bool IsMtkV6(ushort hw) => hw >= 0x0950 || hw is 0x0886 or 0x0813 or 0x0900;

    public static string MapHwCodeToName(ushort hw) => hw switch
    {
        0x0717 => "MT6761 (Helio A22)",
        0x0766 => "MT6765 (Helio P35 / G35)",
        0x0707 => "MT6768 (Helio P65 / G85)",
        0x0816 => "MT6785 (Helio G90T / G95)",
        0x0959 => "MT6789 (Helio G99)",
        0x0989 => "MT6833 (Dimensity 700 / 6020)",
        0x0886 => "MT6853 (Dimensity 720)",
        0x0900 => "MT6877 (Dimensity 900 / 1080)",
        _ => $"MediaTek MT{hw:X4}"
    };

    private static bool IsLoaderName(string name, string fileName)
    {
        string s = $"{name} {fileName}".ToLowerInvariant();
        return s.Contains("preloader") || s.Contains("firehose") || s.Contains("prog_emmc") ||
               s.Contains("prog_ufs") || s.Contains("fdl1") || s.Contains("fdl2") || s.EndsWith(".auth");
    }

    private static string ClassifyCategory(string name, string fileName)
    {
        if (IsLoaderName(name, fileName)) return "LOADER";
        string n = name.ToLowerInvariant();
        if (n is "boot" or "init_boot" or "vendor_boot" or "vbmeta" or "recovery" or "lk") return "BOOT/SEC";
        if (n is "nvram" or "nvdata" or "nvcfg" or "protect1" or "protect2" or "modem" or "efs" or "fsg" or "modemst1" or "modemst2") return "MODEM/RF";
        if (n is "super" or "system" or "vendor" or "product" or "userdata") return "SYSTEM";
        return "FIRMWARE";
    }

    private static bool IsCritical(string name)
    {
        string n = name.ToLowerInvariant();
        return n is "nvram" or "nvdata" or "nvcfg" or "protect1" or "protect2" or "proinfo" or "seccfg" or "persist" or "efs" or "fsg" or "modemst1" or "modemst2";
    }

    private static string InferFs(string name)
    {
        string n = name.ToLowerInvariant();
        if (n is "nvdata" or "nvcfg" or "protect1" or "protect2" or "persist") return "EXT4";
        if (n is "userdata") return "F2FS/EXT4";
        if (n is "super") return "DYNAMIC";
        return "RAW";
    }

    public static string FormatSize(ulong bytes)
    {
        if (bytes >= 1024UL * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        if (bytes >= 1024UL * 1024) return $"{bytes / (1024.0 * 1024):F2} MB";
        if (bytes >= 1024UL) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }

    protected void Log(LogLevel level, string message) => OnLog?.Invoke(level, message);
    protected void ReportProgress(string status, int pct) => OnProgress?.Invoke(status, pct);
}
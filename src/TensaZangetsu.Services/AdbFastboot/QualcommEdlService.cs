using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TensaZangetsu.Services.AdbFastboot;

public sealed record Sm6125ExtractionResult(
    string BootstrapBinaryPath,
    string ManifestJsonPath,
    string Sha256,
    int TotalBytes,
    int TriggerWrites
);

/// <summary>
/// Servicio de automatización para Qualcomm Snapdragon en modos Fastboot, EDL 9008,
/// Sahara, Firehose, herramientas externas (emmcdl, fh_loader, fastboot_edl) y extracción de ROMs primarias.
/// </summary>
public sealed class QualcommEdlService
{
    private readonly string _adbDirectory;

    public QualcommEdlService(string? customAdbDir = null)
    {
        _adbDirectory = customAdbDir ?? ResolveAdbDirectory();
    }

    public string AdbDirectory => _adbDirectory;

    private static string ResolveAdbDirectory()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidates = [
            Path.Combine(baseDir, "ExternalAssets", "ADB"),
            Path.Combine(baseDir, "Assets", "ADB"),
            Path.Combine(baseDir, "mt-katool-FutureAssets", "ADB"),
            Path.Combine(baseDir, "..", "..", "..", "mt-katool-FutureAssets", "ADB"),
            Path.Combine(baseDir, "..", "..", "..", "..", "mt-katool-FutureAssets", "ADB")
        ];

        foreach (var c in candidates)
        {
            try
            {
                string full = Path.GetFullPath(c);
                if (Directory.Exists(full) && File.Exists(Path.Combine(full, "adb.exe")))
                    return full;
            }
            catch { }
        }

        var current = new DirectoryInfo(baseDir);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, "mt-katool-FutureAssets", "ADB");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "adb.exe")))
                return candidate;

            string candidate2 = Path.Combine(current.FullName, "ExternalAssets", "ADB");
            if (Directory.Exists(candidate2) && File.Exists(Path.Combine(candidate2, "adb.exe")))
                return candidate2;

            current = current.Parent;
        }

        return Path.Combine(baseDir, "Assets", "ADB");
    }

    public string? GetToolPath(string toolName)
    {
        string exe = Path.Combine(_adbDirectory, toolName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? toolName : toolName + ".exe");
        return File.Exists(exe) ? exe : null;
    }

    /// <summary>
    /// Fuerza el reinicio a modo EDL (Qualcomm HS-USB QDLoader 9008) desde Fastboot o FastbootD.
    /// Ejecuta la secuencia en cascada: fastboot oem edl -> fastboot reboot-edl -> fastboot_edl.exe.
    /// </summary>
    public async Task<bool> RebootToEdlFromFastbootAsync(Action<string>? onLog = null, CancellationToken ct = default)
    {
        string? fastbootExe = GetToolPath("fastboot");
        if (fastbootExe != null)
        {
            onLog?.Invoke("▶ [Fastboot EDL] Intentando: fastboot oem edl...");
            int code1 = await RunProcessAsync(fastbootExe, "oem edl", onLog, ct);
            if (code1 == 0) return true;

            onLog?.Invoke("▶ [Fastboot EDL] Intentando: fastboot reboot-edl...");
            int code2 = await RunProcessAsync(fastbootExe, "reboot-edl", onLog, ct);
            if (code2 == 0) return true;
        }

        string? fastbootEdlExe = GetToolPath("fastboot_edl");
        if (fastbootEdlExe != null)
        {
            onLog?.Invoke("▶ [Fastboot EDL] Intentando: fastboot_edl.exe reboot-edl...");
            int code3 = await RunProcessAsync(fastbootEdlExe, "reboot-edl", onLog, ct);
            if (code3 == 0) return true;
        }

        onLog?.Invoke("⚠️ [Fastboot EDL] Los comandos directos finalizaron. Verifica si el dispositivo entró en puerto COM 9008.");
        return false;
    }

    /// <summary>
    /// Reinicia un dispositivo a modo EDL desde ADB estándar.
    /// </summary>
    public async Task<bool> RebootToEdlFromAdbAsync(Action<string>? onLog = null, CancellationToken ct = default)
    {
        string? adbExe = GetToolPath("adb");
        if (adbExe == null)
        {
            onLog?.Invoke("❌ [ADB] No se encontró adb.exe");
            return false;
        }

        onLog?.Invoke("▶ [ADB] Ejecutando: adb reboot edl...");
        int code = await RunProcessAsync(adbExe, "reboot edl", onLog, ct);
        return code == 0;
    }

    /// <summary>
    /// Ejecuta una operación de emmcdl.exe (ej. volcado o escritura de sectores Raw/GPT por puerto COM).
    /// </summary>
    public async Task<int> RunEmmcdlAsync(string portName, string arguments, Action<string>? onLog = null, CancellationToken ct = default)
    {
        string? emmcdl = GetToolPath("emmcdl");
        if (emmcdl == null)
        {
            onLog?.Invoke("❌ [emmcdl] No se encontró emmcdl.exe");
            return -1;
        }

        string portNumber = portName.Replace("COM", "", StringComparison.OrdinalIgnoreCase).Trim();
        string args = $"-p COM{portNumber} {arguments}";
        onLog?.Invoke($"▶ [emmcdl] COM{portNumber} {arguments}");
        return await RunProcessAsync(emmcdl, args, onLog, ct);
    }

    /// <summary>
    /// Ejecuta una operación de fh_loader.exe (cargador de streaming Firehose de Qualcomm).
    /// </summary>
    public async Task<int> RunFhLoaderAsync(string portName, string arguments, Action<string>? onLog = null, CancellationToken ct = default)
    {
        string? fhLoader = GetToolPath("fh_loader");
        if (fhLoader == null)
        {
            onLog?.Invoke("❌ [fh_loader] No se encontró fh_loader.exe");
            return -1;
        }

        string portNumber = portName.Replace("COM", "", StringComparison.OrdinalIgnoreCase).Trim();
        string args = $"--port=\\\\.\\COM{portNumber} {arguments}";
        onLog?.Invoke($"▶ [fh_loader] COM{portNumber} {arguments}");
        return await RunProcessAsync(fhLoader, args, onLog, ct);
    }

    /// <summary>
    /// Reconstruye el volcado de la ROM primaria (PBL) de SM6125 (Snapdragon 665)
    /// a partir de una captura USB de Bus Hound (.txt), validando hashes criptográficos de bloques.
    /// Transpilado nativamente desde extract_sm6125_pbl.py.
    /// </summary>
    public static Sm6125ExtractionResult RebuildSm6125Bootstrap(string busHoundCapturePath, string outputDirectory)
    {
        if (!File.Exists(busHoundCapturePath))
            throw new FileNotFoundException("No se encontró el archivo de captura Bus Hound.", busHoundCapturePath);

        const string ExpectedSha256 = "ec2f75c34a1f89a207a528c83aef5ac42873d94231cb5bf49b70c0b238ed1a55";
        var ranges = new (int Offset, int Length, int Request, int Output)[]
        {
            (0, 64, 276, 278),
            (64, 896, 279, 280),
            (4096, 4096, 281, 282),
            (8192, 4096, 284, 285),
            (12288, 4096, 287, 288),
            (16384, 4096, 290, 291)
        };

        var posRegex = new Regex(@"(\d+)\.(\d+)\.(\d+)(?:\((\d+)\))?\s*$", RegexOptions.Compiled);

        var packets = new Dictionary<(int Command, int Phase), (string Direction, int Repeat, List<byte> Data)>();
        (string Direction, int Repeat, List<byte> Data)? current = null;

        var lines = File.ReadAllLines(busHoundCapturePath, Encoding.UTF8);
        foreach (var line in lines)
        {
            var match = posRegex.Match(line);
            if (!match.Success) continue;

            int command = int.Parse(match.Groups[1].Value);
            int phase = int.Parse(match.Groups[2].Value);
            int offset = int.Parse(match.Groups[3].Value);

            if (command > 296) break;

            string device = line.Length >= 6 ? line[..6].Trim() : "";
            if (!string.IsNullOrEmpty(device))
            {
                current = null;
                string dir = line.Length >= 12 ? line.Substring(7, 5).Trim() : "";
                if (device == "9.1" && (dir == "IN" || dir == "OUT"))
                {
                    int repeat = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 1;
                    current = (dir, repeat, new List<byte>());
                    packets[(command, phase)] = current.Value;
                }
            }

            if (current != null)
            {
                if (line.Length >= 40)
                {
                    string raw = line.Substring(14, 26).Trim();
                    string cleanHex = raw.Replace(" ", "");
                    if (!string.IsNullOrEmpty(cleanHex) && cleanHex.Length % 2 == 0)
                    {
                        byte[] bytes = Convert.FromHexString(cleanHex);
                        current.Value.Data.AddRange(bytes);
                    }
                }
            }
        }

        byte[] image = new byte[20480];
        var coverage = new List<object>();

        foreach (var (offset, length, request, output) in ranges)
        {
            if (!packets.TryGetValue((output, 1), out var outPkt))
                throw new InvalidDataException($"Falta el paquete de salida para fase {output}.");

            byte[] payload = outPkt.Data.Take(length).ToArray();
            Array.Copy(payload, 0, image, offset, payload.Length);

            using var sha = SHA256.Create();
            string blockHash = Convert.ToHexString(sha.ComputeHash(payload)).ToLowerInvariant();
            coverage.Add(new
            {
                Offset = offset,
                Length = length,
                RequestCmd = request,
                OutputCmd = output,
                Sha256 = blockHash
            });
        }

        using var fullSha = SHA256.Create();
        string fullHash = Convert.ToHexString(fullSha.ComputeHash(image)).ToLowerInvariant();

        Directory.CreateDirectory(outputDirectory);
        string binPath = Path.Combine(outputDirectory, "sm6125_pbl_bootstrap.bin");
        string jsonPath = Path.Combine(outputDirectory, "sm6125_pbl_bootstrap.json");

        File.WriteAllBytes(binPath, image);

        var manifestObj = new
        {
            ResourceSha256 = fullHash,
            ExpectedSha256 = ExpectedSha256,
            IsValid = fullHash.Equals(ExpectedSha256, StringComparison.OrdinalIgnoreCase),
            ResourceLength = image.Length,
            TransmittedBytes = 17344,
            Coverage = coverage,
            UnobservedZeroPlaceholder = new { Offset = 960, Length = 3136 }
        };

        File.WriteAllText(jsonPath, JsonSerializer.Serialize(manifestObj, new JsonSerializerOptions { WriteIndented = true }));

        return new Sm6125ExtractionResult(
            BootstrapBinaryPath: binPath,
            ManifestJsonPath: jsonPath,
            Sha256: fullHash,
            TotalBytes: image.Length,
            TriggerWrites: 95
        );
    }

    private static async Task<int> RunProcessAsync(string exePath, string arguments, Action<string>? onLog, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi };

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data)) onLog?.Invoke(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data)) onLog?.Invoke($"[ERR] {e.Data}");
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(ct);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            return -99;
        }
        catch (Exception ex)
        {
            onLog?.Invoke($"❌ Error ejecutando proceso: {ex.Message}");
            return -1;
        }
    }
}

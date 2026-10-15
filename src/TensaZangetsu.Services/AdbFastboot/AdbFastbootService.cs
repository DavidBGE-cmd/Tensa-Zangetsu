using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TensaZangetsu.Services.AdbFastboot;

public class AdbFastbootService : IAdbFastbootService
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, (string AndroidVer, string BuildId, string ActiveSlot)> _adbMetadataCache = new();
    private CancellationTokenSource? _monitorCts;
    private AdbFastbootDeviceInfo _currentDevice = AdbFastbootDeviceInfo.Disconnected;

    public event Action<AdbFastbootDeviceInfo>? DeviceStateChanged;
    public event Action<string>? OutputEmitted;

    public AdbFastbootDeviceInfo CurrentDevice => _currentDevice;
    public string AdbExecutablePath { get; private set; } = "adb";
    public string FastbootExecutablePath { get; private set; } = "fastboot";
    public bool IsMonitoring => _monitorCts != null;

    public AdbFastbootService(string? customToolsDirectory = null)
    {
        LocateExecutables(customToolsDirectory);
    }

    private void LocateExecutables(string? customToolsDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(customToolsDirectory) && Directory.Exists(customToolsDirectory))
        {
            string customAdb = Path.Combine(customToolsDirectory, "adb.exe");
            string customFb = Path.Combine(customToolsDirectory, "fastboot.exe");
            if (File.Exists(customAdb)) AdbExecutablePath = customAdb;
            if (File.Exists(customFb)) FastbootExecutablePath = customFb;
        }

        // 1. Verificar binarios integrados en Assets/Tools/PlatformTools
        string? localAssetsAdb = FindBundledTool("adb.exe");
        string? localAssetsFb = FindBundledTool("fastboot.exe");

        if (localAssetsAdb != null && (AdbExecutablePath == "adb" || !File.Exists(AdbExecutablePath)))
            AdbExecutablePath = localAssetsAdb;
        if (localAssetsFb != null && (FastbootExecutablePath == "fastboot" || !File.Exists(FastbootExecutablePath)))
            FastbootExecutablePath = localAssetsFb;

        // 2. Verificar ruta estándar preferida en C:\platform-tools\
        if (!File.Exists(AdbExecutablePath))
        {
            string cToolsAdb = @"C:\platform-tools\adb.exe";
            string cToolsFb = @"C:\platform-tools\fastboot.exe";

            if (File.Exists(cToolsAdb)) AdbExecutablePath = cToolsAdb;
            if (File.Exists(cToolsFb)) FastbootExecutablePath = cToolsFb;
        }

        // 3. Si no existen, buscar en LocalAppData Android SDK
        if (!File.Exists(AdbExecutablePath))
        {
            string localSdk = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Android\Sdk\platform-tools\adb.exe");
            if (File.Exists(localSdk))
            {
                AdbExecutablePath = localSdk;
                string localSdkFb = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Android\Sdk\platform-tools\fastboot.exe");
                if (File.Exists(localSdkFb)) FastbootExecutablePath = localSdkFb;
            }
        }
    }

    private static string? FindBundledTool(string toolName)
    {
        string[] relativeFolders = [
            Path.Combine("Assets", "Tools", "PlatformTools"),
            Path.Combine("Assets", "Tools", "ADB"),
            Path.Combine("ExternalAssets", "Tools", "ADB"),
            Path.Combine("ExternalAssets", "Tools", "PlatformTools")
        ];

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        foreach (var rf in relativeFolders)
        {
            string direct = Path.Combine(baseDir, rf, toolName);
            if (File.Exists(direct)) return direct;
        }

        var current = new DirectoryInfo(baseDir);
        while (current != null)
        {
            foreach (var rf in relativeFolders)
            {
                string candidate = Path.Combine(current.FullName, rf, toolName);
                if (File.Exists(candidate)) return candidate;

                string srcCandidate = Path.Combine(current.FullName, "src", "TensaZangetsu.App", rf, toolName);
                if (File.Exists(srcCandidate)) return srcCandidate;
            }

            current = current.Parent;
        }

        return null;
    }

    private void EmitOutput(string text)
    {
        OutputEmitted?.Invoke(text);
    }

    public Task StartMonitoringAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_monitorCts != null) return Task.CompletedTask;
            _monitorCts = new CancellationTokenSource();
        }

        _ = Task.Run(async () =>
        {
            var token = _monitorCts.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var detected = await ScanDevicesAsync(token);
                    UpdateDeviceStateIfChanged(detected);
                    await Task.Delay(1200, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    EmitOutput($"[DEBUG Monitoreo]: {ex.Message}");
                    await Task.Delay(2000, token);
                }
            }
        }, _monitorCts.Token);

        return Task.CompletedTask;
    }

    public Task StopMonitoringAsync()
    {
        lock (_lock)
        {
            _monitorCts?.Cancel();
            _monitorCts = null;
        }
        return Task.CompletedTask;
    }

    private void UpdateDeviceStateIfChanged(AdbFastbootDeviceInfo detected)
    {
        bool changed = detected.Mode != _currentDevice.Mode ||
                       detected.Serial != _currentDevice.Serial ||
                       detected.IsUserspace != _currentDevice.IsUserspace ||
                       detected.ActiveSlot != _currentDevice.ActiveSlot;

        if (changed)
        {
            _currentDevice = detected;
            DeviceStateChanged?.Invoke(_currentDevice);

            if (_currentDevice.Mode != ConnectedDeviceMode.Disconnected)
            {
                EmitOutput($"[ESTADO DISPOSITIVO]: {_currentDevice.ModeBadgeText} | Serial: {_currentDevice.Serial} | {_currentDevice.Brand} {_currentDevice.ModelName}");
                if (!string.IsNullOrWhiteSpace(_currentDevice.DriverAdvice))
                {
                    EmitOutput($"[DIAGNÓSTICO CONTROLADOR]: {_currentDevice.DriverAdvice}");
                }
            }
            else
            {
                EmitOutput("[ESTADO DISPOSITIVO]: Desconectado.");
            }
        }
    }

    public async Task<AdbFastbootDeviceInfo> ScanDevicesAsync(CancellationToken ct = default)
    {
        // 1. Probar ADB devices
        var (adbCode, adbOut, _) = await RunProcessAsync(AdbExecutablePath, "devices -l", ct);
        if (adbCode == 0 && !string.IsNullOrWhiteSpace(adbOut))
        {
            var lines = adbOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("List of devices attached", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("* daemon", StringComparison.OrdinalIgnoreCase)) continue;

                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    string serial = parts[0];
                    string state = parts[1].ToLowerInvariant();

                    if (state == "device")
                    {
                        // ADB Online y Autorizado
                        string brand = ExtractAdbProp(line, "product:") ?? "";
                        string model = ExtractAdbProp(line, "model:") ?? "";
                        string deviceCode = ExtractAdbProp(line, "device:") ?? "";

                        // Leer detalles adicionales desde caché o consultar si es nuevo
                        string androidVer = "";
                        string buildId = "";
                        string activeSlot = "";

                        if (_adbMetadataCache.TryGetValue(serial, out var cachedMeta))
                        {
                            androidVer = cachedMeta.AndroidVer;
                            buildId = cachedMeta.BuildId;
                            activeSlot = cachedMeta.ActiveSlot;
                        }
                        else
                        {
                            var (_, propOut, _) = await RunProcessAsync(AdbExecutablePath, $"-s {serial} shell getprop ro.build.version.release", ct);
                            if (!string.IsNullOrWhiteSpace(propOut)) androidVer = propOut.Trim();

                            var (_, buildOut, _) = await RunProcessAsync(AdbExecutablePath, $"-s {serial} shell getprop ro.build.display.id", ct);
                            if (!string.IsNullOrWhiteSpace(buildOut)) buildId = buildOut.Trim();

                            var (_, slotOut, _) = await RunProcessAsync(AdbExecutablePath, $"-s {serial} shell getprop ro.boot.slot_suffix", ct);
                            if (!string.IsNullOrWhiteSpace(slotOut)) activeSlot = slotOut.Trim().Replace("_", "");

                            _adbMetadataCache[serial] = (androidVer, buildId, activeSlot);
                        }

                        return new AdbFastbootDeviceInfo(
                            Mode: ConnectedDeviceMode.AdbOnline,
                            Serial: serial,
                            Brand: brand.ToUpperInvariant(),
                            ModelName: model,
                            ProductCode: deviceCode,
                            AndroidVersion: string.IsNullOrEmpty(androidVer) ? "Android" : $"Android {androidVer}",
                            BuildId: buildId,
                            ActiveSlot: activeSlot,
                            IsUserspace: false,
                            DriverName: "Google Android Composite ADB Interface",
                            IsDriverOk: true,
                            DriverAdvice: "Controlador ADB funcionando correctamente.",
                            Details: $"Dispositivo activo y listo para comandos shell / sideload / backup."
                        );
                    }
                    else if (state == "unauthorized")
                    {
                        return new AdbFastbootDeviceInfo(
                            Mode: ConnectedDeviceMode.AdbUnauthorized,
                            Serial: serial,
                            Brand: "Android",
                            ModelName: "Dispositivo sin autorizar",
                            ProductCode: "",
                            AndroidVersion: "",
                            BuildId: "",
                            ActiveSlot: "",
                            IsUserspace: false,
                            DriverName: "Google Android ADB Interface",
                            IsDriverOk: true,
                            DriverAdvice: "⚠️ Pantalla Bloqueada: Desbloquee el teléfono y marque 'Permitir siempre desde esta computadora' en el cuadro de diálogo de huella digital RSA.",
                            Details: "La conexión ADB física existe pero falta autorización del usuario en pantalla."
                        );
                    }
                    else if (state == "recovery")
                    {
                        return new AdbFastbootDeviceInfo(
                            Mode: ConnectedDeviceMode.AdbRecovery,
                            Serial: serial,
                            Brand: "Android",
                            ModelName: "Recovery Mode",
                            ProductCode: "",
                            AndroidVersion: "",
                            BuildId: "",
                            ActiveSlot: "",
                            IsUserspace: false,
                            DriverName: "Android Recovery ADB Interface",
                            IsDriverOk: true,
                            DriverAdvice: "Dispositivo en Modo Recovery (TWRP / OrangeFox / Stock). Listo para comandos de recuperación y wipe.",
                            Details: "Modo de recuperación activo."
                        );
                    }
                    else if (state == "sideload")
                    {
                        return new AdbFastbootDeviceInfo(
                            Mode: ConnectedDeviceMode.AdbSideload,
                            Serial: serial,
                            Brand: "Android",
                            ModelName: "ADB Sideload",
                            ProductCode: "",
                            AndroidVersion: "",
                            BuildId: "",
                            ActiveSlot: "",
                            IsUserspace: false,
                            DriverName: "Android Sideload Interface",
                            IsDriverOk: true,
                            DriverAdvice: "Modo ADB Sideload activo en Recovery. Listo para flashear paquetes OTA o ROM con 'adb sideload <archivo.zip>'.",
                            Details: "Sideload listo."
                        );
                    }
                }
            }
        }

        // 2. Probar Fastboot devices
        var (fbCode, fbOut, _) = await RunProcessAsync(FastbootExecutablePath, "devices", ct);
        if (fbCode == 0 && !string.IsNullOrWhiteSpace(fbOut))
        {
            var lines = fbOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[1].Equals("fastboot", StringComparison.OrdinalIgnoreCase))
                {
                    string serial = parts[0];

                    // Determinar si es FastbootD (userspace) o Bootloader Fastboot
                    var (_, userspaceOut, _) = await RunProcessAsync(FastbootExecutablePath, $"-s {serial} getvar is-userspace", ct);
                    bool isUserspace = userspaceOut.Contains("is-userspace: yes", StringComparison.OrdinalIgnoreCase);

                    // Consultar producto y slot
                    string product = "";
                    string currentSlot = "";

                    var (_, prodOut, _) = await RunProcessAsync(FastbootExecutablePath, $"-s {serial} getvar product", ct);
                    var matchProd = Regex.Match(prodOut, @"product:\s*(\S+)");
                    if (matchProd.Success) product = matchProd.Groups[1].Value;

                    var (_, slotOut, _) = await RunProcessAsync(FastbootExecutablePath, $"-s {serial} getvar current-slot", ct);
                    var matchSlot = Regex.Match(slotOut, @"current-slot:\s*(\S+)");
                    if (matchSlot.Success) currentSlot = matchSlot.Groups[1].Value;

                    if (isUserspace)
                    {
                        return new AdbFastbootDeviceInfo(
                            Mode: ConnectedDeviceMode.FastbootD,
                            Serial: serial,
                            Brand: "Android Userspace",
                            ModelName: $"FastbootD ({product})",
                            ProductCode: product,
                            AndroidVersion: "",
                            BuildId: "",
                            ActiveSlot: currentSlot,
                            IsUserspace: true,
                            DriverName: "Android Bootloader Interface / WinUSB",
                            IsDriverOk: true,
                            DriverAdvice: "⚡ MODO FASTBOOT-D ACTIVO: Particiones dinámicas habilitadas (super: system, vendor, product, system_ext). Listo para flashear GSIs o particiones de usuario.",
                            Details: $"FastbootD activo. Slot actual: {currentSlot}."
                        );
                    }
                    else
                    {
                        return new AdbFastbootDeviceInfo(
                            Mode: ConnectedDeviceMode.Fastboot,
                            Serial: serial,
                            Brand: "Bootloader",
                            ModelName: $"Fastboot ({product})",
                            ProductCode: product,
                            AndroidVersion: "",
                            BuildId: "",
                            ActiveSlot: currentSlot,
                            IsUserspace: false,
                            DriverName: "Android Bootloader Interface (Kedacom / WinUSB)",
                            IsDriverOk: true,
                            DriverAdvice: "Modo Fastboot Bootloader clásico activo. Permite comandos OEM, lectura de variables críticas y flasheo de particiones físicas (boot, recovery, abl).",
                            Details: $"Fastboot Bootloader. Slot actual: {currentSlot}."
                        );
                    }
                }
            }
        }

        // 3. Inspección por Registro de Windows (Samsung Download, BROM, EDL, o Drivers Faltantes)
        var hwDevice = CheckWindowsUsbHardware();
        if (hwDevice != null)
        {
            return hwDevice;
        }

        return AdbFastbootDeviceInfo.Disconnected;
    }

    private AdbFastbootDeviceInfo? CheckWindowsUsbHardware()
    {
        try
        {
            var enumerator = UsbWatcher.UsbEnumeratorFactory.Create();
            var devices = enumerator.GetDevices();

            foreach (var dev in devices)
            {
                int vid = dev.VendorId ?? 0;
                int pid = dev.ProductId ?? 0;
                string desc = dev.Description ?? dev.FriendlyName ?? "";
                string friendly = dev.FriendlyName ?? dev.Description ?? "Dispositivo USB";

                // 1. MediaTek BROM / Preloader / DA (Puertos COM y USB activos)
                if (vid == 0x0E8D || desc.Contains("MediaTek", StringComparison.OrdinalIgnoreCase))
                {
                    var mode = pid switch
                    {
                        0x0003 => ConnectedDeviceMode.MtkBrom,
                        0x2000 => ConnectedDeviceMode.MtkPreloader,
                        0x2001 => ConnectedDeviceMode.MtkDa,
                        _ => desc.Contains("Preloader", StringComparison.OrdinalIgnoreCase)
                            ? ConnectedDeviceMode.MtkPreloader
                            : ConnectedDeviceMode.MtkBrom
                    };

                    string modeLabel = mode switch
                    {
                        ConnectedDeviceMode.MtkBrom => "BROM / BootROM",
                        ConnectedDeviceMode.MtkPreloader => "Preloader VCOM",
                        ConnectedDeviceMode.MtkDa => "DA USB",
                        _ => "MediaTek"
                    };

                    return new AdbFastbootDeviceInfo(
                        Mode: mode,
                        Serial: friendly,
                        Brand: "MediaTek",
                        ModelName: friendly,
                        ProductCode: $"VID_0E8D&PID_{pid:X4}",
                        AndroidVersion: "",
                        BuildId: "",
                        ActiveSlot: "",
                        IsUserspace: false,
                        DriverName: friendly,
                        IsDriverOk: true,
                        DriverAdvice: $"Dispositivo MediaTek en modo {modeLabel} detectado en Windows.",
                        Details: $"Puerto MediaTek activo: {friendly}."
                    );
                }

                // 2. Qualcomm Snapdragon en Modo Emergencia EDL 9008
                if ((vid == 0x05C6 && pid == 0x9008) || desc.Contains("9008", StringComparison.OrdinalIgnoreCase))
                {
                    return new AdbFastbootDeviceInfo(
                        Mode: ConnectedDeviceMode.QualcommEdl,
                        Serial: friendly,
                        Brand: "Qualcomm",
                        ModelName: friendly,
                        ProductCode: "EDL_9008",
                        AndroidVersion: "",
                        BuildId: "",
                        ActiveSlot: "",
                        IsUserspace: false,
                        DriverName: friendly,
                        IsDriverOk: true,
                        DriverAdvice: "Dispositivo Qualcomm Snapdragon en Modo Emergencia EDL 9008 listo para programador Firehose.",
                        Details: "Qualcomm HS-USB QDLoader 9008 activo."
                    );
                }

                // 3. UNISOC / Spreadtrum BSL (Diag Port)
                if (vid == 0x1782 || desc.Contains("SPRD", StringComparison.OrdinalIgnoreCase) || desc.Contains("Spreadtrum", StringComparison.OrdinalIgnoreCase))
                {
                    return new AdbFastbootDeviceInfo(
                        Mode: ConnectedDeviceMode.UnisocBsl,
                        Serial: friendly,
                        Brand: "UNISOC",
                        ModelName: friendly,
                        ProductCode: "SPRD_BSL",
                        AndroidVersion: "",
                        BuildId: "",
                        ActiveSlot: "",
                        IsUserspace: false,
                        DriverName: friendly,
                        IsDriverOk: true,
                        DriverAdvice: "Dispositivo UNISOC / Spreadtrum en Modo BSL BootROM detectado.",
                        Details: "Protocolo BSL listo para cargar FDL1/FDL2."
                    );
                }

                // 4. Samsung Download Mode (SOLO si el dispositivo está actualmente conectado físicamente)
                if (vid == 0x04E8 && (pid == 0x685D || pid == 0x6860 || desc.Contains("SAMSUNG Mobile USB", StringComparison.OrdinalIgnoreCase)))
                {
                    return new AdbFastbootDeviceInfo(
                        Mode: ConnectedDeviceMode.SamsungDownload,
                        Serial: "SAMSUNG-DOWNLOAD",
                        Brand: "SAMSUNG",
                        ModelName: friendly,
                        ProductCode: $"VID_04E8&PID_{pid:X4}",
                        AndroidVersion: "",
                        BuildId: "",
                        ActiveSlot: "",
                        IsUserspace: false,
                        DriverName: friendly,
                        IsDriverOk: true,
                        DriverAdvice: "Dispositivo Samsung en Modo Descarga (Odin / Loke) activo.",
                        Details: "Samsung Download Mode activo."
                    );
                }
            }
        }
        catch { }

        return null;
    }

    private static string? ExtractAdbProp(string line, string prefix)
    {
        int idx = line.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        int start = idx + prefix.Length;
        int end = line.IndexOf(' ', start);
        if (end < 0) end = line.Length;
        return line.Substring(start, end - start);
    }

    public async Task<string> ExecuteRawCommandAsync(string command, string args, CancellationToken ct = default)
    {
        string exe = command.Equals("fastboot", StringComparison.OrdinalIgnoreCase)
            ? FastbootExecutablePath
            : (command.Equals("adb", StringComparison.OrdinalIgnoreCase) ? AdbExecutablePath : command);

        var (code, stdout, stderr) = await RunProcessAsync(exe, args, ct);
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(stdout)) sb.AppendLine(stdout.Trim());
        if (!string.IsNullOrWhiteSpace(stderr)) sb.AppendLine($"[STDERR]: {stderr.Trim()}");
        return sb.ToString();
    }

    // ================= UNIVERSAL REBOOT & POWER =================
    public async Task<bool> RebootAsync(string targetMode, CancellationToken ct = default)
    {
        string mode = targetMode.ToLowerInvariant();
        EmitOutput($"::: Solicitando reinicio a modo: {mode} :::");

        if (_currentDevice.Mode is ConnectedDeviceMode.Fastboot or ConnectedDeviceMode.FastbootD)
        {
            string args = mode switch
            {
                "recovery" => "reboot recovery",
                "bootloader" => "reboot bootloader",
                "fastboot" or "fastbootd" => "reboot fastboot",
                "edl" => "oem reboot-edl",
                _ => "reboot"
            };
            var (code, outStr, _) = await RunProcessAsync(FastbootExecutablePath, args, ct);
            EmitOutput(outStr);
            return code == 0;
        }
        else
        {
            // Vía ADB
            string args = mode switch
            {
                "recovery" => "reboot recovery",
                "bootloader" => "reboot bootloader",
                "fastboot" or "fastbootd" => "reboot fastboot",
                "download" => "reboot download",
                "edl" => "reboot edl",
                "sideload" => "reboot sideload",
                _ => "reboot"
            };
            var (code, outStr, _) = await RunProcessAsync(AdbExecutablePath, args, ct);
            EmitOutput(outStr);
            return code == 0;
        }
    }

    public async Task<string> GetPropertiesAsync(CancellationToken ct = default)
    {
        if (_currentDevice.Mode == ConnectedDeviceMode.AdbOnline)
        {
            var (_, stdout, _) = await RunProcessAsync(AdbExecutablePath, "shell getprop", ct);
            return stdout;
        }
        else if (_currentDevice.Mode is ConnectedDeviceMode.Fastboot or ConnectedDeviceMode.FastbootD)
        {
            var (_, stdout, stderr) = await RunProcessAsync(FastbootExecutablePath, "getvar all", ct);
            return string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
        }
        return "Dispositivo no disponible para consulta de propiedades.";
    }

    // ================= FASTBOOT DYNAMIC & SLOTS =================
    public async Task<string> CheckFastbootSlotAsync(CancellationToken ct = default)
    {
        var (_, stdout, stderr) = await RunProcessAsync(FastbootExecutablePath, "getvar current-slot", ct);
        string res = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
        var match = Regex.Match(res, @"current-slot:\s*(\S+)");
        return match.Success ? match.Groups[1].Value : res.Trim();
    }

    public async Task<bool> SwitchFastbootSlotAsync(string slot, CancellationToken ct = default)
    {
        string s = slot.ToLowerInvariant().Replace("_", "");
        var (code, stdout, stderr) = await RunProcessAsync(FastbootExecutablePath, $"--set-active={s}", ct);
        EmitOutput(string.IsNullOrWhiteSpace(stdout) ? stderr : stdout);
        return code == 0;
    }

    public async Task<bool> SwitchToFastbootDAsync(CancellationToken ct = default)
    {
        EmitOutput("::: Conmutando a Modo Fastboot-D (Userspace) :::");
        if (_currentDevice.Mode == ConnectedDeviceMode.AdbOnline)
        {
            var (code, stdout, _) = await RunProcessAsync(AdbExecutablePath, "reboot fastboot", ct);
            EmitOutput(stdout);
            return code == 0;
        }
        else
        {
            var (code, stdout, stderr) = await RunProcessAsync(FastbootExecutablePath, "reboot fastboot", ct);
            EmitOutput(string.IsNullOrWhiteSpace(stdout) ? stderr : stdout);
            return code == 0;
        }
    }

    public async Task<bool> SwitchToBootloaderAsync(CancellationToken ct = default)
    {
        EmitOutput("::: Conmutando a Modo Bootloader Fastboot Clásico :::");
        var (code, stdout, stderr) = await RunProcessAsync(FastbootExecutablePath, "reboot bootloader", ct);
        EmitOutput(string.IsNullOrWhiteSpace(stdout) ? stderr : stdout);
        return code == 0;
    }

    public async Task<string> UnlockGenericAsync(CancellationToken ct = default)
    {
        EmitOutput("::: Ejecutando 'fastboot flashing unlock' :::");
        var (code, stdout, stderr) = await RunProcessAsync(FastbootExecutablePath, "flashing unlock", ct);
        string res = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;

        if (code != 0 || res.Contains("unknown command", StringComparison.OrdinalIgnoreCase))
        {
            EmitOutput("Intentando con comando legacy 'fastboot oem unlock'...");
            var (_, legacyOut, legacyErr) = await RunProcessAsync(FastbootExecutablePath, "oem unlock", ct);
            res = string.IsNullOrWhiteSpace(legacyOut) ? legacyErr : legacyOut;
        }

        return res;
    }

    // ================= MOTOROLA SPECIFIC =================
    public async Task<string> GetMotorolaUnlockDataAsync(CancellationToken ct = default)
    {
        EmitOutput("::: Obteniendo Unlock Data de Motorola ('fastboot oem get_unlock_data') :::");
        var (_, stdout, stderr) = await RunProcessAsync(FastbootExecutablePath, "oem get_unlock_data", ct);
        string combined = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;

        var hexLines = new List<string>();
        foreach (var rawLine in combined.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Replace("(bootloader)", "").Replace("#", "").Trim();
            if (Regex.IsMatch(line, "^[0-9a-fA-F]{20,}$"))
            {
                hexLines.Add(line);
            }
        }

        if (hexLines.Count > 0)
        {
            string mergedToken = string.Join("", hexLines);
            EmitOutput($"✓ Código de desbloqueo Motorola generado con éxito ({hexLines.Count} líneas combinadas):");
            EmitOutput(mergedToken);
            EmitOutput("Copie esta clave y péguela en el portal oficial de desbloqueo de Motorola.");
            return mergedToken;
        }

        return combined;
    }

    public async Task<string> UnlockMotorolaAsync(string key, CancellationToken ct = default)
    {
        EmitOutput($"::: Desbloqueando Bootloader Motorola con Clave :::");
        var (_, stdout, stderr) = await RunProcessAsync(FastbootExecutablePath, $"oem unlock {key.Trim()}", ct);
        return string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
    }

    public async Task<string> GetMotorolaHardwareInfoAsync(CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        var (_, out1, err1) = await RunProcessAsync(FastbootExecutablePath, "getvar ro.build.version.full", ct);
        sb.AppendLine($"Build: {(string.IsNullOrWhiteSpace(out1) ? err1 : out1).Trim()}");

        var (_, out2, err2) = await RunProcessAsync(FastbootExecutablePath, "getvar channelid", ct);
        sb.AppendLine($"Canal / Operadora: {(string.IsNullOrWhiteSpace(out2) ? err2 : out2).Trim()}");

        var (_, out3, err3) = await RunProcessAsync(FastbootExecutablePath, "getvar securestate", ct);
        sb.AppendLine($"Estado de Seguridad: {(string.IsNullOrWhiteSpace(out3) ? err3 : out3).Trim()}");

        var (_, out4, err4) = await RunProcessAsync(FastbootExecutablePath, "getvar cid", ct);
        sb.AppendLine($"CID: {(string.IsNullOrWhiteSpace(out4) ? err4 : out4).Trim()}");

        return sb.ToString();
    }

    public async Task<bool> RebootMotorolaEdlAsync(CancellationToken ct = default)
    {
        EmitOutput("::: Reiniciando Motorola a EDL 9008 ('fastboot oem reboot-edl') :::");
        var (code, stdout, stderr) = await RunProcessAsync(FastbootExecutablePath, "oem reboot-edl", ct);
        EmitOutput(string.IsNullOrWhiteSpace(stdout) ? stderr : stdout);
        return code == 0;
    }

    // ================= XIAOMI SPECIFIC =================
    public async Task<string> GetXiaomiAntiRollbackAsync(CancellationToken ct = default)
    {
        EmitOutput("::: Consultando Anti-Rollback (ARB) de Xiaomi ('fastboot getvar anti') :::");
        var (_, stdout, stderr) = await RunProcessAsync(FastbootExecutablePath, "getvar anti", ct);
        string res = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;

        var match = Regex.Match(res, @"anti:\s*(\d+)");
        if (match.Success)
        {
            int arb = int.Parse(match.Groups[1].Value);
            string advice = arb switch
            {
                0 => "Anti-Rollback Desactivado (0). Dispositivo seguro para cualquier ROM de su región.",
                1 => "ARB Nivel 1. Seguro siempre que la ROM a instalar tenga ARB >= 1.",
                2 => "ARB Nivel 2. No flashear firmwares antiguos con ARB inferior.",
                3 => "ARB Nivel 3. Precaución: Flasheo de ROM inferior provocará Hard Brick (EDL 9008).",
                4 => "ARB Nivel 4 (Máximo). ¡CRÍTICO! Prohibido hacer downgrade de MIUI/HyperOS.",
                _ => $"ARB Nivel {arb}."
            };
            return $"Índice Anti-Rollback Detectado: {arb}\n{advice}";
        }

        return res;
    }

    public async Task<string> GetXiaomiProductAndTokenAsync(CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        var (_, out1, err1) = await RunProcessAsync(FastbootExecutablePath, "getvar product", ct);
        sb.AppendLine($"Dispositivo (Codename): {(string.IsNullOrWhiteSpace(out1) ? err1 : out1).Trim()}");

        var (_, out2, err2) = await RunProcessAsync(FastbootExecutablePath, "getvar token", ct);
        sb.AppendLine($"Token de Desbloqueo: {(string.IsNullOrWhiteSpace(out2) ? err2 : out2).Trim()}");

        return sb.ToString();
    }

    public async Task<bool> BypassXiaomiSetupWizardAsync(CancellationToken ct = default)
    {
        EmitOutput("::: Ejecutando Bypass de Asistente de Configuración (MIUI/HyperOS) vía ADB :::");
        await RunProcessAsync(AdbExecutablePath, "shell pm disable-user --user 0 com.google.android.setupwizard", ct);
        await RunProcessAsync(AdbExecutablePath, "shell pm disable-user --user 0 com.miui.setupwizard", ct);
        await RunProcessAsync(AdbExecutablePath, "shell settings put secure user_setup_complete 1", ct);
        await RunProcessAsync(AdbExecutablePath, "shell settings put global device_provisioned 1", ct);
        EmitOutput("✓ Comandos de bypass enviados. El dispositivo debería ingresar directamente a la pantalla de inicio.");
        return true;
    }

    public async Task<bool> RebootXiaomiSideloadAsync(CancellationToken ct = default)
    {
        EmitOutput("::: Reiniciando Xiaomi a Modo Mi Assistant Recovery / Sideload :::");
        var (code, stdout, _) = await RunProcessAsync(AdbExecutablePath, "reboot recovery", ct);
        EmitOutput(stdout);
        return code == 0;
    }

    // ================= SAMSUNG SPECIFIC =================
    public async Task<bool> RebootSamsungDownloadModeAsync(CancellationToken ct = default)
    {
        EmitOutput("::: Reiniciando Samsung a Modo Descarga (Odin) :::");
        var (code, stdout, _) = await RunProcessAsync(AdbExecutablePath, "reboot download", ct);
        if (code != 0)
        {
            EmitOutput("Intentando con 'adb reboot bootloader'...");
            var (c2, s2, _) = await RunProcessAsync(AdbExecutablePath, "reboot bootloader", ct);
            return c2 == 0;
        }
        return code == 0;
    }

    public async Task<string> GetSamsungSoftwareInfoAsync(CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        var (_, out1, _) = await RunProcessAsync(AdbExecutablePath, "shell getprop ro.csc.sales_code", ct);
        sb.AppendLine($"Código CSC / Región: {out1.Trim()}");

        var (_, out2, _) = await RunProcessAsync(AdbExecutablePath, "shell getprop ro.build.PDA", ct);
        sb.AppendLine($"PDA / Versión Base: {out2.Trim()}");

        var (_, out3, _) = await RunProcessAsync(AdbExecutablePath, "shell getprop ro.product.model", ct);
        sb.AppendLine($"Modelo Samsung: {out3.Trim()}");

        return sb.ToString();
    }

    // ================= VIVO SPECIFIC =================
    public async Task<string> GetVivoDeviceInfoAsync(CancellationToken ct = default)
    {
        var (_, stdout, stderr) = await RunProcessAsync(FastbootExecutablePath, "oem info", ct);
        string res = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
        if (res.Contains("unknown command", StringComparison.OrdinalIgnoreCase))
        {
            var (_, pOut, pErr) = await RunProcessAsync(FastbootExecutablePath, "getvar product", ct);
            res = string.IsNullOrWhiteSpace(pOut) ? pErr : pOut;
        }
        return res;
    }

    public async Task<bool> RebootVivoEdlAsync(CancellationToken ct = default)
    {
        EmitOutput("::: Reiniciando Vivo a EDL 9008 ('fastboot oem reboot-edl') :::");
        var (code, stdout, stderr) = await RunProcessAsync(FastbootExecutablePath, "oem reboot-edl", ct);
        EmitOutput(string.IsNullOrWhiteSpace(stdout) ? stderr : stdout);
        return code == 0;
    }

    // ================= ONEPLUS / OPPO / REALME =================
    public async Task<string> UnlockOnePlusBootloaderAsync(CancellationToken ct = default)
    {
        EmitOutput("::: Desbloqueando Bootloader OnePlus / OPLUS ('fastboot flashing unlock') :::");
        var (code, stdout, stderr) = await RunProcessAsync(FastbootExecutablePath, "flashing unlock", ct);
        return string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
    }

    public async Task<string> GetOplusProjectInfoAsync(CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        var (_, out1, err1) = await RunProcessAsync(FastbootExecutablePath, "getvar oppo_version", ct);
        sb.AppendLine($"Versión OPLUS: {(string.IsNullOrWhiteSpace(out1) ? err1 : out1).Trim()}");

        var (_, out2, err2) = await RunProcessAsync(FastbootExecutablePath, "getvar project_name", ct);
        sb.AppendLine($"Proyecto: {(string.IsNullOrWhiteSpace(out2) ? err2 : out2).Trim()}");

        return sb.ToString();
    }

    // ================= PROCESS RUNNER HELPER =================
    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(
        string executable,
        string arguments,
        CancellationToken ct = default)
    {
        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            proc.Start();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);

            // Timeout de 15 segundos para no colgar el proceso
            var completed = await Task.WhenAny(proc.WaitForExitAsync(ct), Task.Delay(15000, ct));
            if (!proc.HasExited)
            {
                try { proc.Kill(); } catch { }
                return (-1, "", "Tiempo de espera agotado (Timeout).");
            }

            string stdout = await stdoutTask;
            string stderr = await stderrTask;

            return (proc.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }
}

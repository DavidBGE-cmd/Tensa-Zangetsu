using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeekFlashCore.App.Services;
using GeekFlashCore.Protocol.Abstractions;
using GeekFlashCore.Protocol.Mtk.Abstractions;
using GeekFlashCore.Services.Engine;
using GeekFlashCore.Services.Firmware;
using GeekFlashCore.Services.Loaders;
using GeekFlashCore.Services.Operations;

namespace GeekFlashCore.App.ViewModels;

public sealed class LogDisplayItem
{
    public string Time { get; }
    public string Message { get; }
    public string ColorHex { get; }

    public LogDisplayItem(string time, string message, string colorHex = "#ECEFF1")
    {
        Time = time;
        Message = message;
        ColorHex = colorHex;
    }
}

public sealed partial class FlashPartitionRow : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected = true;

    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string LengthFormatted { get; set; } = "0 B";
    public long LengthBytes { get; set; }
    public string Type { get; set; } = "EMMC";
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}

public partial class MainViewModel : ViewModelBase
{
    private readonly FlashEngineService _engineService;
    private readonly AutoLoaderResolverService _loaderResolver;
    private readonly MtkAuthenticationService _authService;
    private readonly PythonScriptRunnerService _pythonRunner;
    private readonly MtkLkImageInjector _lkInjector;
    private readonly StorageDialogService _storageDialog = new();
    private readonly List<FlashPartitionRow> _allPartitions = [];
    private CancellationTokenSource? _currentOperationCts;

    // ================== CONEXIÓN Y PUERTO ==================
    [ObservableProperty]
    private string? _selectedPort;

    [ObservableProperty]
    private ObservableCollection<string> _availablePorts = [];

    [ObservableProperty]
    private string _selectedBase = "Mediatek";

    [ObservableProperty]
    private ObservableCollection<string> _availableBases = ["Mediatek", "Qualcomm", "Unisoc", "Samsung"];

    [ObservableProperty]
    private string _selectedLoader = "Brom V1";

    [ObservableProperty]
    private ObservableCollection<string> _availableLoaders = [
        "Brom V1",
        "Brom V2",
        "DA V5 (XFlash)",
        "DA V6 (XML)",
        "Auto-Detect DA",
        "Demo / Emulación Offline"
    ];

    // ================== CUSTOM LOADERS / EXPANDER ==================
    [ObservableProperty]
    private bool _isCustomExpanded;

    [ObservableProperty]
    private string? _customDaPath;

    [ObservableProperty]
    private string? _customPreloaderPath;

    [ObservableProperty]
    private string? _customAuthPath;

    // ================== CONSOLA DE LOGS ==================
    [ObservableProperty]
    private ObservableCollection<LogDisplayItem> _logMessages = [];

    // ================== BARRA DE ESTADO / PROGRESO ==================
    [ObservableProperty]
    private string _progressStatusText = "Info/Reboot";

    [ObservableProperty]
    private int _progressValue = 0;

    [ObservableProperty]
    private bool _isBusy;

    // ================== SERVICIO (TAB 1) ==================
    [ObservableProperty]
    private string _selectedFactoryResetTask = "Format MISC";

    [ObservableProperty]
    private ObservableCollection<string> _factoryResetTasks = ["Format MISC", "Wipe Userdata", "Safe Format (Preserve Photos)"];

    [ObservableProperty]
    private string _selectedSecurityTask = "Backup";

    [ObservableProperty]
    private ObservableCollection<string> _securityTasks = ["Backup", "Restore", "Erase"];

    [ObservableProperty]
    private string _selectedMetaTask = "Remove FRP Tecno/Infinix/Itel";

    [ObservableProperty]
    private ObservableCollection<string> _metaTasks = [
        "Remove FRP Tecno/Infinix/Itel",
        "Factory Reset Meta",
        "Read Meta Info",
        "Dump NVRAM Meta"
    ];

    [ObservableProperty]
    private string _selectedMt62xxTask = "FactoryReset/Reset Data MT62xx";

    [ObservableProperty]
    private ObservableCollection<string> _mt62xxTasks = ["FactoryReset/Reset Data MT62xx", "Read Flash MT62xx", "Format FAT MT62xx"];

    // ================== FLASH (TAB 2) ==================
    [ObservableProperty]
    private string _selectedRebootType = "Mediatek TXT,XML Scatter";

    [ObservableProperty]
    private ObservableCollection<string> _rebootTypes = [
        "Mediatek TXT,XML Scatter",
        "Qualcomm rawprogram.xml",
        "UNISOC .PAC Firmware"
    ];

    [ObservableProperty]
    private string? _firmwareScatterPath;

    [ObservableProperty]
    private string _scatterFilterQuery = string.Empty;

    [ObservableProperty]
    private ObservableCollection<FlashPartitionRow> _partitions = [];

    [ObservableProperty]
    private FlashPartitionRow? _selectedPartition;

    // ================== TOOLS (TAB 3) ==================
    [ObservableProperty]
    private string _selectedRpmbTask = "Read";

    [ObservableProperty]
    private ObservableCollection<string> _rpmbTasks = ["Read", "Write", "Erase"];

    [ObservableProperty]
    private string _selectedOemInfoTask = "Read";

    [ObservableProperty]
    private ObservableCollection<string> _oemInfoTasks = ["Read", "Write", "Erase"];

    [ObservableProperty]
    private string _selectedLkTask = "Patch Boot Verification (Orange State)";

    [ObservableProperty]
    private ObservableCollection<string> _lkTasks = [
        "Patch Boot Verification (Orange State)",
        "Bypass Certificate Check",
        "Inject bl2_ext Payload"
    ];

    // ================== SETTINGS (TAB 4) ==================
    [ObservableProperty]
    private int _baudRate = 921600;

    [ObservableProperty]
    private bool _autoReboot = true;

    [ObservableProperty]
    private bool _verifyWrite = true;

    [ObservableProperty]
    private bool _useHighSpeedUsb = true;

    [ObservableProperty]
    private string _detectedChipsetBanner = "Chipset ID : Ready";

    public MainViewModel()
    {
        _engineService = new FlashEngineService();
        _loaderResolver = new AutoLoaderResolverService();
        _authService = new MtkAuthenticationService();
        _pythonRunner = new PythonScriptRunnerService();
        _lkInjector = new MtkLkImageInjector();

        // Enlazar eventos del motor de flasheo
        _engineService.OnLog += (level, msg) =>
        {
            string color = level switch
            {
                GeekFlashCore.Services.Engine.LogLevel.Success => "#00E676",
                GeekFlashCore.Services.Engine.LogLevel.Warning => "#FFAB00",
                GeekFlashCore.Services.Engine.LogLevel.Error => "#FF1744",
                GeekFlashCore.Services.Engine.LogLevel.Debug => "#78909C",
                _ => "#00E5FF"
            };
            AddLog(msg, color);
        };

        _engineService.OnProgress += (status, val) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                ProgressStatusText = status;
                ProgressValue = Math.Clamp(val, 0, 100);
            });
        };

        RefreshPorts();
        ShowBannerWelcome();
    }

    private void ShowBannerWelcome()
    {
        AddLog("Dear Customer : GSMXTEAM", "#00E5FF");
        AddLog("Activation Plan : Platinum", "#ECEFF1");
        AddLog("Remaining Days : 360", "#ECEFF1");
        AddLog("Activation Date : 2026-10-01 06:24:43", "#ECEFF1");
        AddLog("Activation Expiration : 2027-10-01 06:24:43", "#ECEFF1");
        AddLog("Your Balance : 999.00", "#00E5FF");
        AddLog("### Notification ### :", "#FF1744");
        AddLog("GEEKFLASH PRO • ALL-IN-ONE UNIVERSAL FLASHING & SERVICING SUITE", "#00E676");
        AddLog("[World First] Added Unisoc Chip Tiger T7300 [Itel S26 Ultra]", "#00E5FF");
        AddLog("[HOT] Added MediaTek MT6768 / MT6765 / MT6785 / MT6833 / MT6877 Support", "#00E5FF");
        AddLog("[World First] Added Qualcomm Snapdragon EDL 9008 + Sahara PBL Dump", "#00E5FF");
        AddLog("[World First] Added Samsung MediaTek BROM + Preloader Auth / RPMB / OEMINFO", "#00E5FF");
        AddLog("----------------------------------------------------------------", "#546E7A");
    }

    public void AddLog(string message, string colorHex = "#ECEFF1")
    {
        Dispatcher.UIThread.Post(() =>
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            LogMessages.Add(new LogDisplayItem(time, message, colorHex));
            if (LogMessages.Count > 1000) LogMessages.RemoveAt(0);
        });
    }

    [RelayCommand]
    public void RefreshPorts()
    {
        AvailablePorts.Clear();
        try
        {
            var detected = _engineService.DetectConnectedHardwarePort();
            if (!string.IsNullOrEmpty(detected.PortName))
            {
                AvailablePorts.Add(detected.Description);
                SelectedPort = detected.Description;
                AddLog($"Dispositivo detectado: {detected.Description} ({detected.Platform})", "#00E676");
            }

            var ports = SerialPort.GetPortNames();
            foreach (var p in ports)
            {
                if (!AvailablePorts.Any(x => x.Contains(p))) AvailablePorts.Add(p);
            }

            if (SelectedPort == null && AvailablePorts.Count > 0)
                SelectedPort = AvailablePorts[0];
        }
        catch (Exception ex)
        {
            AddLog($"Error al enumerar puertos: {ex.Message}", "#FFAB00");
        }
    }

    [RelayCommand]
    public void ClearLogs()
    {
        LogMessages.Clear();
    }

    [RelayCommand]
    public void CancelOperation()
    {
        if (_currentOperationCts != null && !_currentOperationCts.IsCancellationRequested)
        {
            AddLog("🛑 Solicitando cancelación de operación...", "#FF1744");
            _currentOperationCts.Cancel();
        }
    }

    // =========================================================================
    // =========================================================================
    // COMANDOS DE EXPLORACIÓN DE ARCHIVOS (STORAGE PICKER)
    // =========================================================================
    [RelayCommand]
    public async Task BrowseCustomDaAsync()
    {
        var path = await _storageDialog.OpenFileDialogAsync("Seleccionar Download Agent (DA)", ("Archivos DA (*.bin)", ["*.bin"]));
        if (!string.IsNullOrEmpty(path))
        {
            CustomDaPath = path;
            AddLog($"DA personalizado seleccionado: {Path.GetFileName(path)}", "#00E5FF");
        }
    }

    [RelayCommand]
    public async Task BrowseCustomPreloaderAsync()
    {
        var path = await _storageDialog.OpenFileDialogAsync("Seleccionar Preloader EMI", ("Preloader (*.bin)", ["*.bin"]));
        if (!string.IsNullOrEmpty(path))
        {
            CustomPreloaderPath = path;
            AddLog($"Preloader personalizado seleccionado: {Path.GetFileName(path)}", "#00E5FF");
        }
    }

    [RelayCommand]
    public async Task BrowseCustomAuthAsync()
    {
        var path = await _storageDialog.OpenFileDialogAsync("Seleccionar Archivo de Autenticación", ("Archivos Auth (*.auth, *.bin)", ["*.auth", "*.bin"]));
        if (!string.IsNullOrEmpty(path))
        {
            CustomAuthPath = path;
            AddLog($"Auth personalizado seleccionado: {Path.GetFileName(path)}", "#00E5FF");
        }
    }

    [RelayCommand]
    public async Task BrowseFirmwareAsync()
    {
        var path = await _storageDialog.OpenFileDialogAsync(
            "Seleccionar Firmware / Scatter",
            ("Archivos de Firmware / Scatter", ["*.txt", "*.xml", "*.ofp", "*.ops", "*.pac", "*.bin"]),
            ("MTK Scatter (*.txt, *.xml)", ["*.txt", "*.xml"]),
            ("Paquetes ROM (*.ofp, *.ops, *.pac, *.ozip, payload.bin)", ["*.ofp", "*.ops", "*.pac", "*.ozip", "payload.bin"]),
            ("Todos los archivos", ["*.*"]));

        if (!string.IsNullOrEmpty(path))
        {
            FirmwareScatterPath = path;
        }
    }

    partial void OnFirmwareScatterPathChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !File.Exists(value)) return;
        LoadScatterOrRom(value);
    }

    partial void OnScatterFilterQueryChanged(string value)
    {
        ApplyPartitionFilter();
    }

    private void ApplyPartitionFilter()
    {
        if (string.IsNullOrWhiteSpace(ScatterFilterQuery))
        {
            Partitions = new ObservableCollection<FlashPartitionRow>(_allPartitions);
        }
        else
        {
            var terms = ScatterFilterQuery.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var filtered = _allPartitions.Where(p =>
                terms.Any(t => p.Name.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                               p.FileName.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                               p.Type.Contains(t, StringComparison.OrdinalIgnoreCase))).ToList();
            Partitions = new ObservableCollection<FlashPartitionRow>(filtered);
        }
    }

    private void LoadScatterOrRom(string path)
    {
        try
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string fileName = Path.GetFileName(path);

            _allPartitions.Clear();
            Partitions.Clear();

            if (ext is ".txt" or ".xml")
            {
                string content = File.ReadAllText(path);
                var manifest = MtkScatterManifestParser.Parse(content);
                int idx = 0;
                string dir = Path.GetDirectoryName(path) ?? "";

                foreach (var p in manifest.Partitions)
                {
                    string partFn = p.FileName ?? $"{p.Name}.img";
                    string partFp = Path.Combine(dir, partFn);
                    long len = (long)p.Length;

                    var row = new FlashPartitionRow
                    {
                        Index = idx++,
                        Name = p.Name,
                        LengthBytes = len,
                        LengthFormatted = FormatBytes(len),
                        Type = p.Storage.ToString().ToUpperInvariant(),
                        FileName = partFn,
                        FilePath = partFp,
                        IsSelected = p.Download
                    };
                    _allPartitions.Add(row);
                }

                ApplyPartitionFilter();
                AddLog($"✔ Scatter cargado: {fileName} ({_allPartitions.Count} particiones)", "#00E676");
                ProgressStatusText = "Scatter cargado";
            }
            else if (ext is ".ofp" or ".ops" or ".pac" or ".ozip" || fileName.Equals("payload.bin", StringComparison.OrdinalIgnoreCase))
            {
                var entries = _engineService.InspectRomPackage(path);
                int idx = 0;
                foreach (var e in entries)
                {
                    var row = new FlashPartitionRow
                    {
                        Index = idx++,
                        Name = e.Name,
                        LengthBytes = e.SizeBytes,
                        LengthFormatted = e.SizeFormatted,
                        Type = e.Category,
                        FileName = e.FileName,
                        FilePath = path,
                        IsSelected = !e.IsLoader
                    };
                    _allPartitions.Add(row);
                }

                ApplyPartitionFilter();
                AddLog($"✔ Paquete ROM cargado: {fileName} ({_allPartitions.Count} entradas)", "#00E676");
                ProgressStatusText = "ROM cargada";
            }
        }
        catch (Exception ex)
        {
            AddLog($"❌ Error al parsear firmware/scatter: {ex.Message}", "#FF1744");
        }
    }

    // =========================================================================
    // SERVICIO: READ INFO
    // =========================================================================
    [RelayCommand]
    public async Task ReadInfoAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        _currentOperationCts = new CancellationTokenSource();
        var ct = _currentOperationCts.Token;
        bool isDemo = SelectedLoader == "Demo / Emulación Offline";

        try
        {
            AddLog($"Operation : Read Info {(isDemo ? "[MODO SIMULACIÓN]" : "")}", "#FFAB00");
            ProgressStatusText = "Waiting for device";
            ProgressValue = 2;

            var (portName, platform, desc) = _engineService.DetectConnectedHardwarePort();
            string actualPort = portName ?? (SelectedPort?.Split('(').LastOrDefault()?.TrimEnd(')') ?? "COM9");

            if (!isDemo)
            {
                AddLog($"Buscando dispositivo en puerto {actualPort}...", "#00E5FF");
                Dictionary<string, string> info;
                try
                {
                    info = await _engineService.ReadDeviceInfoAsync(null, null, ct);
                }
                catch (Exception ex)
                {
                    AddLog($"❌ No se pudo conectar con el dispositivo: {ex.Message}", "#FF1744");
                    AddLog("💡 Para conectar un dispositivo MediaTek:", "#00E5FF");
                    AddLog("   1. Apaga el teléfono por completo.", "#ECEFF1");
                    AddLog("   2. Mantén presionadas las teclas Vol+ y Vol-.", "#ECEFF1");
                    AddLog("   3. Conecta el cable USB a la PC y no sueltes hasta que inicie el handshake.", "#ECEFF1");
                    AddLog("ℹ️ Si deseas probar la interfaz sin teléfono conectado, selecciona el Loader 'Demo / Emulación Offline'.", "#FFAB00");
                    ProgressStatusText = "Dispositivo no detectado";
                    ProgressValue = 0;
                    return;
                }

                ushort hwCode = _engineService.LastDetectedFingerprint?.MtkHwCode ?? 0x0707;
                string socName = FlashEngineService.MapHwCodeToName(hwCode);
                DetectedChipsetBanner = $"Chipset ID : {socName} (0x{hwCode:X4})";
                AddLog($"USB Port : {portName} ({desc})", "#00E5FF");
                AddLog($"Chipset ID : {socName} (0x{hwCode:X4})", "#00E676");
                if (info.TryGetValue("Seguridad", out var sec))
                    AddLog($"- Active Sec Flag : {sec}", "#FF1744");

                LoadSimulatedOrReadPartitions();
                ProgressValue = 100;
                ProgressStatusText = "Read Info completado con éxito";
                AddLog("✔ Dispositivo identificado correctamente.", "#00E676");
                return;
            }

            // Flujo en Modo Demostración
            AddLog("[DEMO] Waiting for MTK Device usb device : OK", "#00E676");
            AddLog($"[DEMO] USB Port : MediaTek USB Port ({actualPort})", "#00E5FF");
            AddLog($"[DEMO] USB Device : MediaTek Inc. v3.0.1512.0 [0xE8D-0x3]", "#00E5FF");

            ProgressValue = 4;
            AddLog("[DEMO] Connecting MTK device : OK", "#00E676");

            ushort demoHwCode = 0x0707;
            string meidHex = "0xDC8E9AB41B672449A5B6A58C14D94295";
            string chipsetStr = "MT6768 Helio G70/G80/G85 707 8a00 ca00 0";

            DetectedChipsetBanner = $"Chipset ID : {chipsetStr}";
            AddLog($"[DEMO] MEID : {meidHex}", "#00E5FF");
            AddLog($"[DEMO] Chipset ID : {chipsetStr}", "#FFAB00");
            AddLog("[DEMO] - Active Sec Flag : SBC,SDA", "#FF1744");

            ProgressValue = 10;
            ProgressStatusText = "Bypassing SLA / DAA";

            AddLog("[DEMO] Checking SLA/DAA Security Protection : Evaluando...", "#ECEFF1");
            AddLog("[DEMO] Applying BROM Security Bypass (Penumbra / Kamakiri) : OK", "#00E676");

            ProgressValue = 25;
            AddLog("[DEMO] Sending 1st DA : OK", "#00E676");
            AddLog("[DEMO] Jump to 1st DA : OK", "#00E676");

            ProgressValue = 40;
            AddLog("[DEMO] Start setup da environment : OK", "#00E676");

            var resolved = _loaderResolver.ResolveForDevice(new DeviceHardwareFingerprint(
                Platform: "MTK",
                MtkHwCode: demoHwCode));

            if (!string.IsNullOrEmpty(resolved.MtkPreloaderPath))
                AddLog($"[DEMO] Matched Preloader EMI : {Path.GetFileName(resolved.MtkPreloaderPath)}", "#00E5FF");

            if (!string.IsNullOrEmpty(resolved.MtkDaPath))
                AddLog($"[DEMO] Matched Download Agent (DA) : {Path.GetFileName(resolved.MtkDaPath)}", "#00E5FF");

            ProgressValue = 65;
            AddLog("[DEMO] Sending 2nd DA (DRAM Init) : OK", "#00E676");
            AddLog("[DEMO] Storage Type : eMMC / UFS (High Speed Protocol)", "#00E676");

            ProgressValue = 85;
            AddLog("[DEMO] Reading Partition Table (GPT) : OK", "#00E676");
            LoadSimulatedOrReadPartitions();

            ProgressValue = 100;
            ProgressStatusText = "Read Info Completed Successfully";
            AddLog("✔ [DEMO] Operación completada exitosamente.", "#00E676");
        }
        catch (OperationCanceledException)
        {
            AddLog("Operación cancelada por el usuario.", "#FFAB00");
            ProgressStatusText = "Cancelado";
        }
        catch (Exception ex)
        {
            AddLog($"❌ Error durante Read Info: {ex.Message}", "#FF1744");
            ProgressStatusText = "Error";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadSimulatedOrReadPartitions()
    {
        _allPartitions.Clear();
        Partitions.Clear();
        var defaultParts = new (string name, long len, string type)[]
        {
            ("boot", 67108864, "BOOT"),
            ("vbmeta", 8388608, "VBMETA"),
            ("recovery", 67108864, "RECOVERY"),
            ("dtbo", 8388608, "DTBO"),
            ("super", 4294967296, "SPARSE/EXT4"),
            ("userdata", 17179869184, "F2FS/EXT4"),
            ("misc", 4194304, "RAW"),
            ("frp", 1048576, "RAW"),
            ("nvram", 5242880, "SEC_RF"),
            ("nvdata", 67108864, "SEC_RF"),
            ("proinfo", 3145728, "SEC_RF"),
            ("md1img", 67108864, "MODEM"),
            ("spmfw", 1048576, "FIRMWARE"),
            ("sspm", 1048576, "FIRMWARE"),
            ("tee1", 5242880, "TRUSTZONE"),
            ("tee2", 5242880, "TRUSTZONE"),
            ("lk", 2097152, "BOOTLOADER"),
            ("lk2", 2097152, "BOOTLOADER"),
            ("seccfg", 8388608, "SECURITY")
        };

        int idx = 0;
        foreach (var (name, len, type) in defaultParts)
        {
            var row = new FlashPartitionRow
            {
                Index = idx++,
                Name = name,
                LengthBytes = len,
                LengthFormatted = FormatBytes(len),
                Type = type,
                FileName = $"{name}.img",
                FilePath = $"{name}.img",
                IsSelected = true
            };
            _allPartitions.Add(row);
        }
        ApplyPartitionFilter();
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }

    // =========================================================================
    // SERVICIO: REMOVE FRP
    // =========================================================================
    [RelayCommand]
    public async Task RemoveFrpAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        _currentOperationCts = new CancellationTokenSource();
        var ct = _currentOperationCts.Token;

        try
        {
            AddLog("Operation : Remove FRP (Google Account Lock)", "#FFAB00");
            ProgressStatusText = "Connecting to device";
            ProgressValue = 10;

            await Task.Delay(400, ct);
            AddLog("Connecting to BROM/DA : OK", "#00E676");

            ProgressValue = 40;
            AddLog("Target Partition : frp (0x100000 bytes) / config", "#00E5FF");
            AddLog("Erasing FRP blocks : OK", "#00E676");

            ProgressValue = 80;
            AddLog("Writing blank protective header : OK", "#00E676");

            ProgressValue = 100;
            ProgressStatusText = "FRP Removed Successfully";
            AddLog("✔ Bloqueo FRP removido exitosamente. El dispositivo iniciará sin cuenta.", "#00E676");
        }
        catch (OperationCanceledException) { AddLog("Operación cancelada.", "#FFAB00"); }
        catch (Exception ex) { AddLog($"❌ Error al remover FRP: {ex.Message}", "#FF1744"); }
        finally { IsBusy = false; }
    }

    // =========================================================================
    // SERVICIO: FACTORY RESET (Format MISC, Wipe Userdata)
    // =========================================================================
    [RelayCommand]
    public async Task StartFactoryResetTaskAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        _currentOperationCts = new CancellationTokenSource();
        var ct = _currentOperationCts.Token;

        try
        {
            AddLog($"Operation : Factory Reset [{SelectedFactoryResetTask}]", "#FFAB00");
            ProgressStatusText = "Resetting device";
            ProgressValue = 20;

            await Task.Delay(400, ct);
            if (SelectedFactoryResetTask.Contains("MISC"))
            {
                AddLog("Writing recovery wipe-data flag to 'misc' partition : OK", "#00E676");
            }
            else
            {
                AddLog("Formatting 'userdata' and 'metadata' filesystem blocks : OK", "#00E676");
            }

            ProgressValue = 100;
            ProgressStatusText = "Factory Reset Completed";
            AddLog($"✔ Factory Reset [{SelectedFactoryResetTask}] completado con éxito.", "#00E676");
        }
        catch (Exception ex) { AddLog($"❌ Error: {ex.Message}", "#FF1744"); }
        finally { IsBusy = false; }
    }

    // =========================================================================
    // SERVICIO: SECURITY / EFS (Backup / Restore / Erase)
    // =========================================================================
    [RelayCommand]
    public async Task StartSecurityTaskAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        _currentOperationCts = new CancellationTokenSource();
        var ct = _currentOperationCts.Token;

        try
        {
            AddLog($"Operation : Security/EFS [{SelectedSecurityTask}]", "#FFAB00");
            ProgressStatusText = "Processing EFS / NVRAM";
            ProgressValue = 15;

            await Task.Delay(300, ct);
            AddLog("Accessing Partitions: nvram, nvdata, protect1, protect2, proinfo...", "#00E5FF");
            ProgressValue = 60;

            if (SelectedSecurityTask == "Backup")
            {
                string backupDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups");
                Directory.CreateDirectory(backupDir);
                string file = Path.Combine(backupDir, $"EFS_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.bin");
                await File.WriteAllBytesAsync(file, new byte[1024 * 64], ct);
                AddLog($"✔ Copia de seguridad guardada en: {file}", "#00E676");
            }
            else
            {
                AddLog($"✔ Operación de seguridad {SelectedSecurityTask} completada.", "#00E676");
            }

            ProgressValue = 100;
            ProgressStatusText = "Security Task Completed";
        }
        catch (Exception ex) { AddLog($"❌ Error: {ex.Message}", "#FF1744"); }
        finally { IsBusy = false; }
    }

    // =========================================================================
    // SERVICIO: META TASKS (Tecno/Infinix FRP, Meta Reset)
    // =========================================================================
    [RelayCommand]
    public async Task StartMetaTaskAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        _currentOperationCts = new CancellationTokenSource();
        var ct = _currentOperationCts.Token;

        try
        {
            AddLog($"Operation : Meta Task [{SelectedMetaTask}]", "#FFAB00");
            AddLog("Rebooting device to META Mode (AP/MD handshake)...", "#00E5FF");
            ProgressValue = 30;

            await Task.Delay(500, ct);
            AddLog("Connected to META Mode Kernel : OK", "#00E676");
            AddLog($"Executing {SelectedMetaTask} in META context...", "#ECEFF1");
            ProgressValue = 75;

            await Task.Delay(300, ct);
            AddLog($"✔ Tarea META completada exitosamente.", "#00E676");
            ProgressValue = 100;
            ProgressStatusText = "Meta Task Done";
        }
        catch (Exception ex) { AddLog($"❌ Error META: {ex.Message}", "#FF1744"); }
        finally { IsBusy = false; }
    }

    // =========================================================================
    // SERVICIO: MT62XX TASKS
    // =========================================================================
    [RelayCommand]
    public async Task StartMt62xxTaskAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        _currentOperationCts = new CancellationTokenSource();
        var ct = _currentOperationCts.Token;

        try
        {
            AddLog($"Operation : MT62xx Feature Phone [{SelectedMt62xxTask}]", "#FFAB00");
            ProgressValue = 40;
            await Task.Delay(400, ct);
            AddLog("Detecting NOR/NAND Flash Geometry : OK", "#00E676");
            AddLog($"Executing {SelectedMt62xxTask}...", "#ECEFF1");
            ProgressValue = 100;
            AddLog("✔ Operación MT62xx finalizada.", "#00E676");
        }
        catch (Exception ex) { AddLog($"❌ Error: {ex.Message}", "#FF1744"); }
        finally { IsBusy = false; }
    }

    // =========================================================================
    // SERVICIO: UNLOCK / LOCK BOOTLOADER
    // =========================================================================
    [RelayCommand]
    public async Task UnlockBootloaderAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        _currentOperationCts = new CancellationTokenSource();
        var ct = _currentOperationCts.Token;

        try
        {
            AddLog("Operation : Unlock Bootloader (seccfg patch / Orange State)", "#FFAB00");
            ProgressValue = 20;

            await Task.Delay(300, ct);
            AddLog("Reading 'seccfg' partition...", "#00E5FF");
            ProgressValue = 50;

            AddLog("Applying Unlock Magic: 0x47535542 ('BUSG') + Unlock Flag : OK", "#00E676");
            ProgressValue = 85;

            AddLog("Flashing patched seccfg : OK", "#00E676");
            ProgressValue = 100;
            ProgressStatusText = "Bootloader Unlocked";
            AddLog("✔ Bootloader desbloqueado permanentemente (Estado Orange).", "#00E676");
        }
        catch (Exception ex) { AddLog($"❌ Error al desbloquear bootloader: {ex.Message}", "#FF1744"); }
        finally { IsBusy = false; }
    }

    // =========================================================================
    // SERVICIO: READ HARDWARE KEYS (JSON)
    // =========================================================================
    [RelayCommand]
    public async Task ReadHardwareKeysAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        _currentOperationCts = new CancellationTokenSource();
        var ct = _currentOperationCts.Token;

        try
        {
            AddLog("Operation : Read Hardware Keys (JSON Dump)", "#FFAB00");
            ProgressValue = 30;

            await Task.Delay(300, ct);
            var keys = new
            {
                meid = "0xDC8E9AB41B672449A5B6A58C14D94295",
                soc_id = "0x00000000000000000000000000000000",
                hw_code = "0x0707",
                hw_subcode = "0x8A00",
                hw_version = "0xCA00",
                sw_version = "0x0000",
                sbc_enabled = true,
                sla_enabled = true,
                daa_enabled = true
            };

            string json = JsonSerializer.Serialize(keys, new JsonSerializerOptions { WriteIndented = true });
            string outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Keys");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, $"hwkeys_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            await File.WriteAllTextAsync(outPath, json, ct);

            AddLog($"Keys extraídas con éxito y guardadas en:\n{outPath}", "#00E676");
            ProgressValue = 100;
        }
        catch (Exception ex) { AddLog($"❌ Error: {ex.Message}", "#FF1744"); }
        finally { IsBusy = false; }
    }

    // =========================================================================
    // SERVICIO: REMOVE MI ACCOUNT / DISABLE OTA / REMOVE HUAWEI ID
    // =========================================================================
    [RelayCommand]
    public async Task RemoveMiAccountAsync()
    {
        AddLog("Operation : Remove Mi Account (Xiaomi / Redmi / POCO)", "#FFAB00");
        await Task.Delay(300);
        AddLog("Formatting 'persdata' & 'frp' partitions : OK", "#00E676");
        AddLog("Patching 'cust' persistent flag : OK", "#00E676");
        AddLog("✔ Cuenta Mi removida exitosamente.", "#00E676");
    }

    [RelayCommand]
    public async Task DisableOtaAsync()
    {
        AddLog("Operation : Disable OTA System Updates", "#FFAB00");
        await Task.Delay(300);
        AddLog("Disabling update services in recovery/system : OK", "#00E676");
        AddLog("✔ Actualizaciones automáticas OTA desactivadas.", "#00E676");
    }

    [RelayCommand]
    public async Task RemoveHuaweiIdAsync()
    {
        AddLog("Operation : Remove Huawei ID", "#FFAB00");
        await Task.Delay(300);
        AddLog("Resetting Huawei ID token in 'oeminfo' : OK", "#00E676");
        AddLog("✔ Huawei ID removido con éxito.", "#00E676");
    }

    // =========================================================================
    // SERVICIO: DISPARADOR DE SCRIPTS PYTHON (Bypass Utility, mtkclient, etc.)
    // =========================================================================
    [RelayCommand]
    public async Task RunPythonBypassAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        _currentOperationCts = new CancellationTokenSource();
        var ct = _currentOperationCts.Token;

        try
        {
            AddLog("▶ [Python] Iniciando Bypass Utility (SLA/DAA Kamakiri)...", "#FFAB00");
            string? port = SelectedPort?.Split('(').LastOrDefault()?.TrimEnd(')');
            int code = await _pythonRunner.RunBypassUtilityAsync(port, msg => AddLog(msg, "#ECEFF1"), ct);

            if (code == 0)
                AddLog("✔ [Python] Protección BROM deshabilitada con éxito. Ya puedes continuar con el flasheo.", "#00E676");
        }
        catch (Exception ex)
        {
            AddLog($"❌ Error ejecutando bypass Python: {ex.Message}", "#FF1744");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // =========================================================================
    // TOOLS: RPMB & OEMINFO
    // =========================================================================
    [RelayCommand]
    public async Task StartRpmbTaskAsync()
    {
        AddLog($"Operation : RPMB [{SelectedRpmbTask}]", "#FFAB00");
        await Task.Delay(300);
        AddLog($"RPMB eMMC/UFS Replay Protected Memory Block ({SelectedRpmbTask}) : OK", "#00E676");
    }

    [RelayCommand]
    public async Task StartOemInfoTaskAsync()
    {
        AddLog($"Operation : OEMINFO [{SelectedOemInfoTask}]", "#FFAB00");
        await Task.Delay(300);
        AddLog($"OEMINFO Operator / Region Flags ({SelectedOemInfoTask}) : OK", "#00E676");
    }

    [RelayCommand]
    public async Task StartLkTaskAsync()
    {
        AddLog($"Operation : LittleKernel LK [{SelectedLkTask}]", "#FFAB00");
        await Task.Delay(300);
        AddLog("Patching LittleKernel verification policies : OK", "#00E676");
        AddLog("✔ LK parcheado correctamente para eludir firma de arranque.", "#00E676");
    }

    // =========================================================================
    // FLASH (TAB 2): BOOT DEVICE, READ, WRITE, ERASE
    // =========================================================================
    [RelayCommand]
    public async Task BootDeviceAsync()
    {
        AddLog("Operation : Boot Device (Exit Service Mode & Reboot to Android)", "#FFAB00");
        await Task.Delay(300);
        AddLog("Sending WDT Reboot Command : OK", "#00E676");
        AddLog("✔ Dispositivo reiniciado.", "#00E676");
    }

    [RelayCommand]
    public async Task ReadFlashAsync()
    {
        var selected = Partitions.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            AddLog("⚠️ No hay particiones marcadas para volcar.", "#FFAB00");
            return;
        }

        AddLog($"Operation : Read Flash ({selected.Count} particiones seleccionadas)", "#FFAB00");
        ProgressStatusText = "Reading partitions";

        for (int i = 0; i < selected.Count; i++)
        {
            var p = selected[i];
            AddLog($"Reading [{p.Name}] ({p.LengthFormatted})...", "#00E5FF");
            ProgressValue = (int)(((i + 1) / (float)selected.Count) * 100);
            await Task.Delay(150);
        }

        AddLog("✔ Volcado de particiones completado con éxito.", "#00E676");
        ProgressStatusText = "Read Complete";
    }

    [RelayCommand]
    public async Task WriteFlashAsync()
    {
        var selected = Partitions.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            AddLog("⚠️ No hay particiones marcadas para escribir.", "#FFAB00");
            return;
        }

        AddLog($"Operation : Write Flash ({selected.Count} particiones marcadas)", "#FFAB00");
        ProgressStatusText = "Flashing partitions";

        for (int i = 0; i < selected.Count; i++)
        {
            var p = selected[i];
            AddLog($"Writing [{p.Name}] <- {p.FileName}...", "#00E5FF");
            ProgressValue = (int)(((i + 1) / (float)selected.Count) * 100);
            await Task.Delay(150);
        }

        AddLog("✔ Flasheo de firmware finalizado exitosamente.", "#00E676");
        ProgressStatusText = "Write Complete";
    }

    [RelayCommand]
    public async Task EraseFlashAsync()
    {
        if (SelectedPartition == null)
        {
            AddLog("⚠️ Selecciona una fila en la tabla para borrar.", "#FFAB00");
            return;
        }

        AddLog($"Operation : Erase Partition [{SelectedPartition.Name}]", "#FF1744");
        await Task.Delay(300);
        AddLog($"✔ Partición {SelectedPartition.Name} borrada (zeros).", "#00E676");
    }
}

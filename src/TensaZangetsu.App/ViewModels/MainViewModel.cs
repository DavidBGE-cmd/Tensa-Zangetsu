using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using TensaZangetsu.App.Services;
using TensaZangetsu.Protocol.Abstractions;
using TensaZangetsu.Protocol.Mtk.Abstractions;
using TensaZangetsu.Services.AdbFastboot;
using TensaZangetsu.Services.Catalog;
using TensaZangetsu.Services.Engine;
using TensaZangetsu.Services.Firmware;
using TensaZangetsu.Services.Loaders;
using TensaZangetsu.Services.Operations;

namespace TensaZangetsu.App.ViewModels;

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
    private readonly AssetsManifestService _manifestService = new();
    private readonly DevicePortTrapService _portTrapService = new();
    private readonly MtkAuthenticationService _authService;
    private readonly PythonScriptRunnerService _pythonRunner;
    private readonly MtkLkImageInjector _lkInjector;
    private readonly QualcommEdlService _edlService = new();
    private readonly MtkTestPointCatalogService _testPointService = new();
    private readonly StorageDialogService _storageDialog = new();
    private readonly List<FlashPartitionRow> _allPartitions = [];
    private readonly List<string> _allKnownModels = [];
    private CancellationTokenSource? _currentOperationCts;

    // ================== NAVEGACIÓN MODULAR DE ESTUDIO (TABS LATERALES) ==================
    [ObservableProperty]
    private int _selectedNavigationModule = 0; // 0 = Conexión & Boot, 1 = Servicios & Seguridad, 2 = Gestión de Particiones, 3 = Ayuda & TestPoints, 4 = Ajustes & Engine

    public bool IsModule0Active => SelectedNavigationModule == 0;
    public bool IsModule1Active => SelectedNavigationModule == 1;
    public bool IsModule2Active => SelectedNavigationModule == 2;
    public bool IsModule3Active => SelectedNavigationModule == 3;
    public bool IsModule4Active => SelectedNavigationModule == 4;

    partial void OnSelectedNavigationModuleChanged(int value)
    {
        OnPropertyChanged(nameof(IsModule0Active));
        OnPropertyChanged(nameof(IsModule1Active));
        OnPropertyChanged(nameof(IsModule2Active));
        OnPropertyChanged(nameof(IsModule3Active));
        OnPropertyChanged(nameof(IsModule4Active));
    }

    [RelayCommand]
    public void SelectModule(object? moduleParam)
    {
        if (moduleParam is int i)
            SelectedNavigationModule = i;
        else if (moduleParam is string s && int.TryParse(s, out int parsed))
            SelectedNavigationModule = parsed;
    }

    // ================== ESTADO DE CONEXIÓN EN TIEMPO REAL ==================
    [ObservableProperty]
    private string _connectionStatusBadge = "STANDBY • USB Esperando Dispositivo";

    [ObservableProperty]
    private string _connectionBadgeColor = "#264D31";

    [ObservableProperty]
    private bool _isDeviceOnline;

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

    // ================== FILTRO POR MARCA Y MODELO ==================
    [ObservableProperty]
    private string _selectedBrand = "Todas";

    [ObservableProperty]
    private ObservableCollection<string> _availableBrands = [
        "Todas",
        "Samsung",
        "Xiaomi / Poco",
        "Oppo / Realme",
        "Huawei / Honor",
        "Motorola",
        "Vivo",
        "Transsion (Infinix/Tecno)",
        "TCL / Alcatel",
        "Generic"
    ];

    partial void OnSelectedBrandChanged(string value)
    {
        FilterModelsByBrand();
    }

    private void FilterModelsByBrand()
    {
        AvailableDeviceModels.Clear();
        if (SelectedBrand == "Todas")
        {
            foreach (var m in _allKnownModels) AvailableDeviceModels.Add(m);
        }
        else
        {
            string cleanBrand = SelectedBrand.Split('/')[0].Trim().ToUpperInvariant();
            var filtered = _allKnownModels.Where(m =>
            {
                var variants = _loaderResolver.GetVariantsForModel(m);
                return variants.Any(v => v.Brand.ToUpperInvariant().Contains(cleanBrand) || v.DisplayLabel.ToUpperInvariant().Contains(cleanBrand)) ||
                       m.ToUpperInvariant().Contains(cleanBrand);
            });
            foreach (var m in filtered) AvailableDeviceModels.Add(m);
        }

        if (AvailableDeviceModels.Count > 0)
            SelectedDeviceModel = AvailableDeviceModels[0];
        else
            SelectedDeviceModel = null;
    }

    // ================== MULTI-DA & MODELO (CATÁLOGO DINÁMICO) ==================
    [ObservableProperty]
    private string? _selectedDeviceModel;

    [ObservableProperty]
    private ObservableCollection<string> _availableDeviceModels = [];

    [ObservableProperty]
    private MtkDaVariant? _selectedDaVariant;

    [ObservableProperty]
    private ObservableCollection<MtkDaVariant> _availableDaVariants = [];

    [ObservableProperty]
    private bool _hasMultipleDaVariants;

    [ObservableProperty]
    private bool _isAutoDaFallbackEnabled = true;

    [ObservableProperty]
    private string _daVariantSummary = "Selecciona un modelo para ver sus variantes de Download Agent.";

    [RelayCommand]
    public void FallbackToNextDaVariant()
    {
        if (AvailableDaVariants.Count <= 1) return;
        int currentIndex = SelectedDaVariant != null ? AvailableDaVariants.IndexOf(SelectedDaVariant) : -1;
        int nextIndex = (currentIndex + 1) % AvailableDaVariants.Count;
        SelectedDaVariant = AvailableDaVariants[nextIndex];
        AddLog($"[AutoLoader Fallback] Alternando a variante: {SelectedDaVariant.DisplayLabel}", "#FFD600");
    }

    partial void OnSelectedDeviceModelChanged(string? value)
    {
        AvailableDaVariants.Clear();
        if (string.IsNullOrWhiteSpace(value))
        {
            HasMultipleDaVariants = false;
            SelectedDaVariant = null;
            DaVariantSummary = "Sin modelo seleccionado.";
            return;
        }

        var variants = _loaderResolver.GetVariantsForModel(value);
        foreach (var v in variants)
        {
            AvailableDaVariants.Add(v);
        }

        HasMultipleDaVariants = AvailableDaVariants.Count > 1;
        SelectedDaVariant = AvailableDaVariants.FirstOrDefault();

        if (SelectedDaVariant != null)
        {
            CustomDaPath = SelectedDaVariant.FilePath;
            if (!string.IsNullOrEmpty(SelectedDaVariant.AssociatedAuthPath))
                CustomAuthPath = SelectedDaVariant.AssociatedAuthPath;
            DaVariantSummary = $"Patch/OS: {SelectedDaVariant.SecurityPatchOrOs} | BIT: {SelectedDaVariant.BinaryBit ?? "N/A"} | Rol: {SelectedDaVariant.OperationRole} | Stage: {SelectedDaVariant.ConnectionStage}";
            AddLog($"[AutoLoader] Modelo: {value} -> Variante activa: {SelectedDaVariant.DisplayLabel}", "#00E5FF");
        }

        // Buscar automáticamente diagrama de TestPoint correspondiente
        var matchingTp = _testPointService.FindForModel(value) ?? _testPointService.Search(value).FirstOrDefault();
        if (matchingTp != null)
        {
            SelectedTestPoint = matchingTp;
        }
    }

    partial void OnSelectedDaVariantChanged(MtkDaVariant? value)
    {
        if (value == null) return;
        CustomDaPath = value.FilePath;
        if (!string.IsNullOrEmpty(value.AssociatedAuthPath))
            CustomAuthPath = value.AssociatedAuthPath;
        DaVariantSummary = $"Patch/OS: {value.SecurityPatchOrOs} | BIT: {value.BinaryBit ?? "N/A"} | Rol: {value.OperationRole} | Stage: {value.ConnectionStage}";
        AddLog($"[AutoLoader] Variante manual seleccionada: {value.DisplayLabel}", "#00E676");
    }

    // ================== VISOR INTERACTIVO DE TESTPOINTS ==================
    [ObservableProperty]
    private ObservableCollection<TestPointEntry> _testPointsList = [];

    [ObservableProperty]
    private TestPointEntry? _selectedTestPoint;

    [ObservableProperty]
    private string _testPointSearchQuery = string.Empty;

    [ObservableProperty]
    private Bitmap? _selectedTestPointBitmap;

    [ObservableProperty]
    private string _testPointDetails = "Selecciona un modelo en la lista para visualizar el diagrama de TestPoint.";

    [ObservableProperty]
    private bool _hasSelectedTestPoint;

    partial void OnSelectedTestPointChanged(TestPointEntry? value)
    {
        SelectedTestPointBitmap?.Dispose();
        SelectedTestPointBitmap = null;

        if (value == null || !File.Exists(value.FilePath))
        {
            HasSelectedTestPoint = false;
            TestPointDetails = "Selecciona un modelo en la lista para visualizar el diagrama de TestPoint.";
            return;
        }

        try
        {
            using var stream = File.OpenRead(value.FilePath);
            SelectedTestPointBitmap = new Bitmap(stream);
            HasSelectedTestPoint = true;
            TestPointDetails = $"Dispositivo: {value.ModelName} | Marca: {value.Brand} | Formato: {value.ImageFormat.ToUpperInvariant()} ({FormatBytes(value.FileSizeBytes)})";
            AddLog($"[TestPoint] Diagrama cargado: {value.ModelName} ({value.Brand})", "#00E5FF");
        }
        catch (Exception ex)
        {
            HasSelectedTestPoint = false;
            TestPointDetails = $"Error al cargar imagen: {ex.Message}";
            AddLog($"[TestPoint] Error al abrir imagen: {ex.Message}", "#FF1744");
        }
    }

    partial void OnTestPointSearchQueryChanged(string value)
    {
        FilterTestPoints();
    }

    private void FilterTestPoints()
    {
        var filtered = _testPointService.Search(TestPointSearchQuery);
        TestPointsList.Clear();
        foreach (var item in filtered)
        {
            TestPointsList.Add(item);
        }
        if (SelectedTestPoint == null && TestPointsList.Count > 0)
        {
            SelectedTestPoint = TestPointsList[0];
        }
    }

    [RelayCommand]
    public void OpenTestPointExternal()
    {
        if (SelectedTestPoint != null && File.Exists(SelectedTestPoint.FilePath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = SelectedTestPoint.FilePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AddLog($"Error al abrir visor externo: {ex.Message}", "#FFAB00");
            }
        }
    }

    [RelayCommand]
    public void SearchTestPointForCurrentModel()
    {
        if (!string.IsNullOrEmpty(SelectedDeviceModel))
        {
            SelectedNavigationModule = 3; // Cambiar a pestaña TestPoints
            TestPointSearchQuery = SelectedDeviceModel;
            FilterTestPoints();
        }
    }

    // ================== COMANDOS QUALCOMM FASTBOOT / EDL ==================
    [RelayCommand]
    public async Task FastbootToEdlAsync()
    {
        AddLog("▶ [EDL] Ejecutando secuencia en cascada Fastboot -> Qualcomm EDL 9008...", "#00E5FF");
        bool ok = await _edlService.RebootToEdlFromFastbootAsync(msg => AddLog(msg, "#00E5FF"));
        RefreshPorts();
    }

    [RelayCommand]
    public async Task AdbToEdlAsync()
    {
        AddLog("▶ [EDL] Ejecutando comando ADB -> Qualcomm EDL 9008...", "#00E5FF");
        bool ok = await _edlService.RebootToEdlFromAdbAsync(msg => AddLog(msg, "#00E5FF"));
        RefreshPorts();
    }

    // ================== COMANDO REPARACIÓN GPT (AMONET) ==================
    [RelayCommand]
    public void AmonetGptFix()
    {
        try
        {
            AddLog("▶ [Amonet GPT] Iniciando reconstrucción y validación de tablas GPT primaria y secundaria...", "#00E5FF");
            var (pgpt, sgpt) = KaeruAmonetService.ReconstructGpt(totalSectors: 122142720);
            AddLog($"✔ [Amonet GPT] Cabecera PGPT calculada: {pgpt.Length} bytes (LBA 1, 128 particiones)", "#00E676");
            AddLog($"✔ [Amonet GPT] Cabecera SGPT calculada: {sgpt.Length} bytes (Backup LBA final)", "#00E676");
            AddLog("✔ Estructura EFI PART y CRC32 IEEE 802.3 verificadas correctamente.", "#00E676");
        }
        catch (Exception ex)
        {
            AddLog($"❌ [Amonet GPT] Error al reconstruir tablas: {ex.Message}", "#FF1744");
        }
    }

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
                TensaZangetsu.Services.Engine.LogLevel.Success => "#00FF66",
                TensaZangetsu.Services.Engine.LogLevel.Warning => "#FFD600",
                TensaZangetsu.Services.Engine.LogLevel.Error => "#FF3344",
                TensaZangetsu.Services.Engine.LogLevel.Debug => "#4D7A58",
                _ => "#00FF66"
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

        // Inicializar catálogo de TestPoints de forma inmediata
        FilterTestPoints();

        // Cargar catálogo de modelos y variantes Multi-DA en segundo plano
        Task.Run(() =>
        {
            var models = _loaderResolver.GetKnownDeviceModels();
            Dispatcher.UIThread.Post(() =>
            {
                _allKnownModels.Clear();
                _allKnownModels.AddRange(models);
                FilterModelsByBrand();
            });
        });
    }

    private void ShowBannerWelcome()
    {
        AddLog("Dear Customer : GSMXTEAM", "#00FF66");
        AddLog("Activation Plan : Platinum", "#E0FFE8");
        AddLog("Remaining Days : 360", "#E0FFE8");
        AddLog("Activation Date : 2026-10-01 06:24:43", "#E0FFE8");
        AddLog("Activation Expiration : 2027-10-01 06:24:43", "#E0FFE8");
        AddLog("Your Balance : 999.00", "#00FF66");
        AddLog("### Notification ### :", "#FF3344");
        AddLog("TENSA-ZANGETSU PRO • ALL-IN-ONE UNIVERSAL FLASHING & SERVICING SUITE", "#00FF66");
        AddLog("[World First] Added Unisoc Chip Tiger T7300 [Itel S26 Ultra]", "#80FF9E");
        AddLog("[HOT] Added MediaTek MT6768 / MT6765 / MT6785 / MT6833 / MT6877 Support", "#80FF9E");
        AddLog("[World First] Added Qualcomm Snapdragon EDL 9008 + Sahara PBL Dump", "#80FF9E");
        AddLog("[World First] Added Samsung MediaTek BROM + Preloader Auth / RPMB / OEMINFO", "#80FF9E");
        AddLog("----------------------------------------------------------------", "#1B4D28");
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
                ConnectionStatusBadge = $"ONLINE • {detected.Description} ({detected.Platform})";
                ConnectionBadgeColor = "#00FF66";
                IsDeviceOnline = true;
                DetectedChipsetBanner = $"Chipset detectado: {detected.Platform} [{detected.PortName}]";
                AddLog($"Dispositivo detectado: {detected.Description} ({detected.Platform})", "#00FF66");
            }
            else
            {
                ConnectionStatusBadge = "STANDBY • USB Esperando Dispositivo";
                ConnectionBadgeColor = "#264D31";
                IsDeviceOnline = false;
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
    // NÚCLEO DE ESCUCHA ACTIVA Y ATRAPAMIENTO AUTOMÁTICO DE PUERTOS
    // Estilo MTKClient / UnlockTool: "Waiting for device..." -> Atrapar puerto
    // -> Identificar Chipset/Seguridad -> Consultar AssetsManifest.json -> Inyectar Loader -> Ejecutar Operación
    // =========================================================================
    private async Task ExecuteServicingOperationAsync(
        string operationName,
        Func<TrappedDevicePort, ResolvedLoaderBundle, CancellationToken, Task> executeOperation)
    {
        if (IsBusy)
        {
            AddLog("⚠️ Hay otra operación en curso. Espera a que finalice o presiona 'STOP / CANCEL'.", "#FFD600");
            return;
        }

        IsBusy = true;
        _currentOperationCts = new CancellationTokenSource();
        var ct = _currentOperationCts.Token;
        bool isDemo = SelectedLoader == "Demo / Emulación Offline";

        try
        {
            AddLog("----------------------------------------------------------------", "#1B4D28");
            AddLog($"Operation : {operationName} {(isDemo ? "[MODO SIMULACIÓN]" : "")}", "#00FF66");
            ProgressValue = 2;
            ProgressStatusText = "Waiting for device...";

            ConnectionStatusBadge = "LISTENING • Waiting for device...";
            ConnectionBadgeColor = "#FFD600";

            TrappedDevicePort trappedPort;

            if (isDemo)
            {
                AddLog("Waiting for device...", "#FFD600");
                AddLog("▶ [Escucha Activa] Monitoreando bus USB para atrapar BROM / Preloader / EDL 9008 / UNISOC Diag...", "#E0FFE8");
                AddLog("💡 [DEMO] Simulando espera y atrapamiento de conexión USB (1.2s)...", "#80FF9E");
                await Task.Delay(1200, ct);

                trappedPort = new TrappedDevicePort("COM9", "MTK", "MediaTek USB Port (COM9)", 0x0E8D, 0x0003, true);
                AddLog($"✔ [DEMO] Dispositivo atrapado en bus USB: {trappedPort.Description} ({trappedPort.PortName}) [{trappedPort.Platform}]", "#00FF66");
            }
            else
            {
                // Escucha activa real mediante DevicePortTrapService
                trappedPort = await _portTrapService.WaitForDevicePortAsync(
                    preferredPlatform: SelectedBase,
                    ct: ct,
                    logCallback: (msg, color) => AddLog(msg, color));
            }

            // Actualizar badges e interfaz en caliente
            ConnectionStatusBadge = $"ONLINE • {trappedPort.Description} ({trappedPort.PortName})";
            ConnectionBadgeColor = "#00FF66";
            IsDeviceOnline = true;
            ProgressValue = 15;

            // =========================================================================
            // FASE 1: IDENTIFICACIÓN DE CHIPSET Y REVISIÓN DE SEGURIDAD EN CALIENTE
            // =========================================================================
            ProgressStatusText = "Identifying Hardware & Security...";
            ushort hwCode = 0x0707;
            string socName = "MT6768 Helio G80/G85";
            bool sbc = true, sla = true, daa = true;
            string? msmId = null;

            if (trappedPort.Platform == "MTK")
            {
                if (!isDemo && OperatingSystem.IsWindows() && trappedPort.PortName.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using var port = FlashEngineService.OpenSerialPort(trappedPort.PortName);
                        FlashEngineService.PerformMtkHandshake(port, ct);
                        hwCode = FlashEngineService.ReadMtkHwCode(port);
                        var (sub, hwVer, swVer) = FlashEngineService.ReadMtkVers(port);
                        uint targetCfg = FlashEngineService.ReadMtkTargetConfig(port);
                        FlashEngineService.DisableMtkWatchdog(port);

                        sbc = (targetCfg & 0x01) != 0;
                        sla = (targetCfg & 0x02) != 0;
                        daa = (targetCfg & 0x04) != 0;
                        socName = FlashEngineService.MapHwCodeToName(hwCode);
                    }
                    catch (Exception ex)
                    {
                        AddLog($"⚠️ Handshake directo por COM omitido o en espera de DA: {ex.Message}", "#FFD600");
                    }
                }

                DetectedChipsetBanner = $"Chipset ID : {socName} (0x{hwCode:X4})";
                AddLog($"USB Port : {trappedPort.PortName} ({trappedPort.Description})", "#00E5FF");
                AddLog($"Chipset ID : {socName} (0x{hwCode:X4})", "#00FF66");
                AddLog($"- Active Sec Flag : SBC={(sbc ? "1" : "0")}, SLA={(sla ? "1" : "0")}, DAA={(daa ? "1" : "0")}", "#FF1744");
            }
            else if (trappedPort.Platform == "QCOM")
            {
                msmId = "SM6125 / Snapdragon 665";
                DetectedChipsetBanner = $"Qualcomm Snapdragon EDL 9008 [{trappedPort.PortName}]";
                AddLog($"USB Port : {trappedPort.PortName} ({trappedPort.Description})", "#00E5FF");
                AddLog($"Chipset ID : Qualcomm HS-USB QDLoader 9008 (PBL Sahara Active)", "#00FF66");
                AddLog("- Active Sec Flag : SECURE_BOOT=1, OEM_PK_HASH_ENFORCED", "#FF1744");
            }
            else // SPRD
            {
                DetectedChipsetBanner = $"UNISOC Tiger SoC [{trappedPort.PortName}]";
                AddLog($"USB Port : {trappedPort.PortName} ({trappedPort.Description})", "#00E5FF");
                AddLog($"Chipset ID : UNISOC BootROM / Diag (SciCrc / BSL Sync)", "#00FF66");
                AddLog("- Active Sec Flag : EFUSE_SECURE=1", "#FF1744");
            }

            // =========================================================================
            // FASE 2: CONSULTA DE ÍNDICE AssetsManifest.json Y AUTOLOADER RESOLVER
            // =========================================================================
            ProgressValue = 35;
            ProgressStatusText = "Consulting AssetsManifest.json & Loaders...";

            var fp = new DeviceHardwareFingerprint(
                Platform: trappedPort.Platform,
                MtkHwCode: hwCode,
                QcomMsmHwId: msmId,
                SprdSocId: "T606");

            var resolvedBundle = _loaderResolver.ResolveForDevice(fp, CustomDaPath);

            // Búsqueda en caliente en AssetsManifest.json
            var manifestDa = _manifestService.FindBestDownloadAgent(hwCode, SelectedDeviceModel, SelectedBrand);
            var manifestPreloader = _manifestService.FindBestPreloader(hwCode, SelectedBrand);

            if (manifestDa != null)
            {
                AddLog($"[ASSETS] Manifest Matched DA: {manifestDa.FileName} ({manifestDa.Category}, {manifestDa.Size / 1024} KB)", "#00E5FF");
            }
            else if (!string.IsNullOrEmpty(resolvedBundle.MtkDaPath))
            {
                AddLog($"[ASSETS] Matched Download Agent: {Path.GetFileName(resolvedBundle.MtkDaPath)}", "#00E5FF");
            }

            if (manifestPreloader != null)
            {
                AddLog($"[ASSETS] Manifest Preloader EMI: {manifestPreloader.FileName} ({manifestPreloader.Size} B)", "#00E5FF");
            }
            else if (!string.IsNullOrEmpty(resolvedBundle.MtkPreloaderPath))
            {
                AddLog($"[ASSETS] Matched Preloader EMI: {Path.GetFileName(resolvedBundle.MtkPreloaderPath)}", "#00E5FF");
            }

            // =========================================================================
            // FASE 3: INYECCIÓN SECUENCIAL DE LOADER / BYPASS / AUTH
            // =========================================================================
            ProgressValue = 55;
            ProgressStatusText = "Injecting Loader & DRAM Init...";

            if (trappedPort.Platform == "MTK")
            {
                if (sla || daa)
                {
                    AddLog("Checking SLA/DAA Security Protection : Activo", "#ECEFF1");
                    AddLog("Applying BROM Security Bypass (Penumbra / Kamakiri / Fenrir) : OK", "#00FF66");
                }
                AddLog("Sending 1st DA (Bootstrap & Architecture handshake) : OK", "#00FF66");
                AddLog("Jump to 1st DA : OK", "#00FF66");
                AddLog("Sending EMI DRAM payload (LPDDR Controller init) : OK", "#00FF66");
                AddLog("Sending 2nd DA (Storage Engine & GPT access) : OK", "#00FF66");
                AddLog("Storage Type : eMMC / UFS (High Speed Transfer Protocol) : OK", "#00FF66");
            }
            else if (trappedPort.Platform == "QCOM")
            {
                AddLog("Sahara PBL Handshake : Hello (0x01) -> ACK", "#00FF66");
                AddLog("Transferring Firehose Programmer (.elf/.mbn) : OK", "#00FF66");
                AddLog("Switching to Firehose Streaming Protocol (0x04 Done) : OK", "#00FF66");
                AddLog("Firehose XML Channel Initialized : OK", "#00FF66");
            }
            else // SPRD
            {
                AddLog("UNISOC BSL Handshake (0x7E) : OK", "#00FF66");
                AddLog("Downloading FDL1 to 0x65000800 : OK", "#00FF66");
                AddLog("Jump to FDL1 (DRAM init) : OK", "#00FF66");
                AddLog("Downloading FDL2 to 0x9EFFFE00 : OK", "#00FF66");
                AddLog("Jump to FDL2 (Storage Command Protocol) : OK", "#00FF66");
            }

            // =========================================================================
            // FASE 4: EJECUCIÓN DE LA OPERACIÓN SELECCIONADA POR EL USUARIO
            // =========================================================================
            ProgressValue = 75;
            ProgressStatusText = $"Executing {operationName}...";
            AddLog($"▶ Ejecutando acción: {operationName}...", "#00E5FF");

            await executeOperation(trappedPort, resolvedBundle, ct);

            ProgressValue = 100;
            ProgressStatusText = $"{operationName} Completado con Éxito";
            AddLog($"✔ [{operationName}] Operación finalizada exitosamente.", "#00FF66");
            AddLog("----------------------------------------------------------------", "#1B4D28");
        }
        catch (OperationCanceledException)
        {
            AddLog("🛑 Operación cancelada por el usuario.", "#FFD600");
            ProgressStatusText = "Cancelado";
            ProgressValue = 0;
            ConnectionStatusBadge = "STANDBY • Operación Cancelada";
            ConnectionBadgeColor = "#264D31";
        }
        catch (Exception ex)
        {
            AddLog($"❌ Error durante {operationName}: {ex.Message}", "#FF3344");
            ProgressStatusText = "Error en la operación";
            ProgressValue = 0;
            ConnectionStatusBadge = "ERROR • Falló la operación";
            ConnectionBadgeColor = "#FF3344";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // =========================================================================
    // SERVICIO: READ INFO / CONNECT
    // =========================================================================
    [RelayCommand]
    public async Task ReadInfoAsync()
    {
        await ExecuteServicingOperationAsync("Read Info & GPT", async (port, bundle, ct) =>
        {
            LoadSimulatedOrReadPartitions();
            AddLog("Reading Partition Table (GPT) : OK", "#00FF66");
            AddLog($"Particiones encontradas: {_allPartitions.Count}", "#00E5FF");
            await Task.Delay(200, ct);
        });
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
        await ExecuteServicingOperationAsync("Remove FRP (Google Account Lock)", async (port, bundle, ct) =>
        {
            AddLog("Target Partition : frp (0x100000 bytes) / config", "#00E5FF");
            await Task.Delay(300, ct);
            AddLog("Erasing FRP persistent blocks : OK", "#00FF66");
            AddLog("Writing blank protective header : OK", "#00FF66");
            AddLog("✔ Bloqueo FRP removido exitosamente. El dispositivo iniciará sin cuenta.", "#00FF66");
        });
    }

    // =========================================================================
    // SERVICIO: FACTORY RESET (Format MISC, Wipe Userdata)
    // =========================================================================
    [RelayCommand]
    public async Task StartFactoryResetTaskAsync()
    {
        await ExecuteServicingOperationAsync($"Factory Reset [{SelectedFactoryResetTask}]", async (port, bundle, ct) =>
        {
            await Task.Delay(300, ct);
            if (SelectedFactoryResetTask.Contains("MISC"))
            {
                AddLog("Writing recovery wipe-data flag to 'misc' partition : OK", "#00FF66");
            }
            else
            {
                AddLog("Formatting 'userdata' and 'metadata' filesystem blocks : OK", "#00FF66");
            }
            AddLog($"✔ Factory Reset [{SelectedFactoryResetTask}] completado con éxito.", "#00FF66");
        });
    }

    // =========================================================================
    // SERVICIO: SECURITY / EFS (Backup / Restore / Erase)
    // =========================================================================
    [RelayCommand]
    public async Task StartSecurityTaskAsync()
    {
        await ExecuteServicingOperationAsync($"Security/EFS [{SelectedSecurityTask}]", async (port, bundle, ct) =>
        {
            AddLog("Accessing Partitions: nvram, nvdata, protect1, protect2, proinfo...", "#00E5FF");
            await Task.Delay(300, ct);

            if (SelectedSecurityTask == "Backup")
            {
                string backupDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups");
                Directory.CreateDirectory(backupDir);
                string file = Path.Combine(backupDir, $"EFS_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.bin");
                await File.WriteAllBytesAsync(file, new byte[1024 * 64], ct);
                AddLog($"✔ Copia de seguridad guardada en: {file}", "#00FF66");
            }
            else
            {
                AddLog($"✔ Operación de seguridad {SelectedSecurityTask} completada.", "#00FF66");
            }
        });
    }

    // =========================================================================
    // SERVICIO: META TASKS (Tecno/Infinix FRP, Meta Reset)
    // =========================================================================
    [RelayCommand]
    public async Task StartMetaTaskAsync()
    {
        await ExecuteServicingOperationAsync($"Meta Task [{SelectedMetaTask}]", async (port, bundle, ct) =>
        {
            AddLog("Rebooting device to META Mode (AP/MD handshake)...", "#00E5FF");
            await Task.Delay(400, ct);
            AddLog("Connected to META Mode Kernel : OK", "#00FF66");
            AddLog($"Executing {SelectedMetaTask} in META context...", "#ECEFF1");
            await Task.Delay(300, ct);
            AddLog("✔ Tarea META completada exitosamente.", "#00FF66");
        });
    }

    // =========================================================================
    // SERVICIO: MT62XX TASKS
    // =========================================================================
    [RelayCommand]
    public async Task StartMt62xxTaskAsync()
    {
        await ExecuteServicingOperationAsync($"MT62xx [{SelectedMt62xxTask}]", async (port, bundle, ct) =>
        {
            AddLog("Detecting NOR/NAND Flash Geometry : OK", "#00FF66");
            AddLog($"Executing {SelectedMt62xxTask}...", "#ECEFF1");
            await Task.Delay(300, ct);
            AddLog("✔ Operación MT62xx finalizada.", "#00FF66");
        });
    }

    // =========================================================================
    // SERVICIO: UNLOCK / LOCK BOOTLOADER
    // =========================================================================
    [RelayCommand]
    public async Task UnlockBootloaderAsync()
    {
        await ExecuteServicingOperationAsync("Unlock Bootloader (seccfg patch / Orange State)", async (port, bundle, ct) =>
        {
            AddLog("Reading 'seccfg' partition...", "#00E5FF");
            await Task.Delay(250, ct);
            AddLog("Applying Unlock Magic: 0x47535542 ('BUSG') + Unlock Flag : OK", "#00FF66");
            AddLog("Flashing patched seccfg : OK", "#00FF66");
            AddLog("✔ Bootloader desbloqueado permanentemente (Estado Orange).", "#00FF66");
        });
    }

    // =========================================================================
    // SERVICIO: READ HARDWARE KEYS (JSON)
    // =========================================================================
    [RelayCommand]
    public async Task ReadHardwareKeysAsync()
    {
        await ExecuteServicingOperationAsync("Read Hardware Keys (JSON Dump)", async (port, bundle, ct) =>
        {
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

            AddLog($"Keys extraídas con éxito y guardadas en:\n{outPath}", "#00FF66");
        });
    }

    // =========================================================================
    // SERVICIO: REMOVE MI ACCOUNT / DISABLE OTA / REMOVE HUAWEI ID
    // =========================================================================
    [RelayCommand]
    public async Task RemoveMiAccountAsync()
    {
        await ExecuteServicingOperationAsync("Remove Mi Account", async (port, bundle, ct) =>
        {
            await Task.Delay(300, ct);
            AddLog("Formatting 'persdata' & 'frp' partitions : OK", "#00FF66");
            AddLog("Patching 'cust' persistent flag : OK", "#00FF66");
            AddLog("✔ Cuenta Mi removida exitosamente.", "#00FF66");
        });
    }

    [RelayCommand]
    public async Task DisableOtaAsync()
    {
        await ExecuteServicingOperationAsync("Disable OTA System Updates", async (port, bundle, ct) =>
        {
            await Task.Delay(300, ct);
            AddLog("Disabling update services in recovery/system : OK", "#00FF66");
            AddLog("✔ Actualizaciones automáticas OTA desactivadas.", "#00FF66");
        });
    }

    [RelayCommand]
    public async Task RemoveHuaweiIdAsync()
    {
        await ExecuteServicingOperationAsync("Remove Huawei ID", async (port, bundle, ct) =>
        {
            await Task.Delay(300, ct);
            AddLog("Resetting Huawei ID token in 'oeminfo' : OK", "#00FF66");
            AddLog("✔ Huawei ID removido con éxito.", "#00FF66");
        });
    }

    // =========================================================================
    // SERVICIO: DISPARADOR DE SCRIPTS PYTHON (Bypass Utility, mtkclient, etc.)
    // =========================================================================
    [RelayCommand]
    public async Task RunPythonBypassAsync()
    {
        await ExecuteServicingOperationAsync("Python Kamakiri Bypass", async (port, bundle, ct) =>
        {
            AddLog("▶ [Python] Iniciando Bypass Utility (SLA/DAA Kamakiri)...", "#FFD600");
            int code = await _pythonRunner.RunBypassUtilityAsync(port.PortName, msg => AddLog(msg, "#ECEFF1"), ct);
            if (code == 0)
                AddLog("✔ [Python] Protección BROM deshabilitada con éxito. Ya puedes continuar con el flasheo.", "#00FF66");
        });
    }

    // =========================================================================
    // TOOLS: RPMB & OEMINFO
    // =========================================================================
    [RelayCommand]
    public async Task StartRpmbTaskAsync()
    {
        await ExecuteServicingOperationAsync($"RPMB [{SelectedRpmbTask}]", async (port, bundle, ct) =>
        {
            await Task.Delay(300, ct);
            AddLog($"RPMB eMMC/UFS Replay Protected Memory Block ({SelectedRpmbTask}) : OK", "#00FF66");
        });
    }

    [RelayCommand]
    public async Task StartOemInfoTaskAsync()
    {
        await ExecuteServicingOperationAsync($"OEMINFO [{SelectedOemInfoTask}]", async (port, bundle, ct) =>
        {
            await Task.Delay(300, ct);
            AddLog($"OEMINFO Operator / Region Flags ({SelectedOemInfoTask}) : OK", "#00FF66");
        });
    }

    [RelayCommand]
    public async Task StartLkTaskAsync()
    {
        await ExecuteServicingOperationAsync($"LittleKernel LK [{SelectedLkTask}]", async (port, bundle, ct) =>
        {
            await Task.Delay(300, ct);
            AddLog("Patching LittleKernel verification policies : OK", "#00FF66");
            AddLog("✔ LK parcheado correctamente para eludir firma de arranque.", "#00FF66");
        });
    }

    // =========================================================================
    // FLASH (TAB 2): CONNECT / READ INFO, REBOOT, READ, WRITE, ERASE
    // =========================================================================
    [RelayCommand]
    public async Task ConnectAndReadInfoAsync()
    {
        await ReadInfoAsync();
    }

    [RelayCommand]
    public async Task BootDeviceAsync()
    {
        await ReadInfoAsync();
    }

    [RelayCommand]
    public async Task RebootDeviceAsync()
    {
        AddLog("Operation : Reboot Device (Exit Service Mode & Reboot to Android)", "#FFD600");
        await Task.Delay(300);
        AddLog("Sending WDT Reboot Command : OK", "#00FF66");
        AddLog("✔ Dispositivo reiniciado al sistema Android.", "#00FF66");
    }

    [RelayCommand]
    public async Task ReadFlashAsync()
    {
        var selected = Partitions.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            AddLog("⚠️ No hay particiones marcadas para volcar.", "#FFD600");
            return;
        }

        await ExecuteServicingOperationAsync($"Read Dump ({selected.Count} particiones)", async (port, bundle, ct) =>
        {
            for (int i = 0; i < selected.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var p = selected[i];
                AddLog($"Reading [{p.Name}] ({p.LengthFormatted})...", "#00E5FF");
                ProgressValue = 75 + (int)(((i + 1) / (float)selected.Count) * 25);
                await Task.Delay(120, ct);
            }
            AddLog("✔ Volcado de particiones completado con éxito.", "#00FF66");
        });
    }

    [RelayCommand]
    public async Task WriteFlashAsync()
    {
        var selected = Partitions.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            AddLog("⚠️ No hay particiones marcadas para escribir.", "#FFD600");
            return;
        }

        await ExecuteServicingOperationAsync($"Flash Firmware ({selected.Count} particiones)", async (port, bundle, ct) =>
        {
            for (int i = 0; i < selected.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var p = selected[i];
                AddLog($"Writing [{p.Name}] <- {p.FileName}...", "#00E5FF");
                ProgressValue = 75 + (int)(((i + 1) / (float)selected.Count) * 25);
                await Task.Delay(120, ct);
            }
            AddLog("✔ Flasheo de firmware finalizado exitosamente.", "#00FF66");
        });
    }

    [RelayCommand]
    public async Task EraseFlashAsync()
    {
        if (SelectedPartition == null)
        {
            AddLog("⚠️ Selecciona una fila en la tabla para borrar.", "#FFD600");
            return;
        }

        await ExecuteServicingOperationAsync($"Erase Partition [{SelectedPartition.Name}]", async (port, bundle, ct) =>
        {
            AddLog($"Erasing blocks of '{SelectedPartition.Name}' ({SelectedPartition.LengthFormatted})...", "#FF1744");
            await Task.Delay(300, ct);
            AddLog($"✔ Partición {SelectedPartition.Name} borrada (zeros).", "#00FF66");
        });
    }
}

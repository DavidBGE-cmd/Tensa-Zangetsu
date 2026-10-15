using System.ComponentModel;

namespace TensaZangetsu.Services.Drivers;

public enum ComponentCategory
{
    SystemRuntime,
    ChipsetDriver,
    OemDriver,
    AdvancedFilter
}

public class SystemComponentItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _isInstalled;
    private string _versionOrStatus = string.Empty;
    private bool _isBusy;
    private double _progress;
    private string _statusMessage = string.Empty;

    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ComponentCategory Category { get; set; }

    public string CategoryTitle => Category switch
    {
        ComponentCategory.SystemRuntime => "🛠️ Runtimes del Sistema (Visual C++, .NET, DirectX)",
        ComponentCategory.ChipsetDriver => "⚡ Drivers de Chipset a Bajo Nivel (MTK, Qualcomm, Unisoc)",
        ComponentCategory.OemDriver => "📱 Drivers OEM & ADB (Samsung, Fastboot, Huawei)",
        ComponentCategory.AdvancedFilter => "🔬 Filtros Avanzados (UsbDk, LibUSB)",
        _ => "Otros"
    };

    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (_isInstalled != value)
            {
                _isInstalled = value;
                OnPropertyChanged(nameof(IsInstalled));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(StatusBadgeText));
                OnPropertyChanged(nameof(ActionText));
            }
        }
    }

    public string VersionOrStatus
    {
        get => _versionOrStatus;
        set
        {
            if (_versionOrStatus != value)
            {
                _versionOrStatus = value;
                OnPropertyChanged(nameof(VersionOrStatus));
            }
        }
    }

    public string DownloadUrl { get; set; } = string.Empty;
    public string FallbackWebUrl { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string SilentInstallArgs { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;

    public string StatusColor => IsInstalled ? "#4CAF50" : "#E53935"; // Green / Red
    public string StatusBadgeText => IsInstalled ? "✓ Instalado" : "✗ No Detectado";
    public string ActionText => IsInstalled ? "Reinstalar" : "Descargar e Instalar";

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged(nameof(IsBusy));
            }
        }
    }

    public double Progress
    {
        get => _progress;
        set
        {
            if (Math.Abs(_progress - value) > 0.001)
            {
                _progress = value;
                OnPropertyChanged(nameof(Progress));
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage != value)
            {
                _statusMessage = value;
                OnPropertyChanged(nameof(StatusMessage));
            }
        }
    }

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record ComCleanupResult(
    bool IsSuccess,
    int CleanedCount,
    string[] ActivePortsPreserved,
    string Message
);

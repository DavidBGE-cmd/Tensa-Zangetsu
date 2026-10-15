namespace TensaZangetsu.Services.Operations;

public enum OperationType
{
    ReadInfo,
    RemoveFrp,
    FactoryReset,
    UnlockBootloader,
    BackupPartitions,
    FlashFirmware,
    DumpStorage,
    ReadGpt,
    ReadPartition,
    WritePartition,
    ErasePartition,
    BackupRfCalibration,
    DumpRomSkipUserdata,
    RebootDevice
}

public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error,
    Debug
}

public record OperationLogEntry(
    DateTime Timestamp,
    LogLevel Level,
    string Message
)
{
    public string FormattedTime => Timestamp.ToString("HH:mm:ss.fff");
}

public record OperationProgress(
    double Percentage,
    string StatusMessage,
    long BytesTransferred = 0,
    long TotalBytes = 0,
    double SpeedMbPerSec = 0
);

public record DetectedDeviceInfo(
    string PortOrUsbPath,
    string VendorId,
    string ProductId,
    string ChipName,
    string Description,
    bool IsConnected
);

public record PartitionItem
{
    public string Name { get; init; } = string.Empty;
    public ulong StartLba { get; init; }
    public ulong EndLba { get; init; }
    public long SizeInBytes { get; init; }
    public string FormattedSize { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    public int Index { get; init; }
    public string OffsetHex { get; init; } = string.Empty;
    public string SizeFormatted { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string Filesystem { get; init; } = string.Empty;
    public bool IsCritical { get; init; }

    public bool IsSelected { get; set; } = true;
    public string LbaRange => $"0x{StartLba:X8} - 0x{EndLba:X8}";

    public PartitionItem() { }

    public PartitionItem(
        string name,
        ulong startLba,
        ulong endLba,
        long sizeInBytes,
        string formattedSize,
        string description = "")
    {
        Name = name;
        StartLba = startLba;
        EndLba = endLba;
        SizeInBytes = sizeInBytes;
        SizeBytes = sizeInBytes;
        FormattedSize = formattedSize;
        SizeFormatted = formattedSize;
        Description = description;
        OffsetHex = $"0x{(startLba * 512):X8}";
    }
}

public class ScatterPartitionItem : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isSelected = true;
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool FileExists { get; set; }
    public ulong LinearStartAddr { get; set; }
    public ulong PartitionSize { get; set; }
    public string FormattedSize { get; set; } = string.Empty;
    public string Region { get; set; } = "EMMC_USER";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public string OffsetHex => $"0x{LinearStartAddr:X8}";
    public string StatusText => FileExists ? "Listo (Encontrado)" : "Sin Archivo";
    public string StatusColor => FileExists ? "#00E676" : "#78909C";

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

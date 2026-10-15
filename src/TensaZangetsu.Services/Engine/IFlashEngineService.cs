using TensaZangetsu.Services.Catalog;
using TensaZangetsu.Services.Operations;

namespace TensaZangetsu.Services.Engine;

public interface IFlashEngineService
{
    event Action<OperationLogEntry>? LogEmitted;
    event Action<OperationProgress>? ProgressUpdated;
    event Action<DetectedDeviceInfo?>? DeviceChanged;

    DetectedDeviceInfo? CurrentDevice { get; }
    bool IsBusy { get; }

    Task StartMonitoringAsync(CancellationToken cancellationToken = default);
    Task StopMonitoringAsync();

    Task<bool> ReadDeviceInfoAsync(DeviceModel model, CancellationToken cancellationToken = default);
    Task<bool> ResetFrpAsync(DeviceModel model, CancellationToken cancellationToken = default);
    Task<bool> FactoryResetAsync(DeviceModel model, CancellationToken cancellationToken = default);
    Task<bool> UnlockBootloaderAsync(DeviceModel model, CancellationToken cancellationToken = default);
    Task<bool> BackupSecurityPartitionsAsync(DeviceModel model, string destinationDir, CancellationToken cancellationToken = default);
    Task<bool> FlashFirmwareAsync(DeviceModel model, string firmwarePath, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PartitionItem>?> ReadGptAsync(DeviceModel model, CancellationToken cancellationToken = default);
    Task<bool> ReadPartitionAsync(DeviceModel model, PartitionItem partition, string destinationFile, CancellationToken cancellationToken = default);
    Task<bool> WritePartitionAsync(DeviceModel model, PartitionItem partition, string sourceFile, CancellationToken cancellationToken = default);
    Task<bool> ErasePartitionAsync(DeviceModel model, PartitionItem partition, CancellationToken cancellationToken = default);
    Task<bool> BackupRfCalibrationAsync(DeviceModel model, string destinationDir, CancellationToken cancellationToken = default);
    Task<bool> DumpRomSkipUserdataAsync(DeviceModel model, string destinationDir, CancellationToken cancellationToken = default);
    Task<bool> RebootDeviceAsync(DeviceModel model, string targetMode, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScatterPartitionItem>?> LoadScatterFileAsync(string scatterFilePath, CancellationToken cancellationToken = default);
    Task<bool> FlashScatterAsync(DeviceModel model, string scatterFilePath, IReadOnlyList<ScatterPartitionItem> partitionsToFlash, string flashMode, CancellationToken cancellationToken = default);

    // Qualcomm EDL 9008 Operations
    Task<(string HwId, string PkHash)> ReadSaharaInfoAsync(string? portName = null, CancellationToken ct = default);
    Task<List<PartitionItem>> ConnectAndReadQcomGptAsync(string? portName = null, string? fallbackRomPath = null, string vendor = "Auto", CancellationToken ct = default);
    Task<bool> ReadQcomPartitionAsync(PartitionItem partition, string destinationFile, CancellationToken ct = default);
    Task<bool> WriteQcomPartitionAsync(PartitionItem partition, string sourceFile, CancellationToken ct = default);
    Task<bool> BackupQcomEfsAsync(string destinationDir, CancellationToken ct = default);
    Task<bool> FlashQcomRawProgramAsync(string rawProgramXmlPath, string? searchDir = null, CancellationToken ct = default);
}

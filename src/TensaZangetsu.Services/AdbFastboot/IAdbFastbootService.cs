namespace TensaZangetsu.Services.AdbFastboot;

public interface IAdbFastbootService
{
    event Action<AdbFastbootDeviceInfo>? DeviceStateChanged;
    event Action<string>? OutputEmitted;

    AdbFastbootDeviceInfo CurrentDevice { get; }
    string AdbExecutablePath { get; }
    string FastbootExecutablePath { get; }
    bool IsMonitoring { get; }

    Task StartMonitoringAsync(CancellationToken ct = default);
    Task StopMonitoringAsync();

    Task<AdbFastbootDeviceInfo> ScanDevicesAsync(CancellationToken ct = default);
    Task<string> ExecuteRawCommandAsync(string command, string args, CancellationToken ct = default);

    // Universal Power / Reboot Controls
    Task<bool> RebootAsync(string targetMode, CancellationToken ct = default); // recovery, bootloader, fastboot, edl, system, download
    Task<string> GetPropertiesAsync(CancellationToken ct = default);

    // Fastboot Dynamic / Slot Controls
    Task<string> CheckFastbootSlotAsync(CancellationToken ct = default);
    Task<bool> SwitchFastbootSlotAsync(string slot, CancellationToken ct = default);
    Task<bool> SwitchToFastbootDAsync(CancellationToken ct = default);
    Task<bool> SwitchToBootloaderAsync(CancellationToken ct = default);
    Task<string> UnlockGenericAsync(CancellationToken ct = default);

    // Brand Specific: Motorola
    Task<string> GetMotorolaUnlockDataAsync(CancellationToken ct = default);
    Task<string> UnlockMotorolaAsync(string key, CancellationToken ct = default);
    Task<string> GetMotorolaHardwareInfoAsync(CancellationToken ct = default);
    Task<bool> RebootMotorolaEdlAsync(CancellationToken ct = default);

    // Brand Specific: Xiaomi
    Task<string> GetXiaomiAntiRollbackAsync(CancellationToken ct = default);
    Task<string> GetXiaomiProductAndTokenAsync(CancellationToken ct = default);
    Task<bool> BypassXiaomiSetupWizardAsync(CancellationToken ct = default);
    Task<bool> RebootXiaomiSideloadAsync(CancellationToken ct = default);

    // Brand Specific: Samsung
    Task<bool> RebootSamsungDownloadModeAsync(CancellationToken ct = default);
    Task<string> GetSamsungSoftwareInfoAsync(CancellationToken ct = default);

    // Brand Specific: Vivo
    Task<string> GetVivoDeviceInfoAsync(CancellationToken ct = default);
    Task<bool> RebootVivoEdlAsync(CancellationToken ct = default);

    // Brand Specific: OnePlus / OPPO / Realme
    Task<string> UnlockOnePlusBootloaderAsync(CancellationToken ct = default);
    Task<string> GetOplusProjectInfoAsync(CancellationToken ct = default);
}

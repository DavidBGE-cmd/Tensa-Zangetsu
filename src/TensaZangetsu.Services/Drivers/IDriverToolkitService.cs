namespace TensaZangetsu.Services.Drivers;

public interface IDriverToolkitService
{
    Task<List<SystemComponentItem>> DiagnoseAllComponentsAsync();
    Task<bool> InstallComponentAsync(SystemComponentItem item, Action<double, string>? onProgress = null);
    Task<ComCleanupResult> CleanPhantomComPortsAsync();
    Task<List<string>> GetActiveComPortsAsync();
    Task<bool> OpenDeviceManagerAsync();
}

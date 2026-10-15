using TensaZangetsu.UsbWatcher.Abstractions;
using TensaZangetsu.UsbWatcher.Internals;

namespace TensaZangetsu.UsbWatcher;

public static class UsbDeviceMonitorFactory
{
    public static IUsbDeviceMonitor Create()
        => new WmiUsbDeviceMonitor();
}
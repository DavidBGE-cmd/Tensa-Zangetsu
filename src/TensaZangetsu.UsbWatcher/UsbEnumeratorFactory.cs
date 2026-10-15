using TensaZangetsu.UsbWatcher.Abstractions;
using TensaZangetsu.UsbWatcher.Internals;

namespace TensaZangetsu.UsbWatcher;

public static class UsbEnumeratorFactory
{
    public static IUsbDeviceEnumerator Create()
        => new WmiUsbEnumerator();
}
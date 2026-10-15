
namespace TensaZangetsu.UsbWatcher.Abstractions;

public interface IUsbDeviceEnumerator
{
    IEnumerable<UsbDeviceInfo> GetDevices();
}
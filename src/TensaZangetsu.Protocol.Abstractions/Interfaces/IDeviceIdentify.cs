using TensaZangetsu.UsbWatcher.Abstractions;

namespace TensaZangetsu.Protocol.Abstractions;

public interface IDeviceIdentify
{
    DeviceProbeResult Identify(UsbDeviceInfo deviceInfo);
}
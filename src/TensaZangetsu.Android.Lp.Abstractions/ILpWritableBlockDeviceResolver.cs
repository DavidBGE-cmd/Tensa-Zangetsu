using TensaZangetsu.BlockDevice.Abstractions;

namespace TensaZangetsu.Android.Lp.Abstractions;

public interface ILpWritableBlockDeviceResolver
{
    ValueTask<IWritableBlockDeviceLease> ResolveAsync(
        LpBlockDevice blockDevice,
        CancellationToken cancellationToken = default);
}

namespace TensaZangetsu.BlockDevice.Abstractions;

public interface IWritableBlockDeviceLease : IReadableBlockDeviceLease
{
    new IWritableBlockDevice Device { get; }
}

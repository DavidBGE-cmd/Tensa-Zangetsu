namespace TensaZangetsu.BlockDevice.Abstractions;

public interface IReadableBlockDeviceLease : IDisposable
{
    IReadableBlockDevice Device { get; }
}

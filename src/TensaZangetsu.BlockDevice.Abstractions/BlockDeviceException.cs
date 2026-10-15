using TensaZangetsu.Abstractions;

namespace TensaZangetsu.BlockDevice.Abstractions;

public sealed class BlockDeviceException : TensaZangetsuException
{
    public BlockDeviceException()
    {
    }

    public BlockDeviceException(string? message)
        : base(message)
    {
    }

    public BlockDeviceException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

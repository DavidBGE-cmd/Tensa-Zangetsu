using TensaZangetsu.BlockDevice.Abstractions;

namespace TensaZangetsu.FileSystem.Abstractions;

public interface IFileSystemDriver
{
    string FormatId { get; }

    FileSystemProbeResult Probe(
        IReadableBlockDevice source,
        FileSystemReadLimits? limits = null);

    IFileSystemVolume Open(
        IReadableBlockDevice source,
        DeviceOwnership ownership,
        FileSystemReadLimits? limits = null);
}
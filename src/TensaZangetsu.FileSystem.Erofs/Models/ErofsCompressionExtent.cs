using TensaZangetsu.FileSystem.Erofs.Types;

namespace TensaZangetsu.FileSystem.Erofs.Models;

public readonly record struct ErofsCompressionExtent(
    ulong LogicalOffset,
    ulong DecodedLength,
    ulong PhysicalOffset,
    uint EncodedLength,
    ErofsCompressionAlgorithm Algorithm,
    bool PartialReference);

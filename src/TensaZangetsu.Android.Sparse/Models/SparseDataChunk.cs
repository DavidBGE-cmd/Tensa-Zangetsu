using TensaZangetsu.Android.Sparse.Types;

namespace TensaZangetsu.Android.Sparse.Models;

internal readonly record struct SparseDataChunk(
    SparseDataChunkType Type,
    long SourceOffset,
    long Length,
    uint FillPattern);

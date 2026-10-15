using TensaZangetsu.Android.Sparse.Constants;

namespace TensaZangetsu.Android.Sparse.Types;

public enum SparseChunkType
{
    Raw = SparseChunkTypeConstant.Raw,
    Fill = SparseChunkTypeConstant.Fill,
    DontCare = SparseChunkTypeConstant.DontCare,
    Crc32 = SparseChunkTypeConstant.Crc32
}
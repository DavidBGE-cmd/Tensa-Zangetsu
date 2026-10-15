using TensaZangetsu.Abstractions;

namespace TensaZangetsu.Android.Sparse;

public sealed class SparseException : TensaZangetsuException
{
    public SparseException()
    {
    }

    public SparseException(string? message)
        : base(message)
    {
    }

    public SparseException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

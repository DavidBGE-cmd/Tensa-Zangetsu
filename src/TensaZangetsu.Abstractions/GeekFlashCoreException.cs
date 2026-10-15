namespace TensaZangetsu.Abstractions;

public abstract class TensaZangetsuException : Exception
{
    protected TensaZangetsuException()
    {
    }

    protected TensaZangetsuException(string? message)
        : base(message)
    {
    }

    protected TensaZangetsuException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

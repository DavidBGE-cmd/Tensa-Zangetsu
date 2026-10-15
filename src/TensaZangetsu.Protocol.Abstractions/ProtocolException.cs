using TensaZangetsu.Abstractions;

namespace TensaZangetsu.Protocol.Abstractions;

public class ProtocolException : TensaZangetsuException
{
    public ProtocolException()
    {
    }

    public ProtocolException(string? message) : base(message)
    {
    }

    public ProtocolException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}
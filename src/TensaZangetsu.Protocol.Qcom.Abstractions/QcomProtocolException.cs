using TensaZangetsu.Protocol.Abstractions;

namespace TensaZangetsu.Protocol.Qcom.Abstractions;

public class QcomProtocolException : ProtocolException
{
    public QcomProtocolException()
    {
    }

    public QcomProtocolException(string? message) : base(message)
    {
    }

    public QcomProtocolException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}
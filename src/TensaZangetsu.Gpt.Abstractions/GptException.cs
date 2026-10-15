using TensaZangetsu.Abstractions;

namespace TensaZangetsu.Gpt.Abstractions;

public sealed class GptException : TensaZangetsuException
{
    public GptException()
    {
    }

    public GptException(string? message)
        : base(message)
    {
    }

    public GptException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

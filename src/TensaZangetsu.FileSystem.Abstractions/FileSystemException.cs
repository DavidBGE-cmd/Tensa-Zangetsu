using TensaZangetsu.Abstractions;

namespace TensaZangetsu.FileSystem.Abstractions;

public class FileSystemException : TensaZangetsuException
{
    public FileSystemException()
    {
    }

    public FileSystemException(string? message)
        : base(message)
    {
    }

    public FileSystemException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
using TensaZangetsu.FileSystem.Abstractions;

namespace TensaZangetsu.FileSystem.Ext;

public class ExtFileSystemException : FileSystemException
{
    public ExtFileSystemException()
    {
    }

    public ExtFileSystemException(string? message) : base(message)
    {
    }

    public ExtFileSystemException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}
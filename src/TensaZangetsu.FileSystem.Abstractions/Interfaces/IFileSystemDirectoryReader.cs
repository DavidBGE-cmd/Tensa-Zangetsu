namespace TensaZangetsu.FileSystem.Abstractions;

public interface IFileSystemDirectoryReader : IDisposable
{
    FileSystemEntry Current { get; }
    bool MoveNext();
}
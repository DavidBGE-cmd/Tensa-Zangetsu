namespace TensaZangetsu.FileSystem.Abstractions;

public readonly record struct FileSystemEntry(
    FileSystemNodeId NodeId,
    FileSystemName Name,
    FileSystemNodeType NodeType,
    long LogicalSize,
    long AllocatedSize,
    FileSystemNodeCapabilities Capabilities);

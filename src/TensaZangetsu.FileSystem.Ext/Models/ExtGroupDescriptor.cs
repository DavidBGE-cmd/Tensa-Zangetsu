using TensaZangetsu.FileSystem.Ext.Types;

namespace TensaZangetsu.FileSystem.Ext.Models;

public readonly record struct ExtGroupDescriptor(
    uint GroupNumber,
    ulong BlockBitmapBlock,
    ulong InodeBitmapBlock,
    ulong InodeTableBlock,
    uint FreeBlockCount,
    uint FreeInodeCount,
    uint UsedDirectoryCount,
    ExtBlockGroupFlags Flags,
    uint UnusedInodeCount,
    ushort StoredChecksum,
    bool ChecksumVerified);
namespace TensaZangetsu.FileSystem.Ext.Types;

public enum ExtInodeType
{
    Unknown,
    Fifo,
    CharacterDevice,
    Directory,
    BlockDevice,
    RegularFile,
    SymbolicLink,
    Socket
}
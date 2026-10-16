namespace TensaZangetsu.Services.Loaders;

/// <summary>
/// Rol u objetivo de operación del Download Agent.
/// </summary>
public enum MtkDaOperationRole
{
    StandardFlash,
    RpmbService,
    HwIdRepair,
    NoSecPatched
}

/// <summary>
/// Etapa o protocolo de conexión del Download Agent.
/// </summary>
public enum MtkDaConnectionStage
{
    BROM,
    Preloader,
    SwSecCrypto20,
    V6_XML
}

/// <summary>
/// Metadatos y descriptor de una variante de Download Agent clasificada por versión de SO, BIT y función.
/// Permite mantener múltiples variantes para un mismo modelo o SoC sin descartar ninguna con hash SHA-256 único.
/// </summary>
public sealed record MtkDaVariant(
    string FilePath,
    string FileName,
    string Sha256,
    string Brand,
    string ModelOrCodename,
    string? Soc,
    string SecurityPatchOrOs,
    string? BinaryBit,
    int NumericBit,
    int OsVersionRank,
    MtkDaOperationRole OperationRole,
    MtkDaConnectionStage ConnectionStage,
    string DisplayLabel,
    string? AssociatedAuthPath = null,
    string? AssociatedXmlPath = null
)
{
    public override string ToString() => DisplayLabel;
}

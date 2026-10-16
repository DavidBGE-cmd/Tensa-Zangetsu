namespace TensaZangetsu.Services.Loaders;

/// <summary>
/// Huella de hardware del dispositivo conectado (Qualcomm, MediaTek o UNISOC).
/// </summary>
public sealed record DeviceHardwareFingerprint(
    string Platform,
    string? QcomMsmHwId = null,
    string? QcomOemPkHash = null,
    ushort? MtkHwCode = null,
    ushort? MtkHwSubCode = null,
    string? SprdSocId = null);

/// <summary>
/// Paquete de loaders emparejados para flashear el dispositivo identificado.
/// </summary>
public sealed record ResolvedLoaderBundle(
    string? QcomFirehosePath = null,
    string? MtkDaPath = null,
    string? MtkPreloaderPath = null,
    string? SprdFdl1Path = null,
    string? SprdFdl2Path = null,
    ulong SprdFdl1Address = 0,
    ulong SprdFdl2Address = 0,
    string? AuthFilePath = null,
    string? ScatterPath = null,
    string? CertPath = null,
    string? ModelMatch = null,
    MtkDaVariant? SelectedVariant = null,
    IReadOnlyList<MtkDaVariant>? CandidateDaVariants = null,
    IReadOnlyList<string>? CandidateDaFallbacks = null);

using TensaZangetsu.Protocol.Abstractions;
using TensaZangetsu.Protocol.Mtk.Exploits.Penumbra;
using TensaZangetsu.Protocol.Qcom.Abstractions;
using TensaZangetsu.Services.Firmware;

namespace TensaZangetsu.Services.Loaders;

/// <summary>
/// Proveedor automático de imágenes de programmer Firehose para el protocolo Qualcomm Sahara.
/// Resuelve dinámicamente el programmer .elf/.mbn según el HWID y PKHash del dispositivo,
/// o lo extrae al vuelo en streaming desde una ROM si no se encuentra en disco.
/// </summary>
public sealed class AutoSaharaImageProvider : ISaharaImageProvider
{
    private readonly AutoLoaderResolverService _loaderResolver;
    private readonly SelectiveRomExtractorService _romExtractor;
    private readonly string? _fallbackRomPackagePath;
    private readonly Action<string>? _logger;

    public AutoSaharaImageProvider(
        AutoLoaderResolverService loaderResolver,
        SelectiveRomExtractorService romExtractor,
        string? fallbackRomPackagePath = null,
        Action<string>? logger = null)
    {
        _loaderResolver = loaderResolver;
        _romExtractor = romExtractor;
        _fallbackRomPackagePath = fallbackRomPackagePath;
        _logger = logger;
    }

    public async ValueTask<SaharaImageEntryResponse> ResolveAsync(
        SaharaImageEntryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var saharaInfo = request.TargetInfo;
        uint? msmId = saharaInfo.MsmHwInfo?.MsmId;
        uint? socHwVer = saharaInfo.MsmHwInfo?.SocHwVersion;
        string msmHwIdHex = msmId.HasValue
            ? $"{msmId.Value:X8}"
            : (socHwVer.HasValue ? $"{socHwVer.Value:X8}" : (request.SocHint ?? ""));

        string pkHashHex = saharaInfo.CaHash.HasValue
            ? Convert.ToHexString(saharaInfo.CaHash.Value.Span)
            : "";

        string pkDisplay = pkHashHex.Length > 16 ? pkHashHex[..16] + "..." : pkHashHex;

        _logger?.Invoke(
            $"[SAHARA-AUTO] Dispositivo EDL detectado -> HWID: 0x{msmHwIdHex} | " +
            $"PKHash: {pkDisplay} | Serial: 0x{saharaInfo.Serial.GetValueOrDefault():X8}");

        // 1. Intentar emparejar el Firehose desde la base de datos local Assets/Loaders/Qcom/
        var fingerprint = new DeviceHardwareFingerprint(
            Platform: "QCOM",
            QcomMsmHwId: msmHwIdHex,
            QcomOemPkHash: pkHashHex);

        ResolvedLoaderBundle match = _loaderResolver.ResolveForDevice(fingerprint);
        string? firehosePath = match.QcomFirehosePath;

        // 2. Si no existe localmente y hay una ROM seleccionada (.ofp / .ops / .zip), extraer el Firehose en streaming
        if (string.IsNullOrEmpty(firehosePath) &&
            !string.IsNullOrWhiteSpace(_fallbackRomPackagePath) &&
            File.Exists(_fallbackRomPackagePath))
        {
            _logger?.Invoke($"[SAHARA-AUTO] Extrayendo Firehose en streaming desde ROM '{Path.GetFileName(_fallbackRomPackagePath)}'...");
            string cacheDir = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Assets", "Loaders", "Qcom", $"HW_{(msmHwIdHex.Length >= 8 ? msmHwIdHex[..8] : msmHwIdHex)}");

            var extractedLoaders = await _romExtractor.ExtractLoadersOnlyAsync(
                _fallbackRomPackagePath,
                cacheDir,
                progress: null,
                ct: cancellationToken);

            firehosePath = extractedLoaders.FirstOrDefault(f =>
                f.EndsWith(".elf", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".mbn", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".melf", StringComparison.OrdinalIgnoreCase));
        }

        if (string.IsNullOrEmpty(firehosePath) || !File.Exists(firehosePath))
        {
            throw new FileNotFoundException(
                $"No se encontró un programmer Firehose para HWID=0x{msmHwIdHex} (PKHash={pkDisplay}). " +
                "Colócalo en Assets/Loaders/Qcom o selecciona la ROM del equipo.");
        }

        _logger?.Invoke($"[SAHARA-AUTO] Subiendo Firehose seleccionado: {Path.GetFileName(firehosePath)}");

        byte[] programmerBytes = await File.ReadAllBytesAsync(firehosePath, cancellationToken);
        IDataSource programmerSource = new PenumbraBytesSource(programmerBytes);

        // El ImageId estándar para Firehose ELF/MBN en Sahara EDL es 13 (0x0D).
        // Mapeamos tanto 13 como 7 para garantizar compatibilidad con variantes de bootloader.
        var entry13 = new SaharaImageEntry(
            Id: 13,
            Length: programmerBytes.Length,
            DataSource: programmerSource);

        var entry7 = new SaharaImageEntry(
            Id: 7,
            Length: programmerBytes.Length,
            DataSource: programmerSource);

        return new SaharaImageEntryResponse([entry13, entry7]);
    }
}

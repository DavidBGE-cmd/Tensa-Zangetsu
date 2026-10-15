using TensaZangetsu.Protocol.Mtk.Abstractions;
using TensaZangetsu.Protocol.Mtk.Da;

namespace TensaZangetsu.Services.Loaders;

/// <summary>
/// Cosechador de Preloader y EMI desde la región física boot0/boot1 de un dispositivo en sesión DA activa.
/// Permite revivir dispositivos muertos del mismo SoC guardando el preloader original en Assets/Loaders/Mtk.
/// </summary>
public static class MtkBoot0PreloaderHarvester
{
    /// <summary>
    /// Intenta leer los primeros 512 KB de boot0/boot1 con la sesión DA activa,
    /// extraer el preloader limpio y el archivo _emi.bin, y archivarlos en disco.
    /// </summary>
    internal static string? TryHarvestFromLiveSession(
        IMtkDaSession daSession,
        ushort hwCode,
        string? outputBaseDir = null,
        Action<string>? logger = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(daSession);
        ct.ThrowIfCancellationRequested();

        try
        {
            MtkStorageInfo storage = daSession.GetStorage();
            if (storage.Regions == null || storage.Regions.Count == 0)
                return null;

            // Localizar boot0 / boot1 (generalmente WireId 1 o región con nombre "boot")
            var bootRegion = storage.Regions.FirstOrDefault(r =>
                r.WireId == 1 ||
                r.Name.Contains("boot", StringComparison.OrdinalIgnoreCase) && r.WireId != storage.UserRegionId)
                ?? storage.Regions.FirstOrDefault(r => r.WireId != storage.UserRegionId);

            if (bootRegion is null)
            {
                logger?.Invoke("[HARVEST] No se encontró una región física secundaria (boot0/boot1) para cosechar.");
                return null;
            }

            int harvestSize = 512 * 1024;
            long regionLen = (long)bootRegion.Length;
            if (regionLen > 0 && harvestSize > regionLen)
            {
                harvestSize = (int)regionLen;
            }

            logger?.Invoke($"[HARVEST] Leyendo {harvestSize / 1024} KB de la región física '{bootRegion.Name}' (WireId={bootRegion.WireId})...");

            using var ms = new MemoryStream(harvestSize);
            daSession.Read(bootRegion, 0, harvestSize, ms);
            byte[] bootBytes = ms.ToArray();

            // Intentar extraer Preloader y EMI desde los bytes leídos
            ExtractedEmiResult emiResult;
            try
            {
                emiResult = MtkPreloaderEmiExtractor.ExtractFromPreloaderBytes(bootBytes, $"boot0_MT{hwCode:X4}");
            }
            catch (Exception ex)
            {
                logger?.Invoke($"[HARVEST] No se detectó una cabecera Preloader válida en boot0: {ex.Message}");
                return null;
            }

            // Guardar en Assets/Loaders/Mtk/Preloaders/MT{hwCode:X4}/
            string baseDir = outputBaseDir ?? AppDomain.CurrentDomain.BaseDirectory;
            string targetDir = Path.Combine(baseDir, "Assets", "Loaders", "Mtk", "Preloaders", $"MT{hwCode:X4}");
            Directory.CreateDirectory(targetDir);

            string preloaderFile = Path.Combine(targetDir, $"preloader_harvested_MT{hwCode:X4}.bin");
            string emiFile = Path.Combine(targetDir, $"preloader_MT{hwCode:X4}_emi.bin");

            File.WriteAllBytes(preloaderFile, bootBytes);

            // Guardar bloque EMI en crudo
            using (var emiStream = emiResult.EmiImage.Source.OpenStream())
            using (var emiFs = File.Create(emiFile))
            {
                emiStream.CopyTo(emiFs);
            }

            logger?.Invoke($"[HARVEST] ¡Preloader cosechado con éxito! Guardado en: {preloaderFile} (EMI v{emiResult.EmiVersion})");
            return preloaderFile;
        }
        catch (Exception ex)
        {
            logger?.Invoke($"[HARVEST] Excepción al cosechar preloader desde boot0: {ex.Message}");
            return null;
        }
    }
}

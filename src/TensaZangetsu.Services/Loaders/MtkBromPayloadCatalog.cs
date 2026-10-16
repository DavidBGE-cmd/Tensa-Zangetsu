namespace TensaZangetsu.Services.Loaders;

/// <summary>
/// Parámetros de configuración y descriptores de memoria para la inyección y ejecución de payloads BROM
/// correspondientes a cada SoC MediaTek (mapeados desde default_config.json5 y payloads binarios).
/// </summary>
public sealed record MtkBromChipConfig(
    ushort HardwareCode,
    string ChipName,
    string PayloadFileName,
    uint PtrUsbdl,
    uint PtrDa,
    uint? WatchdogAddress = null,
    uint? PayloadAddress = null,
    byte? Var0 = null,
    byte? Var1 = null
);

/// <summary>
/// Catálogo centralizado de los 51 binarios de arranque y volcado (MTK Flash TOOL\payloads)
/// y tablas de registros de hardware.
/// </summary>
public static class MtkBromPayloadCatalog
{
    private static readonly Dictionary<ushort, MtkBromChipConfig> s_configs = new()
    {
        [0x6261] = new(0x6261, "MT6261", "mt6261_payload.bin", 0, 0, WatchdogAddress: 0xA0030000, Var1: 0x28),
        [0x6572] = new(0x6572, "MT6572", "mt6572_payload.bin", 0x40ba68, 0x40befc, PayloadAddress: 0x10036A0),
        [0x6580] = new(0x6580, "MT6580", "mt6580_payload.bin", 0xb60c, 0xba94, Var1: 0xAC),
        [0x6582] = new(0x6582, "MT6582", "mt6582_payload.bin", 0xa5fc, 0xaa84),
        [0x6592] = new(0x6592, "MT6592", "mt6592_payload.bin", 0xa564, 0xa9ec),
        [0x6595] = new(0x6595, "MT6595", "mt6595_payload.bin", 0xb218, 0xb6a0),
        [0x0321] = new(0x0321, "MT6735", "mt6735_payload.bin", 0x95f8, 0x9a94, WatchdogAddress: 0x10212000, Var0: 0x10, Var1: 0x28),
        [0x0335] = new(0x0335, "MT6737", "mt6737_payload.bin", 0x9608, 0x9aa4, WatchdogAddress: 0x10212000, Var0: 0x10, Var1: 0x28),
        [0x0699] = new(0x0699, "MT6739", "mt6739_payload.bin", 0xdf1c, 0xe3e8, Var0: 0x20, Var1: 0xB4),
        [0x0337] = new(0x0337, "MT6753", "mt6753_payload.bin", 0x9668, 0x9b04, WatchdogAddress: 0x10212000, Var1: 0x28),
        [0x0326] = new(0x0326, "MT6755", "mt6755_payload.bin", 0x9a6c, 0x9f14),
        [0x0551] = new(0x0551, "MT6757", "mt6757_payload.bin", 0x9c2c, 0xa0e8),
        [0x0717] = new(0x0717, "MT6761", "mt6761_payload.bin", 0xbc8c, 0xc158, Var1: 0x25),
        [0x0690] = new(0x0690, "MT6763", "mt6763_payload.bin", 0xd66c, 0xdb38, Var1: 0x7F),
        [0x0766] = new(0x0766, "MT6765", "mt6765_payload.bin", 0xbdc0, 0xc28c, Var0: 0x2C, Var1: 0x25),
        [0x0707] = new(0x0707, "MT6768", "mt6768_payload.bin", 0xc190, 0xc650, Var0: 0x2C, Var1: 0x25),
        [0x0788] = new(0x0788, "MT6771", "mt6771_payload.bin", 0xdebc, 0xe388, Var0: 0x20),
        [0x0725] = new(0x0725, "MT6779", "mt6779_payload.bin", 0xe04c, 0xe50c),
        [0x0813] = new(0x0813, "MT6785", "mt6785_payload.bin", 0xe2a4, 0xe764),
        [0x6795] = new(0x6795, "MT6795", "mt6795_payload.bin", 0x978c, 0x9c28),
        [0x0279] = new(0x0279, "MT6797", "mt6797_payload.bin", 0x9eac, 0xa354),
        [0x0562] = new(0x0562, "MT6799", "mt6799_payload.bin", 0xf5ac, 0xfa78, WatchdogAddress: 0x10211000),
        [0x0989] = new(0x0989, "MT6833", "mt6833_payload.bin", 0xdfe0, 0xe4a0),
        [0x0996] = new(0x0996, "MT6853", "mt6853_payload.bin", 0xea64, 0xef24),
        [0x0886] = new(0x0886, "MT6873", "mt6873_payload.bin", 0xea78, 0xef38),
        [0x0816] = new(0x0816, "MT6885", "mt6885_payload.bin", 0xe6b8, 0xebbc),
        [0x8127] = new(0x8127, "MT8127", "mt8127_payload.bin", 0xb2b8, 0xb740),
        [0x8163] = new(0x8163, "MT8163", "mt8163_payload.bin", 0xc12c, 0xc5c8, Var1: 0xB1),
        [0x8167] = new(0x8167, "MT8167", "mt8167_payload.bin", 0xd2e4, 0xd7ac, Var1: 0xCC),
        [0x8172] = new(0x8172, "MT8173", "mt8173_payload.bin", 0xa0e4, 0xa580, PayloadAddress: 0x120A00),
        [0x8590] = new(0x8590, "MT8590", "mt8590_payload.bin", 0xbbe4, 0xc06c),
        [0x8695] = new(0x8695, "MT8695", "mt8695_payload.bin", 0xbeec, 0xc3f8)
    };

    public static IReadOnlyDictionary<ushort, MtkBromChipConfig> Configs => s_configs;

    public static MtkBromChipConfig? GetConfig(ushort hardwareCode)
    {
        return s_configs.TryGetValue(hardwareCode, out var cfg) ? cfg : null;
    }

    /// <summary>
    /// Localiza la ruta del payload binario correspondiente al código de procesador o nombre genérico.
    /// </summary>
    public static string? FindPayloadPath(string rootAssetsDir, string payloadFileName)
    {
        string[] probeSubDirs = [
            Path.Combine(rootAssetsDir, "DA FILES MTK 2026 v22", "MTK Flash TOOL", "payloads"),
            Path.Combine(rootAssetsDir, "mt-katool-FutureAssets", "DA FILES MTK 2026 v22", "MTK Flash TOOL", "payloads"),
            Path.Combine(rootAssetsDir, "exploits_collection-master", "payloads"),
            Path.Combine(rootAssetsDir, "ExternalAssets", "payloads"),
            Path.Combine(rootAssetsDir, "Assets", "payloads")
        ];

        foreach (var dir in probeSubDirs)
        {
            if (Directory.Exists(dir))
            {
                string target = Path.Combine(dir, payloadFileName);
                if (File.Exists(target)) return target;
            }
        }

        return null;
    }
}

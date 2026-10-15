namespace TensaZangetsu.Services.Catalog;

public static class DeviceCatalogRepository
{
    private static readonly List<DeviceModel> Devices =
    [
        // ==================== UNIVERSAL / GENERIC ====================
        new()
        {
            Id = "gen_mtk_brom",
            Brand = "Generic",
            Name = "MediaTek Universal (BROM / Preloader V5)",
            MarketingName = "All MT67xx / MT65xx / MT68xx",
            Platform = ChipsetPlatform.MediaTek,
            SocChip = "MTK Universal",
            ConnectionInstructions = "Apagar teléfono, presionar Vol+ y Vol- y conectar USB.",
            RequiresDaPatching = true
        },
        new()
        {
            Id = "gen_mtk_v6",
            Brand = "Generic",
            Name = "MediaTek V6 Modern (Dimensity / G99)",
            MarketingName = "MT6789 / MT6855 / MT6886 / MT6895 / MT6983",
            Platform = ChipsetPlatform.MediaTek,
            SocChip = "MTK V6 Protocol",
            ConnectionInstructions = "Conectar en Preloader (sin botones) o forzar mediante 'adb reboot edl'.",
            RequiresDaPatching = true
        },
        new()
        {
            Id = "gen_qcom_edl",
            Brand = "Generic",
            Name = "Qualcomm Snapdragon EDL (9008)",
            MarketingName = "Universal Firehose",
            Platform = ChipsetPlatform.Qualcomm,
            SocChip = "Snapdragon Generic",
            ConnectionInstructions = "Conectar en modo EDL 9008 (Testpoint o cable EDL)."
        },
        new()
        {
            Id = "gen_sprd_pac",
            Brand = "Generic",
            Name = "Unisoc / Spreadtrum BSL",
            MarketingName = "SC9863A / T606 / T610 / T612 / T616 / T618",
            Platform = ChipsetPlatform.Unisoc,
            SocChip = "Unisoc U2S Diag",
            ConnectionInstructions = "Apagar teléfono, presionar Vol- y conectar USB (Puerto 1782:4D00)."
        },

        // ==================== XIAOMI / REDMI / POCO ====================
        new()
        {
            Id = "xiaomi_redmi_9a",
            Brand = "Xiaomi",
            Name = "dandelion",
            MarketingName = "Redmi 9A / 9AT / 9i",
            Platform = ChipsetPlatform.MediaTek,
            SocChip = "MT6762G Helio G25",
            FrpPartitions = ["frp"],
            NvPartitions = ["nvram", "nvdata", "protect1", "protect2", "nvcfg"],
            ConnectionInstructions = "Apagar, presionar Vol- y Vol+ y conectar cable USB."
        },
        new()
        {
            Id = "xiaomi_redmi_9",
            Brand = "Xiaomi",
            Name = "lancelot",
            MarketingName = "Redmi 9 / 9 Prime",
            Platform = ChipsetPlatform.MediaTek,
            SocChip = "MT6769V Helio G80",
            FrpPartitions = ["frp"],
            NvPartitions = ["nvram", "nvdata", "protect1", "protect2"]
        },
        new()
        {
            Id = "xiaomi_redmi_note_8",
            Brand = "Xiaomi",
            Name = "ginkgo",
            MarketingName = "Redmi Note 8",
            Platform = ChipsetPlatform.Qualcomm,
            SocChip = "Snapdragon 665",
            FrpPartitions = ["frp", "config"],
            ConnectionInstructions = "Conectar con Testpoint en modo EDL 9008."
        },
        new()
        {
            Id = "xiaomi_redmi_note_10",
            Brand = "Xiaomi",
            Name = "mojito",
            MarketingName = "Redmi Note 10",
            Platform = ChipsetPlatform.Qualcomm,
            SocChip = "Snapdragon 678",
            FrpPartitions = ["frp"],
            ConnectionInstructions = "Conectar con Testpoint en modo EDL 9008."
        },
        new()
        {
            Id = "xiaomi_redmi_note_11_pro_4g",
            Brand = "Xiaomi",
            Name = "viva",
            MarketingName = "Redmi Note 11 Pro 4G",
            Platform = ChipsetPlatform.MediaTek,
            SocChip = "MT6781 Helio G96",
            FrpPartitions = ["frp"],
            NvPartitions = ["nvram", "nvdata", "protect1", "protect2"]
        },
        new()
        {
            Id = "xiaomi_redmi_note_12s",
            Brand = "Xiaomi",
            Name = "sea",
            MarketingName = "Redmi Note 12S",
            Platform = ChipsetPlatform.MediaTek,
            SocChip = "MT6781 Helio G96",
            FrpPartitions = ["frp"]
        },
        new()
        {
            Id = "xiaomi_poco_m5",
            Brand = "Xiaomi",
            Name = "rock",
            MarketingName = "POCO M5",
            Platform = ChipsetPlatform.MediaTek,
            SocChip = "MT6789 Helio G99",
            FrpPartitions = ["frp"],
            ConnectionInstructions = "Conectar en Preloader (cable normal sin botones)."
        },

        // ==================== SAMSUNG ====================
        new()
        {
            Id = "samsung_a03_core",
            Brand = "Samsung",
            Name = "SM-A032M / SM-A032F",
            MarketingName = "Galaxy A03 Core",
            Platform = ChipsetPlatform.Unisoc,
            SocChip = "SC9863A",
            FrpPartitions = ["persistent"],
            ConnectionInstructions = "Apagar, presionar Vol- y conectar USB (Testpoint si no entra en BSL)."
        },
        new()
        {
            Id = "samsung_a03",
            Brand = "Samsung",
            Name = "SM-A035M / SM-A035F",
            MarketingName = "Galaxy A03",
            Platform = ChipsetPlatform.Unisoc,
            SocChip = "Unisoc T606",
            FrpPartitions = ["persistent"],
            ConnectionInstructions = "Conectar Testpoint a GND y enchufar cable USB."
        },
        new()
        {
            Id = "samsung_a04e",
            Brand = "Samsung",
            Name = "SM-A042M / SM-A042F",
            MarketingName = "Galaxy A04e",
            Platform = ChipsetPlatform.MediaTek,
            SocChip = "MT6765 Helio P35",
            FrpPartitions = ["persistent"],
            NvPartitions = ["nvram", "nvdata", "protect1", "protect2", "sec_efs"],
            ConnectionInstructions = "Apagar, presionar Vol+ y Vol- y conectar USB."
        },
        new()
        {
            Id = "samsung_a12",
            Brand = "Samsung",
            Name = "SM-A125M / SM-A125F",
            MarketingName = "Galaxy A12",
            Platform = ChipsetPlatform.MediaTek,
            SocChip = "MT6765 Helio P35",
            FrpPartitions = ["persistent"],
            NvPartitions = ["nvram", "nvdata", "sec_efs"]
        },
        new()
        {
            Id = "samsung_a14_4g",
            Brand = "Samsung",
            Name = "SM-A145M / SM-A145F",
            MarketingName = "Galaxy A14 4G",
            Platform = ChipsetPlatform.MediaTek,
            SocChip = "MT6769V Helio G80",
            FrpPartitions = ["persistent"]
        },
        new()
        {
            Id = "samsung_a22_4g",
            Brand = "Samsung",
            Name = "SM-A225M / SM-A225F",
            MarketingName = "Galaxy A22 4G",
            Platform = ChipsetPlatform.MediaTek,
            SocChip = "MT6769V Helio G80",
            FrpPartitions = ["persistent"]
        },

        // ==================== MOTOROLA ====================
        new()
        {
            Id = "moto_e7",
            Brand = "Motorola",
            Name = "XT2095",
            MarketingName = "Moto E7",
            Platform = ChipsetPlatform.MediaTek,
            SocChip = "MT6762G Helio G25",
            FrpPartitions = ["frp"]
        },
        new()
        {
            Id = "moto_g22",
            Brand = "Motorola",
            Name = "XT2231",
            MarketingName = "Moto G22",
            Platform = ChipsetPlatform.MediaTek,
            SocChip = "MT6765V Helio G37",
            FrpPartitions = ["frp"]
        },
        new()
        {
            Id = "moto_g24",
            Brand = "Motorola",
            Name = "XT2423",
            MarketingName = "Moto G24",
            Platform = ChipsetPlatform.MediaTek,
            SocChip = "MT6769Z Helio G85",
            FrpPartitions = ["frp"]
        },
        new()
        {
            Id = "moto_g52",
            Brand = "Motorola",
            Name = "XT2221",
            MarketingName = "Moto G52",
            Platform = ChipsetPlatform.Qualcomm,
            SocChip = "Snapdragon 680",
            FrpPartitions = ["frp"],
            ConnectionInstructions = "Conectar en modo EDL 9008 mediante Testpoint."
        },

        // ==================== OPPO / REALME ====================
        new()
        {
            Id = "oppo_a15",
            Brand = "Oppo",
            Name = "CPH2185",
            MarketingName = "Oppo A15",
            Platform = ChipsetPlatform.MediaTek,
            SocChip = "MT6765 Helio P35",
            FrpPartitions = ["frp"]
        },
        new()
        {
            Id = "realme_c11",
            Brand = "Realme",
            Name = "RMX2185",
            MarketingName = "Realme C11",
            Platform = ChipsetPlatform.MediaTek,
            SocChip = "MT6765G Helio G35",
            FrpPartitions = ["frp"]
        },
        new()
        {
            Id = "realme_c30",
            Brand = "Realme",
            Name = "RMX3581",
            MarketingName = "Realme C30",
            Platform = ChipsetPlatform.Unisoc,
            SocChip = "Unisoc T612",
            FrpPartitions = ["persistent"]
        },

        // ==================== TRANSSION (INFINIX / TECNO) ====================
        new()
        {
            Id = "infinix_hot_10",
            Brand = "Infinix",
            Name = "X682",
            MarketingName = "Hot 10",
            Platform = ChipsetPlatform.MediaTek,
            SocChip = "MT6769V Helio G70",
            FrpPartitions = ["frp"]
        },
        new()
        {
            Id = "tecno_spark_8c",
            Brand = "Tecno",
            Name = "KG5k",
            MarketingName = "Spark 8C",
            Platform = ChipsetPlatform.Unisoc,
            SocChip = "Unisoc T606",
            FrpPartitions = ["persistent"]
        }
    ];

    public static IReadOnlyList<DeviceModel> GetAll() => Devices;

    public static IReadOnlyList<string> GetBrands() =>
        Devices.Select(d => d.Brand).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(b => b).ToList();

    public static IReadOnlyList<DeviceModel> GetByBrand(string brand) =>
        Devices.Where(d => string.Equals(d.Brand, brand, StringComparison.OrdinalIgnoreCase)).ToList();

    public static IReadOnlyList<DeviceModel> GetByPlatform(ChipsetPlatform platform) =>
        Devices.Where(d => d.Platform == platform).ToList();

    public static IReadOnlyList<DeviceModel> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Devices;

        return Devices.Where(d =>
            d.Brand.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            d.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            d.MarketingName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            d.SocChip.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}

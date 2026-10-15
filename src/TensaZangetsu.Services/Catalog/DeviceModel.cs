namespace TensaZangetsu.Services.Catalog;

public record DeviceModel
{
    public string Id { get; init; } = string.Empty;
    public string Brand { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string MarketingName { get; init; } = string.Empty;
    public ChipsetPlatform Platform { get; init; }
    public string SocChip { get; init; } = string.Empty;
    public string[] FrpPartitions { get; init; } = ["frp", "persistent", "config"];
    public string[] NvPartitions { get; init; } = ["nvram", "nvdata", "protect1", "protect2"];
    public string ConnectionInstructions { get; init; } = "Apagar teléfono, presionar Vol+ y Vol- y conectar cable USB.";
    public string? CustomLoaderFileName { get; init; }
    public bool RequiresDaPatching { get; init; } = true;
    public string? TestpointInfo { get; init; }

    public string DisplayTitle => string.IsNullOrWhiteSpace(MarketingName) 
        ? $"{Brand} {Name} ({SocChip})" 
        : $"{Brand} {MarketingName} [{Name}] ({SocChip})";
}

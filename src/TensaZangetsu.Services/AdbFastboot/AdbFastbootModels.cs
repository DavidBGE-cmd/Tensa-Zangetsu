namespace TensaZangetsu.Services.AdbFastboot;

public enum ConnectedDeviceMode
{
    Disconnected,
    AdbOnline,              // Android OS corriendo, USB Debugging autorizado
    AdbUnauthorized,        // Android OS corriendo, falta autorizar RSA en pantalla
    AdbRecovery,            // Modo Recovery (TWRP, OrangeFox, Stock Recovery)
    AdbSideload,            // Modo ADB Sideload en Recovery
    Fastboot,               // Modo Bootloader Fastboot clásico (is-userspace: no)
    FastbootD,              // Modo FastbootD userspace para particiones dinámicas super (is-userspace: yes)
    SamsungDownload,        // Modo Odin / Download
    MtkBrom,                // MediaTek BootROM a bajo nivel
    MtkPreloader,           // MediaTek Preloader COM
    MtkDa,                  // MediaTek Download Agent
    QualcommEdl,            // Qualcomm Sahara EDL 9008
    UnisocBsl,              // Unisoc / SPRD U2S Diag
    MissingDriverAdb,       // Hardware presente pero sin driver ADB en Windows
    MissingDriverFastboot,  // Hardware presente pero sin driver Bootloader/WinUSB en Windows
    MissingDriverRecovery   // Hardware en recovery pero sin driver reconocido
}

public record AdbFastbootDeviceInfo(
    ConnectedDeviceMode Mode,
    string Serial,
    string Brand,
    string ModelName,
    string ProductCode,
    string AndroidVersion,
    string BuildId,
    string ActiveSlot,
    bool IsUserspace,
    string DriverName,
    bool IsDriverOk,
    string DriverAdvice,
    string Details
)
{
    public static AdbFastbootDeviceInfo Disconnected => new(
        Mode: ConnectedDeviceMode.Disconnected,
        Serial: string.Empty,
        Brand: string.Empty,
        ModelName: string.Empty,
        ProductCode: string.Empty,
        AndroidVersion: string.Empty,
        BuildId: string.Empty,
        ActiveSlot: string.Empty,
        IsUserspace: false,
        DriverName: "Ninguno",
        IsDriverOk: false,
        DriverAdvice: "Conecte un dispositivo por cable USB.",
        Details: "Ningún dispositivo conectado."
    );

    public string ModeBadgeText => Mode switch
    {
        ConnectedDeviceMode.AdbOnline => "🟢 ANDROID SYSTEM (ADB ONLINE)",
        ConnectedDeviceMode.AdbUnauthorized => "🟡 ADB NO AUTORIZADO (RSA)",
        ConnectedDeviceMode.AdbRecovery => "🟣 MODO RECOVERY (STOCK/TWRP)",
        ConnectedDeviceMode.AdbSideload => "🔵 ADB SIDELOAD",
        ConnectedDeviceMode.Fastboot => "🟠 FASTBOOT BOOTLOADER",
        ConnectedDeviceMode.FastbootD => "🔴 FASTBOOT-D USERSPACE",
        ConnectedDeviceMode.SamsungDownload => "🔵 SAMSUNG DOWNLOAD (ODIN)",
        ConnectedDeviceMode.MtkBrom => "⚡ MEDIATEK BROM",
        ConnectedDeviceMode.MtkPreloader => "⚙️ MEDIATEK PRELOADER",
        ConnectedDeviceMode.MtkDa => "💾 MEDIATEK DA",
        ConnectedDeviceMode.QualcommEdl => "🐉 QUALCOMM EDL 9008",
        ConnectedDeviceMode.UnisocBsl => "📱 UNISOC BSL DIAG",
        ConnectedDeviceMode.MissingDriverAdb => "⚠️ FALTA CONTROLADOR ADB",
        ConnectedDeviceMode.MissingDriverFastboot => "⚠️ FALTA CONTROLADOR FASTBOOT",
        ConnectedDeviceMode.MissingDriverRecovery => "⚠️ FALTA CONTROLADOR RECOVERY",
        _ => "DISPOSITIVO NO CONECTADO"
    };

    public string ModeColorHex => Mode switch
    {
        ConnectedDeviceMode.AdbOnline => "#00E676",
        ConnectedDeviceMode.AdbUnauthorized => "#FFD54F",
        ConnectedDeviceMode.AdbRecovery => "#E040FB",
        ConnectedDeviceMode.AdbSideload => "#00B0FF",
        ConnectedDeviceMode.Fastboot => "#FF9100",
        ConnectedDeviceMode.FastbootD => "#FF1744",
        ConnectedDeviceMode.SamsungDownload => "#2979FF",
        ConnectedDeviceMode.MtkBrom or ConnectedDeviceMode.MtkPreloader => "#00E676",
        ConnectedDeviceMode.QualcommEdl => "#00E5FF",
        ConnectedDeviceMode.UnisocBsl => "#FF6D00",
        ConnectedDeviceMode.MissingDriverAdb or
        ConnectedDeviceMode.MissingDriverFastboot or
        ConnectedDeviceMode.MissingDriverRecovery => "#FF5252",
        _ => "#78909C"
    };

    public string DisplayName => !string.IsNullOrWhiteSpace(ModelName)
        ? (!string.IsNullOrWhiteSpace(Brand) ? $"{Brand} {ModelName}".Trim() : ModelName)
        : (!string.IsNullOrWhiteSpace(ProductCode) ? ProductCode : ModeBadgeText);
}

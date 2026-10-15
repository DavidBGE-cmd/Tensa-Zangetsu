using TensaZangetsu.Protocol.Qcom.Abstractions;

namespace TensaZangetsu.Protocol.Qcom.Vendors.Zte;

public sealed class ZteFirehoseStrategy : IVendorFirehoseStrategy
{
    public static ZteFirehoseStrategy Instance { get; } = new();

    private ZteFirehoseStrategy()
    {
    }

    public QcomVendorKind Vendor => QcomVendorKind.Zte;
    public IReadOnlySet<string> AllowedCustomCommands { get; } = GenericVendorStrategy.Extend();

    public ConfigureCommand PrepareConfigure(ConfigureCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command with { Oem = "ZTE" };
    }
}

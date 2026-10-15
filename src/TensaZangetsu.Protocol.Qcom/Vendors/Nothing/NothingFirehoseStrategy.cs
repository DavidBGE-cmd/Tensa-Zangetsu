using TensaZangetsu.Protocol.Qcom.Abstractions;

namespace TensaZangetsu.Protocol.Qcom.Vendors.Nothing;

public sealed class NothingFirehoseStrategy : IVendorFirehoseStrategy
{
    public static NothingFirehoseStrategy Instance { get; } = new();

    private NothingFirehoseStrategy()
    {
    }

    public QcomVendorKind Vendor => QcomVendorKind.Nothing;
    public IReadOnlySet<string> AllowedCustomCommands { get; } =
        GenericVendorStrategy.Extend("checkntfeature", "ntprojectverify");

    public ConfigureCommand PrepareConfigure(ConfigureCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command;
    }
}

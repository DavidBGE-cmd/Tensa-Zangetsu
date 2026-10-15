using TensaZangetsu.Protocol.Qcom.Abstractions;

namespace TensaZangetsu.Protocol.Qcom.Vendors;

public interface IVendorFirehoseStrategy
{
    QcomVendorKind Vendor { get; }
    IReadOnlySet<string> AllowedCustomCommands { get; }
    ConfigureCommand PrepareConfigure(ConfigureCommand command);
}

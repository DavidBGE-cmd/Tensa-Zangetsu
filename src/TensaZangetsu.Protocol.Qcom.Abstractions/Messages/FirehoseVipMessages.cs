using TensaZangetsu.Protocol.Abstractions;

namespace TensaZangetsu.Protocol.Qcom.Abstractions;

public sealed record FirehoseVipResourceRequest(QcomTargetInfo TargetInfo);

public sealed record FirehoseVipResourceResponse(
    IDataSource SignedTable,
    IReadOnlyList<IDataSource>? ChainedTables = null);

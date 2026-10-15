using TensaZangetsu.Protocol.Abstractions;

namespace TensaZangetsu.Protocol.Qcom.Abstractions;

public sealed record FirehoseDigestResourceRequest(QcomTargetInfo TargetInfo);

public sealed record FirehoseDigestResourceResponse(IDataSource Digest, string? ResourceId = null);

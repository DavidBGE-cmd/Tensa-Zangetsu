using TensaZangetsu.Protocol.Abstractions;

namespace TensaZangetsu.Protocol.Qcom.Abstractions;

public interface IQcomProgrammerInspector
{
    bool TryInspect(ReadOnlySpan<byte> image, out QcomProgrammerInfo info);
    bool TryInspect(string filePath, out QcomProgrammerInfo info);
    bool TryInspect(IDataSource source, out QcomProgrammerInfo info);
}

namespace TensaZangetsu.Protocol.Mtk.Abstractions;

public sealed class MtkFlashFillException(Exception inner) : TensaZangetsu.Protocol.Abstractions.ProtocolException(Localization.Strings.FlashFillUnknown,inner)
{public bool MayHaveWritten=>true;}

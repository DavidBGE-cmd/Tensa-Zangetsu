namespace TensaZangetsu.FileSystem.Abstractions;

public enum FileSystemProbeStatus
{
    NotRecognized,
    RecognizedSupported,
    RecognizedUnsupported,
    RecognizedCorrupt
}
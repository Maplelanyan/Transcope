namespace Transcope.Ocr;

public sealed class OcrEngineUnavailableException : InvalidOperationException
{
    public OcrEngineUnavailableException(OcrEngineKind engineKind, string message)
        : base(message)
    {
        EngineKind = engineKind;
    }

    public OcrEngineKind EngineKind { get; }
}

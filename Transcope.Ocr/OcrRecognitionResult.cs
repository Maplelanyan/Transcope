namespace Transcope.Ocr;

public sealed record OcrRecognitionResult(
    OcrEngineKind EngineKind,
    string Text,
    IReadOnlyList<OcrRecognizedLine> Lines,
    string? LanguageTag,
    double? TextAngle);

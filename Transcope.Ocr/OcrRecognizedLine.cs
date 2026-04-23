namespace Transcope.Ocr;

public sealed record OcrRecognizedLine(
    string Text,
    OcrTextBoundary? Boundary,
    IReadOnlyList<OcrRecognizedWord> Words);

namespace Transcope.Ocr;

public sealed record OcrRecognizedWord(
    string Text,
    OcrTextBoundary Boundary,
    float? Confidence);

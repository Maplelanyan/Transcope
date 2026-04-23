using Windows.Graphics.Imaging;

namespace Transcope.Ocr;

internal interface IOcrEngineAdapter
{
    OcrEngineKind EngineKind { get; }

    ValueTask<bool> IsAvailableAsync(
        OcrRecognitionOptions options,
        CancellationToken cancellationToken);

    ValueTask<OcrRecognitionResult> RecognizeAsync(
        SoftwareBitmap bitmap,
        OcrRecognitionOptions options,
        CancellationToken cancellationToken);
}

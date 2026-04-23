using Windows.Graphics.Imaging;

namespace Transcope.Ocr;

public interface IOcrRecognizer
{
    ValueTask<OcrRecognitionResult> RecognizeAsync(
        SoftwareBitmap bitmap,
        OcrRecognitionOptions? options = null,
        CancellationToken cancellationToken = default);
}

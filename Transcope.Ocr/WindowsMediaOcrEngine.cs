using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Transcope.Ocr;

internal sealed class WindowsMediaOcrEngine : IOcrEngineAdapter
{
    public OcrEngineKind EngineKind => OcrEngineKind.WindowsMedia;

    public ValueTask<bool> IsAvailableAsync(
        OcrRecognitionOptions options,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(CreateEngine(options.LanguageTag) is not null);
    }

    public async ValueTask<OcrRecognitionResult> RecognizeAsync(
        SoftwareBitmap bitmap,
        OcrRecognitionOptions options,
        CancellationToken cancellationToken)
    {
        OcrEngine engine = CreateEngine(options.LanguageTag)
            ?? throw new OcrEngineUnavailableException(
                EngineKind,
                "Windows.Media.Ocr is not available for the requested language.");

        ValidateImageSize(bitmap);

        SoftwareBitmap recognitionBitmap = bitmap;
        bool disposeRecognitionBitmap = false;

        if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
            bitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
        {
            recognitionBitmap = SoftwareBitmap.Convert(
                bitmap,
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied);
            disposeRecognitionBitmap = true;
        }

        try
        {
            OcrResult result = await engine.RecognizeAsync(recognitionBitmap)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            return ToResult(result, engine.RecognizerLanguage?.LanguageTag);
        }
        finally
        {
            if (disposeRecognitionBitmap)
            {
                recognitionBitmap.Dispose();
            }
        }
    }

    private static OcrEngine? CreateEngine(string? languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag))
        {
            return OcrEngine.TryCreateFromUserProfileLanguages();
        }

        try
        {
            Language language = new(languageTag);
            return OcrEngine.IsLanguageSupported(language)
                ? OcrEngine.TryCreateFromLanguage(language)
                : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static void ValidateImageSize(SoftwareBitmap bitmap)
    {
        uint maxDimension = OcrEngine.MaxImageDimension;
        if (bitmap.PixelWidth > maxDimension || bitmap.PixelHeight > maxDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bitmap),
                $"Windows.Media.Ocr supports images up to {maxDimension}px on each side.");
        }
    }
   
    private static OcrRecognitionResult ToResult(OcrResult result, string? languageTag)
    {
        OcrRecognizedLine[] lines = result.Lines.Select(ToLine).ToArray();
        string text = string.Join(Environment.NewLine, lines.Select(static line => line.Text));

        return new OcrRecognitionResult(
            OcrEngineKind.WindowsMedia,
            text,
            lines,
            languageTag,
            TextAngle: result.TextAngle);
    }

    private static OcrRecognizedLine ToLine(OcrLine line)
    {
        OcrRecognizedWord[] words = line.Words.Select(ToWord).ToArray();

        return new OcrRecognizedLine(
            line.Text,
            CreateLineBoundary(words),
            words);
    }

    private static OcrRecognizedWord ToWord(OcrWord word)
    {
        return new OcrRecognizedWord(
            word.Text,
            OcrTextBoundary.FromRect(word.BoundingRect),
            Confidence: null);
    }

    private static OcrTextBoundary? CreateLineBoundary(IReadOnlyCollection<OcrRecognizedWord> words)
    {
        if (words.Count == 0)
        {
            return null;
        }

        OcrRectangle rectangle = OcrRectangle.FromPoints(
            words.SelectMany(static word => new[]
            {
                word.Boundary.TopLeft,
                word.Boundary.TopRight,
                word.Boundary.BottomRight,
                word.Boundary.BottomLeft
            }));

        return OcrTextBoundary.FromRect(new Windows.Foundation.Rect(
            rectangle.X,
            rectangle.Y,
            rectangle.Width,
            rectangle.Height));
    }
}

using Microsoft.Graphics.Imaging;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Imaging;
using Windows.Graphics.Imaging;

namespace Transcope.Ocr;

internal sealed class WindowsAppSdkOcrEngine : IOcrEngineAdapter
{
    public OcrEngineKind EngineKind => OcrEngineKind.WindowsAppSdk;

    public async ValueTask<bool> IsAvailableAsync(
        OcrRecognitionOptions options,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return false;
        }

        try
        {
            AIFeatureReadyState readyState = TextRecognizer.GetReadyState();
            if (readyState == AIFeatureReadyState.Ready)
            {
                return true;
            }

            if (readyState != AIFeatureReadyState.NotReady)
            {
                return false;
            }

            AIFeatureReadyResult readyResult = await TextRecognizer.EnsureReadyAsync()
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            return readyResult.Status == AIFeatureReadyResultState.Success;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask<OcrRecognitionResult> RecognizeAsync(
        SoftwareBitmap bitmap,
        OcrRecognitionOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        TextRecognizer textRecognizer = await TextRecognizer.CreateAsync()
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        using ImageBuffer imageBuffer = ImageBuffer.CreateForSoftwareBitmap(bitmap);
        RecognizedText recognizedText = await textRecognizer.RecognizeTextFromImageAsync(imageBuffer)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        return ToResult(recognizedText);
    }

    private static OcrRecognitionResult ToResult(RecognizedText recognizedText)
    {
        OcrRecognizedLine[] lines = recognizedText.Lines.Select(ToLine).ToArray();
        string text = string.Join(Environment.NewLine, lines.Select(static line => line.Text));

        return new OcrRecognitionResult(
            OcrEngineKind.WindowsAppSdk,
            text,
            lines,
            LanguageTag: null,
            TextAngle: recognizedText.TextAngle);
    }

    private static OcrRecognizedLine ToLine(RecognizedLine line)
    {
        OcrRecognizedWord[] words = line.Words.Select(ToWord).ToArray();

        return new OcrRecognizedLine(
            line.Text,
            ToBoundary(line.BoundingBox),
            words);
    }

    private static OcrRecognizedWord ToWord(RecognizedWord word)
    {
        return new OcrRecognizedWord(
            word.Text,
            ToBoundary(word.BoundingBox),
            word.MatchConfidence);
    }

    private static OcrTextBoundary ToBoundary(RecognizedTextBoundingBox boundingBox)
    {
        return new OcrTextBoundary(
            new OcrPoint(boundingBox.TopLeft.X, boundingBox.TopLeft.Y),
            new OcrPoint(boundingBox.TopRight.X, boundingBox.TopRight.Y),
            new OcrPoint(boundingBox.BottomRight.X, boundingBox.BottomRight.Y),
            new OcrPoint(boundingBox.BottomLeft.X, boundingBox.BottomLeft.Y));
    }
}

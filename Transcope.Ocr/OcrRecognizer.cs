using Windows.Graphics.Imaging;

namespace Transcope.Ocr;

public sealed class OcrRecognizer : IOcrRecognizer
{
    private readonly IOcrEngineAdapter windowsAppSdkEngine;
    private readonly IOcrEngineAdapter windowsMediaEngine;
    private readonly IOcrEngineAdapter tesseractEngine;
    private readonly IOcrEngineAdapter paddleOcrEngine;

    public OcrRecognizer()
        : this(new PaddleOcrEngine(), new WindowsAppSdkOcrEngine(), new WindowsMediaOcrEngine(), new TesseractOcrEngine())
    {
    }

    internal OcrRecognizer(
        IOcrEngineAdapter paddleOcrEngine,
        IOcrEngineAdapter windowsAppSdkEngine,
        IOcrEngineAdapter windowsMediaEngine,
        IOcrEngineAdapter tesseractEngine)
    {
        this.paddleOcrEngine = paddleOcrEngine;
        this.windowsAppSdkEngine = windowsAppSdkEngine;
        this.windowsMediaEngine = windowsMediaEngine;
        this.tesseractEngine = tesseractEngine;
    }

    public async ValueTask<OcrRecognitionResult> RecognizeAsync(
        SoftwareBitmap bitmap,
        OcrRecognitionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        options ??= OcrRecognitionOptions.Default;
        IOcrEngineAdapter engine = await SelectEngineAsync(options, cancellationToken).ConfigureAwait(false);

        return await engine.RecognizeAsync(bitmap, options, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<IOcrEngineAdapter> SelectEngineAsync(
        OcrRecognitionOptions options,
        CancellationToken cancellationToken)
    {
        return options.EngineSelection switch
        {
            OcrEngineSelection.PaddleOcr => await RequireEngineAsync(paddleOcrEngine, options, cancellationToken)
                .ConfigureAwait(false),
            OcrEngineSelection.WindowsAppSdk => await RequireEngineAsync(windowsAppSdkEngine, options, cancellationToken)
                .ConfigureAwait(false),
            OcrEngineSelection.WindowsMedia => await RequireEngineAsync(windowsMediaEngine, options, cancellationToken)
                .ConfigureAwait(false),
            OcrEngineSelection.Tesseract => await RequireEngineAsync(tesseractEngine, options, cancellationToken)
                .ConfigureAwait(false),
            _ => await SelectAutomaticEngineAsync(options, cancellationToken).ConfigureAwait(false)
        };
    }

    private async ValueTask<IOcrEngineAdapter> SelectAutomaticEngineAsync(
        OcrRecognitionOptions options,
        CancellationToken cancellationToken)
    {
        if (await paddleOcrEngine.IsAvailableAsync(options, cancellationToken).ConfigureAwait(false))
        {
            return paddleOcrEngine;
        }

        if (await windowsAppSdkEngine.IsAvailableAsync(options, cancellationToken).ConfigureAwait(false))
        {
            return windowsAppSdkEngine;
        }

        if (await windowsMediaEngine.IsAvailableAsync(options, cancellationToken).ConfigureAwait(false))
        {
            return windowsMediaEngine;
        }

        return await RequireEngineAsync(tesseractEngine, options, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<IOcrEngineAdapter> RequireEngineAsync(
        IOcrEngineAdapter engine,
        OcrRecognitionOptions options,
        CancellationToken cancellationToken)
    {
        if (await engine.IsAvailableAsync(options, cancellationToken).ConfigureAwait(false))
        {
            return engine;
        }

        string message = engine.EngineKind == OcrEngineKind.Tesseract
            ? "Tesseract OCR is not available. Add traineddata files under a tessdata directory, or set TESSDATA_PREFIX."
            : engine.EngineKind == OcrEngineKind.PaddleOcr
                ? "PaddleOCR is not available. Install Python packages with: python -m pip install paddleocr paddlepaddle, or set PADDLEOCR_PYTHON to a Python executable that has them installed."
            : $"{engine.EngineKind} OCR is not available for the current OS, hardware, or requested language.";

        throw new OcrEngineUnavailableException(engine.EngineKind, message);
    }
}

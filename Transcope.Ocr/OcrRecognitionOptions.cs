namespace Transcope.Ocr;

public sealed record OcrRecognitionOptions
{
    public static OcrRecognitionOptions Default { get; } = new();

    public OcrEngineSelection EngineSelection { get; init; } = OcrEngineSelection.Auto;

    public PaddleRuntimeMode PaddleRuntimeMode { get; init; } = PaddleRuntimeMode.Cpu;

    /// <summary>
    /// BCP-47 language tag used by Windows.Media.Ocr. Tesseract also accepts raw traineddata codes such as chi_sim.
    /// Windows App SDK AI OCR currently auto-detects text.
    /// </summary>
    public string? LanguageTag { get; init; }
}

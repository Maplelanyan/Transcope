namespace Transcope.Translate;

public sealed record TranslationRequest
{
    public static TranslationRequest Default { get; } = new();

    public string? SourceLanguage { get; init; }

    public string TargetLanguage { get; init; } = "zh-Hans";
}

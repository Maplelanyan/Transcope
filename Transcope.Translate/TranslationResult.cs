namespace Transcope.Translate;

public sealed record TranslationResult
{
    public required string Text { get; init; }

    public string? SourceLanguage { get; init; }

    public required string TargetLanguage { get; init; }

    public required string Model { get; init; }
}

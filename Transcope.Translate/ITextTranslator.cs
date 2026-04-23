namespace Transcope.Translate;

public interface ITextTranslator
{
    ValueTask<TranslationResult> TranslateAsync(
        string text,
        TranslationRequest? request = null,
        CancellationToken cancellationToken = default);
}

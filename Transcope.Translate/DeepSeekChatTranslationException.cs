namespace Transcope.Translate;

public sealed class DeepSeekChatTranslationException : Exception
{
    public DeepSeekChatTranslationException(string message)
        : base(message)
    {
    }

    public DeepSeekChatTranslationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

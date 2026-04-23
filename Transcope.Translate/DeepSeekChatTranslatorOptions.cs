namespace Transcope.Translate;

public sealed record DeepSeekChatTranslatorOptions
{
    public const string DefaultModel = "deepseek-chat";

    public static Uri DefaultEndpoint { get; } = new("https://api.deepseek.com/chat/completions");

    public string? ApiKey { get; init; }

    public Uri Endpoint { get; init; } = DefaultEndpoint;

    public string Model { get; init; } = DefaultModel;

    public double Temperature { get; init; } = 0;

    public int? MaxTokens { get; init; }
}

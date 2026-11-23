using Ake.Core;

namespace Ake.Ai;

/// <summary>
/// Google Antigravity / Gemini OpenAI-uyumlu uç nokta sağlayıcısı (hem resmi <c>generativelanguage.googleapis.com</c>
/// bulut uç noktasını hem yerel Antigravity vekil adresini destekler).
/// </summary>
public sealed class AntigravityChatProvider : IChatProvider
{
    public const string DefaultAddress = "https://generativelanguage.googleapis.com/v1beta/openai/";
    public const string DefaultModel = "gemini-2.5-pro";
    public const string KeyVariable = "ANTIGRAVITY_API_KEY";

    public static IReadOnlyList<string> KeyVariables { get; } =
    [
        KeyVariable,
        "GEMINI_API_KEY",
        OpenAiChatProvider.KeyVariable,
    ];

    public static IReadOnlyList<string> PresetModels { get; } =
    [
        "gemini-2.5-pro",
        "gemini-2.5-flash",
        "gemini-2.0-flash",
        "claude-sonnet-4-5",
    ];

    private readonly OpenAiChatProvider _inner;

    public AntigravityChatProvider(
        string model = DefaultModel,
        string address = DefaultAddress,
        HttpClient? client = null,
        string? apiKey = null)
    {
        _inner = new OpenAiChatProvider(model, address, client, apiKey, KeyVariables);
    }

    public Task<Result<string>> CompleteAsync(ChatRequest request, CancellationToken cancellation = default) =>
        _inner.CompleteAsync(request, cancellation);

    public static Task<Result<IReadOnlyList<string>>> ListModelsAsync(
        string address = DefaultAddress,
        HttpClient? client = null,
        string? apiKey = null,
        CancellationToken cancellation = default) =>
        OpenAiChatProvider.ListModelsAsync(address, client, cancellation, apiKey, KeyVariables);
}

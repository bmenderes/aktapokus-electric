using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ake.Core;

namespace Ake.Ai;

/// <summary>
/// Anthropic Claude Messages API (<c>/v1/messages</c>) sağlayıcısı. Anahtar pencere oturumundan veya
/// <c>ANTHROPIC_API_KEY</c> / <c>AKE_LLM_API_KEY</c> ortam değişkeninden okunur; dosyaya yazılmaz ve yalnızca HTTPS
/// ya da yerel loopback adresine gönderilir.
/// </summary>
public sealed class AnthropicChatProvider : IChatProvider
{
    public const string DefaultAddress = "https://api.anthropic.com";
    public const string DefaultModel = "claude-sonnet-4-5";
    public const string KeyVariable = "ANTHROPIC_API_KEY";
    public const string FallbackKeyVariable = "AKE_LLM_API_KEY";
    public const string ApiVersion = "2023-06-01";

    public static IReadOnlyList<string> PresetModels { get; } =
    [
        "claude-sonnet-4-5",
        "claude-opus-4-1",
        "claude-3-7-sonnet-latest",
        "claude-3-5-haiku-latest",
    ];

    private readonly HttpClient _client;
    private readonly string _model;
    private readonly string? _apiKey;

    public AnthropicChatProvider(
        string model = DefaultModel,
        string address = DefaultAddress,
        HttpClient? client = null,
        string? apiKey = null)
    {
        _model = model;
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        _client = client ?? new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        _client.BaseAddress ??= OpenAiChatProvider.NormalizeBaseAddress(address);
    }

    public async Task<Result<string>> CompleteAsync(ChatRequest request, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var systemParts = new List<string>();
        var turns = new JsonArray();
        foreach (var msg in request.Messages)
        {
            if (string.Equals(msg.Role, "system", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(msg.Content))
                {
                    systemParts.Add(msg.Content);
                }

                continue;
            }

            var role = string.Equals(msg.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user";
            turns.Add((JsonNode)new JsonObject { ["role"] = role, ["content"] = msg.Content });
        }

        if (!string.IsNullOrWhiteSpace(request.JsonSchema))
        {
            systemParts.Add(
                "Yanıtını yalnızca aşağıdaki JSON şemasına uyan tek bir JSON nesnesi olarak ver; kod çiti veya ek açıklama yazma:\n"
                + request.JsonSchema);
        }

        var body = new JsonObject
        {
            ["model"] = _model,
            ["max_tokens"] = 4096,
            ["temperature"] = 0,
            ["messages"] = turns,
        };
        if (systemParts.Count > 0)
        {
            body["system"] = string.Join("\n\n", systemParts);
        }

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, OpenAiChatProvider.EndpointPath(_client.BaseAddress, "messages"))
            {
                Content = JsonContent.Create(body),
            };
            Authorize(message, _client.BaseAddress, _apiKey);
            using var response = await _client.SendAsync(message, cancellation).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
                return Result.Failure<string>(new Error("ai.sunucu_hatasi", $"Claude {(int)response.StatusCode} döndü: {detail}"));
            }

            var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellation).ConfigureAwait(false);
            var text = string.Concat(
                (json?["content"] as JsonArray ?? [])
                .Where(block => string.Equals(block?["type"]?.GetValue<string>(), "text", StringComparison.OrdinalIgnoreCase))
                .Select(block => block?["text"]?.GetValue<string>() ?? string.Empty));
            text = StripCodeFences(text);
            return string.IsNullOrWhiteSpace(text)
                ? Result.Failure<string>(new Error("ai.bos_yanit", "Claude boş yanıt verdi."))
                : Result.Success(text);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Result.Failure<string>(new Error(
                "ai.sunucuya_ulasilamadi", $"Claude adresine ulaşılamadı ({_client.BaseAddress}): {ex.Message}"));
        }
    }

    public static async Task<Result<IReadOnlyList<string>>> ListModelsAsync(
        string address = DefaultAddress,
        HttpClient? client = null,
        string? apiKey = null,
        CancellationToken cancellation = default)
    {
        var http = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.BaseAddress ??= OpenAiChatProvider.NormalizeBaseAddress(address);
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, OpenAiChatProvider.EndpointPath(http.BaseAddress, "models"));
            Authorize(message, http.BaseAddress, apiKey);
            using var response = await http.SendAsync(message, cancellation).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellation).ConfigureAwait(false);
            var names = (json?["data"] as JsonArray)?
                .Select(model => model?["id"]?.GetValue<string>())
                .OfType<string>()
                .Order(StringComparer.Ordinal)
                .ToList() ?? [];
            return Result.Success<IReadOnlyList<string>>(names.Count > 0 ? names : [.. PresetModels]);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Result.Failure<IReadOnlyList<string>>(new Error(
                "ai.sunucuya_ulasilamadi",
                $"Claude model listesi alınamadı ({http.BaseAddress}, anahtar: {KeyVariable} / {FallbackKeyVariable}): {ex.Message}"));
        }
        finally
        {
            if (client is null)
            {
                http.Dispose();
            }
        }
    }

    private static void Authorize(HttpRequestMessage message, Uri? baseAddress, string? explicitKey)
    {
        message.Headers.TryAddWithoutValidation("anthropic-version", ApiVersion);
        if (!OpenAiChatProvider.IsKeyAllowedOverTransport(baseAddress))
        {
            return;
        }

        if (OpenAiChatProvider.ResolveApiKey(explicitKey, KeyVariable, FallbackKeyVariable) is { } key)
        {
            message.Headers.TryAddWithoutValidation("x-api-key", key);
        }
    }

    private static string StripCodeFences(string raw)
    {
        var trimmed = raw.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewLine = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewLine >= 0 && lastFence > firstNewLine
            ? trimmed[(firstNewLine + 1)..lastFence].Trim()
            : trimmed;
    }
}

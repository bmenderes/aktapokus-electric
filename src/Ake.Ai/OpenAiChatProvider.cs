using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ake.Core;

namespace Ake.Ai;

/// <summary>
/// OpenAI uyumlu <c>/v1/chat/completions</c> uç noktası (LM Studio, vLLM, llama.cpp sunucusu, bulut sağlayıcılar). Anahtar gerekiyorsa
/// <c>AKE_LLM_API_KEY</c> ortam değişkeninden okunur; ayar dosyasına yazılmaz. Çıktı JSON şemasıyla kısıtlanır (<c>response_format</c>).
/// </summary>
public sealed class OpenAiChatProvider : IChatProvider
{
    public const string KeyVariable = "AKE_LLM_API_KEY";

    private readonly HttpClient _client;
    private readonly string _model;
    private readonly string? _apiKey;
    private readonly string[] _keyVariables;

    public OpenAiChatProvider(
        string model,
        string address,
        HttpClient? client = null,
        string? apiKey = null,
        IReadOnlyList<string>? keyVariables = null)
    {
        _model = model;
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        _keyVariables = keyVariables is { Count: > 0 } ? [.. keyVariables] : [KeyVariable];
        _client = client ?? new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        _client.BaseAddress ??= NormalizeBaseAddress(address);
    }

    public async Task<Result<string>> CompleteAsync(ChatRequest request, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = new JsonObject
        {
            ["model"] = _model,
            ["temperature"] = 0,
            ["messages"] = new JsonArray([.. request.Messages.Select(
                message => (JsonNode)new JsonObject { ["role"] = message.Role, ["content"] = message.Content })]),
        };
        if (request.JsonSchema is not null)
        {
            body["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = "patch", ["schema"] = JsonNode.Parse(request.JsonSchema) },
            };
        }

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, EndpointPath(_client.BaseAddress, "chat/completions"))
            {
                Content = JsonContent.Create(body),
            };
            Authorize(message, _client.BaseAddress, _apiKey, _keyVariables);
            using var response = await _client.SendAsync(message, cancellation).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
                return Result.Failure<string>(new Error("ai.sunucu_hatasi", $"Sağlayıcı {(int)response.StatusCode} döndü: {detail}"));
            }

            var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellation).ConfigureAwait(false);
            var content = json?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(content)
                ? Result.Failure<string>(new Error("ai.bos_yanit", "Model boş yanıt verdi."))
                : Result.Success(content);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Result.Failure<string>(new Error(
                "ai.sunucuya_ulasilamadi", $"Sağlayıcıya ulaşılamadı ({_client.BaseAddress}): {ex.Message}"));
        }
    }

    /// <summary>Sağlayıcıdaki model adları (<c>/v1/models</c> veya taban yolda <c>/models</c>).</summary>
    public static async Task<Result<IReadOnlyList<string>>> ListModelsAsync(
        string address,
        HttpClient? client = null,
        CancellationToken cancellation = default,
        string? apiKey = null,
        IReadOnlyList<string>? keyVariables = null)
    {
        var http = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.BaseAddress ??= NormalizeBaseAddress(address);
        var keys = keyVariables is { Count: > 0 } ? [.. keyVariables] : new[] { KeyVariable };
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, EndpointPath(http.BaseAddress, "models"));
            Authorize(message, http.BaseAddress, string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim(), keys);
            using var response = await http.SendAsync(message, cancellation).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellation).ConfigureAwait(false);
            var names = (json?["data"] as JsonArray)?.Select(model => model?["id"]?.GetValue<string>()).OfType<string>().Order(StringComparer.Ordinal).ToList() ?? [];
            return Result.Success<IReadOnlyList<string>>(names);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Result.Failure<IReadOnlyList<string>>(new Error("ai.sunucuya_ulasilamadi", $"Model listesi alınamadı ({http.BaseAddress}): {ex.Message}"));
        }
        finally
        {
            if (client is null)
            {
                http.Dispose();
            }
        }
    }

    internal static Uri NormalizeBaseAddress(string address)
    {
        var trimmed = address.Trim();
        var uri = new Uri(trimmed, UriKind.Absolute);
        var path = uri.AbsolutePath.TrimEnd('/');
        if (!trimmed.EndsWith('/')
            && (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/openai", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/v1beta/openai", StringComparison.OrdinalIgnoreCase)))
        {
            return new Uri(trimmed + "/", UriKind.Absolute);
        }

        return uri;
    }

    internal static string EndpointPath(Uri? baseAddress, string resource)
    {
        var path = baseAddress?.AbsolutePath.TrimEnd('/') ?? string.Empty;
        return path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/openai", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/v1beta/openai", StringComparison.OrdinalIgnoreCase)
            ? resource
            : $"v1/{resource}";
    }

    internal static bool IsKeyAllowedOverTransport(Uri? baseAddress) =>
        baseAddress is not null
        && (string.Equals(baseAddress.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || baseAddress.IsLoopback
            || baseAddress.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));

    internal static string? ResolveApiKey(string? explicitKey, params string[] keyVariables)
    {
        if (!string.IsNullOrWhiteSpace(explicitKey))
        {
            return explicitKey.Trim();
        }

        foreach (var variable in keyVariables)
        {
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value)
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static void Authorize(HttpRequestMessage message, Uri? baseAddress, string? explicitKey, string[] keyVariables)
    {
        // API anahtarı düz HTTP üzerinden uzak sunucuya sızdırılmaz; yalnızca HTTPS veya yerel loopback adreslerine eklenir.
        if (!IsKeyAllowedOverTransport(baseAddress))
        {
            return;
        }

        if (ResolveApiKey(explicitKey, keyVariables) is { } key)
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }
    }
}

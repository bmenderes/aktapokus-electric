using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ake.Core;

namespace Ake.Ai;

/// <summary>
/// Yerel Ollama sunucusuna (<c>/api/chat</c>) bağlanır. Proje verisi yalnızca verilen adrese gider; varsayılan adres yereldir.
/// CPU'da yanıt dakikalar sürebilir, bu yüzden zaman aşımı uzundur.
/// </summary>
public sealed class OllamaChatProvider : IChatProvider
{
    public const string DefaultAddress = "http://127.0.0.1:11434";
    public const string DefaultModel = "qwen3:8b";

    /// <summary>Model son istekten sonra bu süre bellekte kalır; sonraki istekler CPU'da dakikalar süren soğuk yüklemeyi beklemez.</summary>
    public const string KeepAlive = "30m";

    private readonly HttpClient _client;
    private readonly string _model;

    public OllamaChatProvider(string model = DefaultModel, string address = DefaultAddress, HttpClient? client = null)
    {
        _model = model;
        _client = client ?? new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        _client.BaseAddress ??= new Uri(address);
    }

    /// <summary>
    /// Modeli önceden belleğe yükler (boş istek). Pencere açılırken çağrılırsa ilk gerçek istek soğuk yüklemeyi beklemez.
    /// Hata önemsizdir: model sonra yine yüklenir.
    /// </summary>
    public async Task<Result> PreloadAsync(CancellationToken cancellation = default)
    {
        try
        {
            var body = new JsonObject { ["model"] = _model, ["keep_alive"] = KeepAlive, ["stream"] = false };
            using var response = await _client.PostAsJsonAsync("/api/generate", body, cancellation).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? Result.Success()
                : Result.Failure(new Error("ai.on_yukleme_basarisiz", $"Model ön yüklenemedi: {(int)response.StatusCode}"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Result.Failure(new Error("ai.sunucuya_ulasilamadi", $"Ollama'ya ulaşılamadı: {ex.Message}"));
        }
    }

    /// <summary>Ollama'da kurulu model adları (<c>/api/tags</c>); model seçicisi için.</summary>
    public static async Task<Result<IReadOnlyList<string>>> ListModelsAsync(string address = DefaultAddress, HttpClient? client = null, CancellationToken cancellation = default)
    {
        var http = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.BaseAddress ??= new Uri(address);
        try
        {
            var json = await http.GetFromJsonAsync<JsonObject>("/api/tags", cancellation).ConfigureAwait(false);
            var names = (json?["models"] as JsonArray)?.Select(model => model?["name"]?.GetValue<string>()).OfType<string>().Order(StringComparer.Ordinal).ToList() ?? [];
            return Result.Success<IReadOnlyList<string>>(names);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Result.Failure<IReadOnlyList<string>>(new Error("ai.sunucuya_ulasilamadi", $"Ollama model listesi alınamadı ({http.BaseAddress}): {ex.Message}"));
        }
        finally
        {
            if (client is null)
            {
                http.Dispose();
            }
        }
    }

    public async Task<Result<string>> CompleteAsync(ChatRequest request, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = new JsonObject
        {
            ["model"] = _model,
            ["stream"] = false,
            ["think"] = false, // düşünme dökümü yok: çıktı yalnızca şemaya uyan JSON olmalı
            ["keep_alive"] = KeepAlive,
            ["options"] = new JsonObject { ["temperature"] = 0, ["num_ctx"] = 8192, ["num_predict"] = 1536 }, // üst sınır: kaçan üretim dakikalarca sürmesin
            ["messages"] = new JsonArray([.. request.Messages.Select(
                message => (JsonNode)new JsonObject { ["role"] = message.Role, ["content"] = message.Content })]),
        };
        if (request.JsonSchema is not null)
        {
            body["format"] = JsonNode.Parse(request.JsonSchema);
        }

        try
        {
            using var response = await _client.PostAsJsonAsync("/api/chat", body, cancellation).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
                return Result.Failure<string>(new Error("ai.sunucu_hatasi", $"Ollama {(int)response.StatusCode} döndü: {detail}"));
            }

            var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellation).ConfigureAwait(false);
            var content = json?["message"]?["content"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(content)
                ? Result.Failure<string>(new Error("ai.bos_yanit", "Model boş yanıt verdi."))
                : Result.Success(content);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Result.Failure<string>(new Error(
                "ai.sunucuya_ulasilamadi", $"Ollama'ya ulaşılamadı ({_client.BaseAddress}): {ex.Message}. Ollama çalışıyor ve '{_model}' kurulu mu?"));
        }
    }
}

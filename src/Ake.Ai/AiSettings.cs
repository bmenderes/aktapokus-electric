using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ake.Core;

namespace Ake.Ai;

/// <summary>
/// Yapay zeka sağlayıcı ayarı (ADR-0044). <see cref="Provider"/> <c>ollama</c> (yerel, varsayılan), <c>claude</c> (Anthropic),
/// <c>antigravity</c> (Google Antigravity / Gemini) ya da <c>openai</c> (OpenAI uyumlu uç nokta) olur. Uzak bir adres seçilirse
/// proje özeti o adrese gider; arayüz bunu uyarır. Anahtar diske yazılmaz; yalnızca bellek içi oturumdan veya ortam değişkeninden okunur.
/// </summary>
public sealed record AiSettings(
    string Provider = AiSettings.Ollama,
    string Address = OllamaChatProvider.DefaultAddress,
    string Model = OllamaChatProvider.DefaultModel,
    [property: JsonIgnore] string? ApiKey = null)
{
    public const string Ollama = "ollama";
    public const string OpenAi = "openai";
    public const string Claude = "claude";
    public const string Antigravity = "antigravity";

    public static AiSettings Default { get; } = new();

    /// <summary>Adres bu bilgisayarı gösteriyor mu (veri makineden çıkmıyor mu).</summary>
    public bool IsLocal =>
        Uri.TryCreate(Address, UriKind.Absolute, out var uri) && (uri.IsLoopback || uri.Host == "localhost");

    public Error? Check() =>
        Provider is not (Ollama or OpenAi or Claude or Antigravity)
            ? new Error("ai.saglayici_gecersiz", $"Bilinmeyen sağlayıcı '{Provider}'. Sağlayıcılar: {Ollama}, {Claude}, {Antigravity}, {OpenAi}")
        : !Uri.TryCreate(Address, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            ? new Error("ai.adres_gecersiz", $"Adres http ya da https olmalı: '{Address}'")
        : string.IsNullOrWhiteSpace(Model) ? new Error("ai.model_bos", "Model adı boş olamaz.")
        : null;

    /// <summary>Dosyadan okur; dosya yoksa ya da bozuksa varsayılan döner (ayar yüzünden uygulama açılışı engellenmez).</summary>
    public static AiSettings Load(string path)
    {
        try
        {
            var loaded = JsonSerializer.Deserialize<AiSettings>(File.ReadAllText(path, Encoding.UTF8), Options);
            return loaded is not null && loaded.Check() is null ? loaded : Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Default;
        }
    }

    public Result Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Options), new UTF8Encoding(false));
            return Result.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure(new Error("ai.ayar_yazilamadi", $"Yapay zeka ayarı yazılamadı: {ex.Message}"));
        }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

/// <summary>Ayara göre sağlayıcı kurar ve sağlayıcıdaki modelleri listeler.</summary>
public static class ChatProviders
{
    public static IChatProvider Create(AiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Provider switch
        {
            AiSettings.OpenAi => new OpenAiChatProvider(settings.Model, settings.Address, apiKey: settings.ApiKey),
            AiSettings.Claude => new AnthropicChatProvider(settings.Model, settings.Address, apiKey: settings.ApiKey),
            AiSettings.Antigravity => new AntigravityChatProvider(settings.Model, settings.Address, apiKey: settings.ApiKey),
            _ => new OllamaChatProvider(settings.Model, settings.Address),
        };
    }

    /// <summary>Sağlayıcıdaki model adları (seçici için). Ulaşılamazsa hata döner; kullanıcı model adını elle yazabilir.</summary>
    public static Task<Result<IReadOnlyList<string>>> ListModelsAsync(AiSettings settings, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Provider switch
        {
            AiSettings.OpenAi => OpenAiChatProvider.ListModelsAsync(settings.Address, cancellation: cancellation, apiKey: settings.ApiKey),
            AiSettings.Claude => AnthropicChatProvider.ListModelsAsync(settings.Address, apiKey: settings.ApiKey, cancellation: cancellation),
            AiSettings.Antigravity => AntigravityChatProvider.ListModelsAsync(settings.Address, apiKey: settings.ApiKey, cancellation: cancellation),
            _ => OllamaChatProvider.ListModelsAsync(settings.Address, cancellation: cancellation),
        };
    }
}

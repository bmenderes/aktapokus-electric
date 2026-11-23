using Ake.Core;

namespace Ake.Ai;

/// <summary>Sohbetteki bir mesaj. <see cref="Role"/>: <c>system</c>, <c>user</c> ya da <c>assistant</c>.</summary>
public sealed record ChatMessage(string Role, string Content);

/// <summary>
/// Bir sohbet isteği. <see cref="JsonSchema"/> verilirse sağlayıcı çıktıyı bu JSON şemasına uymaya zorlar (küçük yerel modeller için
/// geçerli JSON üretmenin tek güvenilir yolu).
/// </summary>
public sealed record ChatRequest(IReadOnlyList<ChatMessage> Messages, string? JsonSchema = null);

/// <summary>Dil modeli sağlayıcısı (ADR-0044). Yerel Ollama varsayılandır; testler sahte sağlayıcı kullanır.</summary>
public interface IChatProvider
{
    Task<Result<string>> CompleteAsync(ChatRequest request, CancellationToken cancellation = default);
}

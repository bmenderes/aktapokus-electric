# Yapay zeka değerlendirmesi (ADR-0044)

Örnek proje `examples/ref-machine`, değerlendirme seti [`examples/ai/degerlendirme.json`](../examples/ai/degerlendirme.json) (9 vaka; her birinin
elle yazılmış referans çözümü modelsiz testle doğrulanır, istemler few-shot örneklerinden farklıdır). Model: `qwen3:8b` (Ollama), bu bilgisayar,
yalnızca CPU. Yeniden çalıştırma: `$env:AKE_LIVE_LLM='1'; dotnet test tests/Ake.Ai.Tests --filter Category=Live`
(`AKE_LLM_MODEL` modeli, `AKE_LLM_PLAIN=1` bilgi tabanını kapatır).

Bir vaka, öneri hem doğrulamadan (şema, uygulanabilirlik, yeni ERC hatası yok) hem de vakanın beklentisinden (eklenen cihaz/bağlantı sayısı,
var/yok olması gereken tag, tel numarası) geçerse geçer.

## Sonuç

| Tasarım | Geçen | Vaka başına süre | Not |
|---|---|---|---|
| Bilgi tabanı kapalı (yalnızca sistem istemi + proje özeti) | 9/9 | 17–58 sn | ilk vaka soğuk model yüklemesi: 220 sn |
| Örnekler ve notlar **ilk istekte** | 6/9 | 217–321 sn | 3 ekleme vakasında `connect` komutu atlandı |
| **Bilgi tabanı yalnızca yeniden denemede** (uygulanan) | **9/9** | 17–66 sn | ilk vaka soğuk yükleme: 247 sn; 2 vaka 2. denemede |

## Çıkarımlar

- İlk isteğe örnek eklemek bu modelde **zarar verdi**: istek her seferinde farklı bir başlangıçla geldiği için Ollama'nın ortak istem
  önbelleği bozuldu (CPU'da ~3500 token'ı her seferinde baştan işlemek ≈ 4 dk) ve model örnekleri taklit edip bağlantıyı atladı.
- Bu yüzden ilk istek kısa ve değişmez tutulur; doğrulama başarısız olursa hata mesajıyla birlikte ilgili örnek ve notlar geri verilir
  (RAG yalnızca gerektiğinde, yeniden denemede).
- Hızın asıl kaldıracı **modelin bellekte kalması**dır (`keep_alive`): ilk istek 4 dk sürerken sonrakiler saniyeler sürer. Pencere
  açılırken model önceden yüklenir.
- 9 vaka küçük bir kıyas; daha zor, çok adımlı istemler (örn. "3 motorlu konveyör") ve örnek PDF proje geldikçe set genişletilir.

# Yapay zeka ile değişiklik (isteğe bağlı)

Devre şemasında istem yazarak değişiklik yapılabilir: **Araçlar > Yapay zeka > Yapay zeka ile değiştir**. Özellik isteğe bağlıdır;
kurulu değilse editörün geri kalanı etkilenmez. Tasarım: [ADR-0044](adr/0044-yerel-llm-tasarimi.md).

## Kurulum notu (kurulum dosyasına konacak)

- Varsayılan sağlayıcı **Ollama**dır ve ürünle birlikte gelmez; ayrıca kurulur (<https://ollama.com>), model indirilir:
  `ollama pull qwen3:8b`. Model yaklaşık 5 GB yer ve bellek tutar; ekran kartı gerekmez, ama yalnızca işlemcide yanıt
  **dakikalar** sürebilir.
- Ollama kapalıysa ya da model kurulu değilse pencere bunu açıkça söyler; editörün diğer işlevleri çalışmaya devam eder.
- LLM yalnızca elektrik devre şeması için kullanılır; model eğitilmez ya da ince ayar yapılmaz.

## Nasıl çalışır

1. İstek yazılır (örn. "=M1+P1 altına K7 kontaktör bobini ekle ve A2 ucunu -T1:- noktasına bağla").
2. Model bir **patch** önerir. Öneri projenin kopyasında denenir ve ERC'den geçmezse modele geri verilip en çok 3 kez yeniden denenir.
3. Değişiklikler okunur listede gösterilir. **Uygula** denmeden projeye hiçbir şey yazılmaz.
4. Uygulama tek adımdır, kaynak "yapay zeka" olarak denetim kaydına yazılır ve **Geri Al** ile geri alınır.

## Model seçimi

Pencerede sağlayıcı ve model seçilir; **Modelleri yenile** Ollama'daki kurulu modelleri listeler. Seçim kullanıcı profilindeki
`yapay-zeka.json` dosyasında saklanır. **OpenAI uyumlu uç nokta** (LM Studio, vLLM, llama.cpp sunucusu, bulut sağlayıcılar) da
seçilebilir. Adres bu bilgisayar değilse proje özeti (cihaz ve bağlantı adları) o adrese gider ve pencere bunu uyarır; anahtar
dosyaya yazılmaz, `AKE_LLM_API_KEY` ortam değişkeninden okunur.

Komut satırı: `ake ai <proje.json> "<istem>" [--provider ollama|openai] [--model ...] [--address ...] --out oneri.json`; öneriyi
inceleyip `ake apply` ile uygulayın.

## Gerçek model testi

Yavaştır ve Ollama gerektirir; yalnızca istenirse çalışır:

```powershell
$env:AKE_LIVE_LLM = '1'; dotnet test tests/Ake.Ai.Tests --filter "Category=Live"
```

Bu bilgisayarda `qwen3:8b` ile (yalnızca CPU) üç istem de doğrulandı: cihaz ekleyip bağlama (~50 sn, 1. deneme), bağlantı silme
(~25 sn, 1. deneme) ve etiket değiştirme (~30 sn, 2. deneme).

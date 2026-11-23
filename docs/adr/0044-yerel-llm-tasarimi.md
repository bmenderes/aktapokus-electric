# ADR-0044: Yerel LLM ile istem yazarak değişiklik (tasarım)

Durum: kabul edildi (2026-11-18, proje sahibi: Ollama kurulu; LLM yalnızca elektrik devre şemasında kullanılacak, model geliştirilmeyecek). İlk dilim uygulandı.

## Bağlam
Proje sahibi, yerel bir LLM'e istem yazarak şemada değişiklik yapmak ve LLM'in projenin dokümanlarına (komut şeması, ADR'ler, örnek projeler) dayanmasını istiyor.
Donanım: 32 GB RAM, ayrık ekran kartı yok (yalnızca CPU). ADR-0001: hedef makinede ek kurulum gerektirme; AI isteğe bağlı modüldür.

## Değişmeyenler (ADR-0004, ADR-0001)
- LLM modeli doğrudan değiştirmez; yalnızca **patch önerir**. Patch ya tamamen uygulanır ya hiç, tek geri alma adımıyla, kaynak `ai` olarak audit'e yazılır.
- Nesnelere tag ile atıf; ürünler sipariş koduyla kütüphaneden. LLM GUID ya da uç adı uydurmaz.
- Çizimi LLM yapmaz; yerleşimi auto-layout yapar.

## Kapsam kararı (proje sahibi)
- Yerel LLM **yalnızca elektrik devre şeması** işleri içindir (cihaz/bağlantı/numara/sayfa değişikliği önerisi). Genel sohbet, kod yazma vb. kapsam dışı.
- **Model geliştirilmez:** eğitim, ince ayar (fine-tuning) ya da model dağıtımı yok. Hazır bir Ollama modeli kullanılır; davranış istem, JSON şeması, RAG ve doğrulama döngüsüyle sağlanır. Model adı ayardır (`--model`, varsayılan `qwen3:8b`).
- Sağlayıcı: **Ollama** (kullanıcının makinesinde kurulu, `127.0.0.1:11434`). llama.cpp kararı gerekmedi.

## Karar

### 1. Çalışma zamanı (CPU)
- Model **ayrı bir süreçte** çalışır; uygulama OpenAI uyumlu yerel HTTP uç noktasına (`127.0.0.1`) konuşur. Varsayılan sağlayıcı **Ollama** ya da **llama.cpp `llama-server`**; ikisi de aynı uç noktayı verdiği için sağlayıcı soyutlaması tek arayüzdür (`IChatProvider`). Harici API opsiyonel yedek (yol haritasında var), varsayılan kapalı.
- Uygulama yalnızca modül açıkken ve yapılandırılmış uç nokta cevap verirken AI komutlarını gösterir. Çalışma zamanı ve model **ürünle birlikte dağıtılmaz**; kullanıcı ayrıca kurar (self-contained kuralının bilinçli istisnası, isteğe bağlı). Alternatif: `llama-server` ikilisini ve modeli kurulum paketine "AI eklentisi" olarak ayrı indirme yapmak.
- CPU'da 7–8B modeller Q4 kuantizasyonda ~5 GB RAM tutar ve çoğu masaüstü CPU'da saniyede yalnızca ~3–8 token üretir. Bir patch (~300–600 token) bu yüzden **dakika** sürebilir; bu beklenir ve arayüzde ilerleme gösterilir. Daha küçük (3–4B) modeller 2–3 kat hızlıdır ama JSON şemasını kaçırma oranı artar.
- Model seçimi ölçümle yapılır (aşağıda "Değerlendirme"); önerilen aday sınıfı: 7B civarı talimat/kod modeli (Qwen2.5-7B-Instruct, benzeri). Model adı koda gömülmez, ayardır. Lisansı ticari kullanıma uygun olmalı (Apache-2.0/MIT sınıfı); GPL/benzeri kısıt yok.

### 2. Geçerli patch üretimi
- Çıktı **JSON şeması / grammar ile kısıtlanır** (llama.cpp GBNF ya da Ollama `format` şeması): yalnızca `PatchDto` işlem türleri (`addDevice`, `connect`, `assignWire`, `retag`, `assignIo`, `setProperty`, `addPage`, `assignSymbol`…).
- Döngü: **üret → şema doğrula → dry-run (`apply --dry-run` mantığı) + ERC → hata metnini modele geri ver → en çok 3 deneme**. Başarısızsa kullanıcıya hata ve son denenen patch gösterilir, hiçbir şey uygulanmaz.
- Büyük istem tek patch yerine adımlara bölünür (önce cihaz listesi, sonra bağlantılar); her adım ayrı doğrulanır.

### 3. Bağlam ve RAG
- **Proje özeti:** modelin tam JSON'u yerine kısa, tag'li özet (sayfalar, cihazlar, tag/tür/ürün, bağlantılar, boş uçlar, ERC bulguları). Bağlam penceresine sığacak şekilde istem anına göre ilgili kısım seçilir.
- **Doküman RAG:** komut/patch şeması ve örnekleri, ADR özetleri, standart sembol ve kütüphane arama sonuçları. Dizin küçük (yüzlerce parça): yerel gömme modeli (örn. küçük çok dilli bir sentence-embedding) ya da ilk aşamada **SQLite FTS5 ile anahtar sözcük araması** — kütüphane zaten SQLite. Önce FTS5, ölçüm yetersiz kalırsa gömme eklenir (spekülatif soyutlama yok).
- **Ürün seçimi** yalnızca kütüphaneden: model "ABB 16A kontaktör" der, sistem `lib` aramasıyla sipariş kodunu çözer; bulunamazsa soruyu kullanıcıya geri verir.

### 4. Onay akışı (editör)
- Istem kutusu → "Öneri hazırlanıyor" → **fark görünümü** (eklenen/silinen cihaz ve bağlantılar, ERC öncesi/sonrası) → kısmi kabul → tek komutla uygula, tek Geri Al ile geri al.
- CLI: `ake ai "<istem>" <proje.json> --dry-run` (patch'i yazdırır), `--apply` yalnızca onayla.
- Gizlilik: yerel uç nokta dışına hiçbir proje verisi gönderilmez; harici API açıksa uyarı gösterilir.

### 5. Değerlendirme (kabul ölçütü)
- `tests/Ake.Ai.Tests`: sahte sağlayıcıyla döngü, şema/ERC reddi, deneme sınırı, atomiklik (gerçek model olmadan, hızlı).
- Ayrı, elle çalıştırılan **değerlendirme seti** (≈20 istem: "3 motorlu konveyör…", "Q1'e termik ekle", "bu telleri numarala"): gerçek modelle doğru patch oranı ve süre ölçülür; sonuç `docs/ai-degerlendirme.md`'ye yazılır. Model/ayar bu orana göre seçilir.
- DoD (ROADMAP Faz 9): örnek metinden onaylanabilir, ERC-temiz patch.

## İlk dilim (uygulandı: `Ake.Ai`, `ake ai`)
1. `Ake.Ai`: `IChatProvider` + OpenAI uyumlu HTTP istemcisi, şema kısıtlı üretim, doğrulama döngüsü, sahte sağlayıcılı testler.
2. Proje özeti ve FTS5 doküman dizini.
3. `ake ai` komutu.
4. Editörde istem paneli ve fark görünümü.
Her adım ayrı commit; gerçek model ölçümü kullanıcının makinesinde.

## Açık sorular
1. ~~Ollama mı llama.cpp mi?~~ Ollama (kurulu). ~~Dağıtım~~: kullanıcı ayrıca kurar, ürünle birlikte gelmez.
3. Örnek PDF proje: RAG ve değerlendirme setine **örnek** olarak girecek; müşteri verisi içeriyorsa dizine alınmadan yalnızca yerel kalır (`Ornek/` kuralı).
4. Dakikalar süren yanıt süresi kabul edilebilir mi, yoksa daha küçük model/uzak API tercih edilir mi?

## Uygulama notu: bilgi tabanı yalnızca yeniden denemede (2026-11-19)
Örnek ve notlar ilk isteğe konulunca (RAG + few-shot) gerçek modelle ölçüm kötüleşti: 6/9 vaka ve ~4 kat yavaşlık (Ollama'nın ortak istem
önbelleği bozuldu, model bağlantı komutlarını atladı). Karar: ilk istek kısa ve değişmez (sistem istemi + proje özeti + istek); bilgi tabanı
(`Knowledge/bilgi.json`: doğrulanmış örnekler ve kural notları) yalnızca doğrulama başarısız olursa hata mesajıyla birlikte modele geri verilir.
Sonuç 9/9. Ayrıntı ve ölçümler: `docs/ai-degerlendirme.md`. Model eğitimi/ince ayar yapılmadı (karar değişmedi).

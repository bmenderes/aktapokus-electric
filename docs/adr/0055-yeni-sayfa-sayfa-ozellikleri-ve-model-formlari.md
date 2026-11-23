# ADR-0055: Yeni sayfa / sayfa özellikleri pencereleri ve kalan model formları

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-20
- **İlişki:** ADR-0031 (model komutları), ADR-0046 (sayfa türleri), ADR-0047 (proje özellikleri), ADR-0054 (görsel sembol seçimi), ROADMAP Faz 6 ve Faz 6d-2

## Bağlam
1. `NewPageWindow` yalnızca sayfa türü, başlık, konum ve şablon soruyordu; tam sayfa adı (`=Fonksiyon+Yer/Numara`), sayfa numarası, fonksiyon/yer düğümü ve ölçek/ızgara özeti düzenlenemiyor, var olan bir sayfa için sayfa özellikleri penceresi bulunmuyordu (Faz 6d-2).
2. ADR-0031 ile çekirdek komutları yazılmış olan **yapı yönetimi (`=`/`+`)**, **kablo/damar tanımlama-atama** ve **potansiyel işareti (`L1`, `24VDC`, `0V`)** için etkileşimli editör formları henüz eklenmemişti (ROADMAP Faz 6).

## Karar
- **Çekirdek komutlar (`Ake.Core`):**
  - `SetPageDetails`: sayfa başlığı, fonksiyon düğümü (`=`), yer düğümü (`+`) ve şablonu geri alınabilir şekilde günceller.
  - `SetStructureNode` ve `RemoveStructureNode`: yapı düğümü adını/açıklamasını düzenler ve kullanılmayan düğümü siler.
  - `SetCable` ve `RemoveCable`: kablo tag/tip/damar listesini günceller; kablo silinince damarlarının bağlantılardaki atamalarını geri alınabilir biçimde temizler.
  - `RemovePotentialSource`: potansiyel işaretini geri alınabilir biçimde kaldırır.
- **Yeni sayfa ve sayfa özellikleri (`Ake.Editor`):**
  - `NewPageWindow` tam sayfa adı önizlemesi (`=Fonksiyon+Yer/Numara`), sayfa türü ve açıklaması, özel sayfa numarası, `=`/`+` düğümü seçimi, şablon ve `Ölçek: 1:1 · Izgara · Çerçeve` özetini gösterecek biçimde genişletildi; geriye dönük uyumlu kurucu korundu.
  - `PagePropertiesWindow` ile açık/seçili sayfanın numarası, başlığı, `=`/`+` yapı düğümleri ve şablonu `SetPageNumber` + `SetPageDetails` üzerinden tek geri alma adımında düzenlenir; Sayfa şeridinde, sayfa bağlam menüsünde ve sayfa ağacı sağ tık menüsünde yer alır.
- **Kalan model formları:**
  - `ProjectPropertiesWindow` içinde **Yapı** sekmesi etkileşimli hâle getirildi (`=`/`+` düğümü ekleme, güncelleme, silme ve anlık istatistik/ağaç yenileme).
  - `CableWindow` (`Bağlantı > Kablo ve damar…` ve tel sağ tık menüsü): kablo/damar oluşturma, güncelleme, silme ve seçili bağlantıya fiziksel tel ya da kablo damarı atama.
  - `PotentialWindow` (`Bağlantı > Potansiyel işareti…`, sembol ve tel sağ tık menüleri): bağlantı noktasına `L1/L2/L3/N/PE/24VDC/0V` ya da özel potansiyel işareti ekleme ve silme.

## Doğrulama
`Ake.Core.Tests`, `Ake.Editor.Tests` birim testleri, `dotnet format --verify-no-changes` ve `Ake.EditorSnapshot` ekran görüntüleriyle görsel denetim.

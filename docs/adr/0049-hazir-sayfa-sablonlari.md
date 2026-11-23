# ADR-0049: Hazır sayfa şablonları

Durum: kabul edildi (2026-11-19, proje sahibi: "hazır sayfa şablonları"; Faz 6c madde 6)

## Bağlam
Yalnızca bir şablon vardı (A3 yatay). Yaygın E-CAD programları A4/A3/A2 ve yatay/dikey hazır şablonlarla gelir.

## Karar
- `StandardSheetTemplates` (Ake.Core): **A4 yatay (8 kolon), A4 dikey (6), A3 yatay (10), A3 dikey (8), A2 yatay (14)**. Kimlikler sabittir (`0193b000-…-a401/a402/a301/a302/a201`); aynı şablon farklı projelerde aynı şablon olarak tanınır. A3 yatay, eski varsayılanla (kimlik, ad, ölçüler) aynıdır ve `examples/templates/a3-yatay.sablon.json` ile birebir eşdeğerliği testle korunur; editördeki gömülü JSON kaynağı kaldırıldı.
- Yatay şablonlar A3'ün 20 mm'lik antetini iç genişliğe ölçekler; dikey şablonlar dar kâğıt için 30 mm'lik, üç satırlı antet kullanır. Antet içeriği hepsinde aynıdır (proje, başlık, çizim no, tarih, revizyon, çizen, fonksiyon, yer, sayfa, toplam sayfa; logo hücresi).
- Şablon projenin içinde saklanır (proje dosyası tek başına çıktı verir); şema ve dosya biçimi değişmez.
- **Editör:** Proje oluşturma penceresinde şablon seçilir (tüm sayfalar onu kullanır). Yeni sayfa penceresinde "Sayfa şablonu": önceki sayfayla aynı, projedeki şablonlar ya da projede olmayan hazır şablonlar; hazır şablon seçilirse sayfayla aynı adımda projeye eklenir (tek Geri Al).
- **CLI:** `ake template list` ve `ake template add <proje> <ad|kimlik>` (ad parçası tek şablona uymalı).
- Kapak sayfasının başlığı dar kâğıtta çerçeveden taşmaması için genişliğe göre küçülür.

## Bu dilimde olmayanlar
A1 ve A0, kullanıcının kendi şablonunu pencereden tasarlaması (şu an `ake template set` ile dosyadan), kapağa özel ızgarasız şablon, antette logo dosyasının seçimi.

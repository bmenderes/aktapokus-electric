# ADR-0051: Sayfa gezgini — alan bazlı süzgeç, liste görünümü ve grafik önizleme

Durum: kabul edildi (2026-11-19, proje sahibi: "sayfa gezgini"; Faz 6d)

## Karar
- **Süzgeç:** arama kutusunun yanına alan seçici: Tümü, Numara, Başlık, Tür, Fonksiyon, Yer, Şablon. Metin seçilen alanda Türkçe harflere ve büyük-küçük harfe duyarsız aranır (`=M1` ve `m1` aynı). *Tümü* eski davranıştır (numara, başlık, tür, fonksiyon, yer, şablon; ağaçta grup adı eşleşirse altındaki her şey kalır).
- **Ağaç / Liste:** paneldeki iki düğme. *Ağaç* =fonksiyon/+yer gruplarıyla (ADR-0023 sonrası mevcut görünüm); *Liste* sayfaları gruplamadan projedeki sırayla gösterir ve şema dışı sayfalarda türü yanına yazar ("6  Kapak  ·  Kapak"). Alan süzgeci ikisinde de çalışır; ağaçta uyan sayfalar ve onlara giden gruplar (hepsi açık) kalır.
- **Grafik önizleme:** panelin altında seçili sayfanın küçük resmi (salt okunur çizim alanı; sayfa açık olmasa da görünür, düzenlemeyle güncel kalır).
- Dosya biçimi ve model değişmez; hepsi arayüz ve oturum tarafıdır (`PagesMatching`, `PageListRows`, `PageTreeRows(filter, collapsed, field)`).

## Bu dilimde olmayanlar
Sütunlu liste (numara/başlık/tür ayrı sütun), sütuna göre sıralama, birden çok alan koşulunu birleştirme, önizlemeye tıklayınca sayfayı açma.

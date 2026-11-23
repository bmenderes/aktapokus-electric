# ADR-0048: Sayfa sırası değiştirme

Durum: kabul edildi (2026-11-19; kapak sayfası sonradan eklenince listenin başına alınabilmeli)

## Karar
- Çekirdekte `MovePage(sayfa, yeniSıra)` komutu: yalnızca listedeki sıra değişir. **Sayfa numaraları, çapraz referanslar ve tel numaraları aynı kalır**; numaralar istenirse ayrıca "Sayfa numaralarını baştan ver" ile sıraya göre verilir (ADR-0042).
- Geçersiz sıra `page.sira_gecersiz`, bilinmeyen sayfa `page.yok` ile reddedilir; geri alma sayfayı eski sırasına koyar.
- Editör: Sayfa sekmesinde "Sıra" grubu (en başa, yukarı, aşağı taşı; etkin sayfa için) ve sayfa listesinin sağ tık menüsünde aynı komutlar (tıklanan sayfa için). Taşınan sayfa açık kalır.
- Dosya biçimi değişmez: sayfa sırası zaten dosyadaki sayfa sırasıdır.

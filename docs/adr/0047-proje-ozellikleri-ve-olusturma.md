# ADR-0047: Proje özellikleri ve proje oluşturma pencereleri

Durum: kabul edildi (2026-11-19, proje sahibi onayı: Faz 6d, ikinci dilim)

## Bağlam
Kapak sayfası ve antet (ADR-0046) proje özelliklerini kullanır ama özellikler yalnızca `ake apply` (`setProperty`) ile verilebiliyordu. Yeni proje de pencere sormadan "Yeni proje" adıyla açılıyordu. Yaygın E-CAD programlarında proje oluşturulurken ad, konum, şablon, oluşturan ve tarih sorulur; proje özellikleri ayrı bir pencerede (Özellikler, İstatistik, Yapı, Durum sekmeleri) düzenlenir.

## Karar
- **Çekirdek:** yeni `SetProjectName` komutu (boş ad reddedilir, geri alınır). Alanlar mevcut `SetProjectProperty` ile değişir; dosya biçimi değişmez.
- **Proje özellikleri penceresi** (Başlangıç sekmesi ve Dosya menüsü, "Proje özellikleri…"): *Özellikler* sekmesinde proje adı ve alanlar: açıklama (`description`), müşteri (`customer`), sipariş/iş no (`commission`), çizim no (`drawingNo`), firma (`company`), firma adresi (`companyAddress`), çizen (`drawnBy`), tarih (`date`), revizyon (`revision`). Alan anahtarları kapağın ve antet yer tutucularının (`{project.drawingNo}` …) kullandığı anahtarlardır. Boş değer alanı siler, değişmeyen alana dokunulmaz, hepsi **tek adımda** geri alınır. *İstatistik* (sayfa türlerine göre sayfa, cihaz, klemens grubu, bağlantı, numaralı tel, kablo, yerleşim, şablon), *Yapı* (=fonksiyon ve +yer düğümleri; her düğümün sayfa ve cihaz sayısı) ve *Durum* (dosya, kayıt durumu, şema sürümü, oturumdaki değişiklik, son değişiklik, ERC) salt okunurdur.
- **Proje oluşturma penceresi** (Ctrl+N): proje adı, saklama klasörü (varsayılan Belgelerim\aktapokus-electric), şablon (şimdilik gömülü A3 yatay), oluşturan (varsayılan oturum kullanıcısı → `drawnBy`) ve tarih (varsayılan bugün → `date`). Klasör verilirse proje hemen `klasör\ad.json` olarak kaydedilir; boş ad, dosya adında geçersiz karakter ve var olan dosya pencerede reddedilir.
- Pencereyle oluşturulan proje **kapak sayfasıyla** başlar (1 Kapak, 2 boş şema sayfası) ve kapakta açılır. Kapak sayfasında (ve diğer sayfalarda) sağ tık menüsünün ilk maddesi **Proje özellikleri…**'dir; sayfa listesinin sağ tık menüsünde de vardır.
- Betik ve testler için `NewProject(NewProjectInfo)` pencere açmadan çalışır.

## Bu dilimde olmayanlar
Birden çok şablon seçeneği ("hazır sayfa şablonları": A4/A2, dikey, kapaklı; ROADMAP Faz 6c madde 6), proje başlangıç tarihi/firma logosu alanları, proje özelliklerine serbest alan ekleme penceresi.

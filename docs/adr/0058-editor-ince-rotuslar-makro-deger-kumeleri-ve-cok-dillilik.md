# ADR-0058: Editör İnce Rötuşları — Özel Özellikler, Kapak Logosu, Proje Önayarları, Değer Kümeleri ve Çok Dillilik

- Durum: Kabul edildi
- Tarih: 2026-11-21

## Bağlam

Şema editörünün günlük kullanımda beklenen ince ayarları (bileşene özel anahtar/değer satırları ekleme, kapak sayfasına firma logosu koyma, proje tipine özel başlangıç şablonları, hazır makro kütüphanesi ve değer kümeleri, çok dilli çizim metinleri ve koyu çizim zemini) Faz 6 sonrasında tamamlanmak üzere ertelenmişti.

## Karar

1. **Cihaz özel özellikleri (`Device.Properties`, `SetDeviceProperty`)**:
   - `Device` modeline sıralı `Properties` sözlüğü ve model/patch katmanına geri alınabilir `SetDeviceProperty` (`setDeviceProperty`) komutu eklendi; boşken serileştirmede alan yazılmaz (geriye dönük uyumluluk korunur).
   - `DevicePropertiesWindow` içinde `"Özel"` kategorisi üzerinden kullanıcı özel satır ekleyip silebilir.

2. **Kapak logosu ve proje önayarları (`ProjectPropertiesWindow`, `NewProjectWindow`, `ProjectPreset`)**:
   - `ProjectPropertiesWindow`, mevcut `EditorSession.ProjectFields` kümesini bozmadan `"companyLogo"` (görsel seçiciyle) ve `"language"` alanlarını sunar; `SheetRenderer.AutoPages` kapak başlık kutusuna `ImageItem` yerleştirir.
   - `NewProjectInfo` ve `NewProjectWindow`, `ProjectPreset` (`Standard`, `IndustrialMachine`, `CommunicationTopology`, `Pneumatic`) seçimiyle projeye başlangıç sayfaları, yapı düğümleri ve bağlantı türleri kurar.

3. **Hazır makro kütüphanesi ve değer kümeleri (`StandardMacros`, `MacroValueSet`, `MacroLibraryWindow`)**:
   - `Macro` tanımı isteğe bağlı `ValueSets` listesi taşır; `MacroService.Insert(..., valueSetName)` yerleştirme anında kopyalanan cihazlara sipariş kodu, akım, açıklama veya `prop.<anahtar>` değerlerini atar.
   - `StandardMacros` (`Motor çıkışı`, `24VDC besleme`, `Acil stop devresi`) ve `MacroLibraryWindow`, değer kümesi seçimiyle ya da `.makro.json` dosyasından yerleştirmeyi sağlar.

4. **Çok dilli metinler (`MultilingualText`) ve koyu çizim zemini (`SceneView.DarkCanvas`)**:
   - `tr=...;en=...;de=...` biçimli dizeler `project.Properties["language"]` değerine göre kapakta, içindekilerde, antetlerde, yer imlerinde ve serbest grafik metinlerinde çözümlenir.
   - `gorunum.koyu_zemin` komutu ve `SceneView.DarkCanvas`, varsayılan siyah çizgi/yazı renklerini koyu zemin üzerinde okunur açık mürekkeple çizer.

## Sonuçlar

- Mevcut snapshot ve şema dosyaları boş özel özellik/dil yokken bayt düzeyinde değişmeden kalır.
- Kütüphane ve model katmanı bağımsızlık kuralını korur (`Ake.Core` sıfır dış paketle kalır).

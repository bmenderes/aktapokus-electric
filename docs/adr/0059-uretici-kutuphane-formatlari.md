# ADR-0059: Üretici Kütüphane Formatları ve ETIM Fonksiyon Şablonu Eşlemesi

- Durum: Kabul edildi
- Tarih: 2026-11-21

## Bağlam

Kütüphanenin yalnızca elle yazılmış `.json`/`.csv` ürün listeleri yerine endüstride yaygın üretici ve saha veriyolu tanımlama dosyalarından (`BMEcat + ETIM`, `PROFINET GSDML`, `EtherNet/IP EDS`, `IO-Link IODD`, `AASX / AAS`, deneysel `EDZ part.xml` ve `SkyCAD`) kaynak ve lisans zorunluluğuyla beslenmesi gerekiyordu.

## Karar

1. **ETIM sınıfından fonksiyon şablonuna eşleme (`EtimTemplateMapper`)**:
   - Yaygın ETIM sınıfları (`EC000066` kontaktör, `EC000042` devre kesici, `EC000074`/`EC002540` motor koruma, `EC001032` röle, `EC000897` klemens, `EC001419` güç kaynağı, `EC001099` sensör, `EC001418` PLC/fieldbus, `EC001857` sürücü, `EC001449` motor) doğrudan uygun `FunctionTemplate` ve pin adlarına dönüştürülür; kod bilinmiyorsa sınıf adı ipuçlarına düşülür.

2. **Üretici formatı içe aktarıcıları (`ManufacturerImporters`)**:
   - `BmecatImporter` (`SUPPLIER_AID`, `ARTICLE_FEATURES` içindeki ETIM sınıfı + özellikler + anma akımı).
   - `GsdmlImporter` (`DeviceIdentity`, `DeviceAccessPointItem`, `ModuleItem`, `DataItem` G/Ç pinleri).
   - `EdsImporter` (`[Device]`/`[DeviceInfo]`, `[Params]`).
   - `IoddImporter` (ham XML veya zip paketli `.iodd`, `DeviceVariant` + `ProcessData`).
   - `AasxImporter` (OPC ZIP `.aasx`, AAS XML ve AAS JSON `Nameplate`/ETIM özellikleri).
   - `EdzImporter` (`.edz` zip veya bağımsız `part.xml`, `<functiontemplate>` / `<connectionpoint>` veya ETIM yedeği).
   - `SkyCadImporter` (`CataloguePart` / `Component` XML ve terminal listesi).

3. **Tek giriş noktası ve lisans disiplini (`LibraryImport.FromFile`)**:
   - İkili arşiv formatları dosya uzantısına göre metin okuma adımından önce ilgili içe aktarıcıya yönlendirilir; `.xml` dosyaları kök öğe adından otomatik tanınır.
   - Bütün içe aktarıcılar boş olmayan `source` ve `license` bilgisi olmadan çalışmayı reddeder.

## Sonuçlar

- Editör ürün seçme penceresi (`ProductSelectionWindow`) ve `ake lib import` komutu tek dosya seçici üzerinden tüm üretici formatlarını destekler.
- Üçüncü taraf kütüphane gerektirmez; yalnızca .NET standart XML/JSON/ZIP sınıfları kullanılır.

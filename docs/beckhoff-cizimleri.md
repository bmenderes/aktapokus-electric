# Beckhoff teknik çizimleri (Industrial_PC) ve E-CAD'deki yeri

`download.beckhoff.com/download/technical_drawings/` altında beş grup var: `Industrial_PC`, `Embedded_PC`, `Fieldbus_Components`, `drive_technology`,
`vision`. `Industrial_PC` klasörünün `*.dxf.zip` dosyaları (4399 dosya, ~780 MB) 2026-11-18'de yerel olarak indirildi:
`Ornek/beckhoff/` (depoya girmez; Beckhoff kullanım koşulları doğrulanmadı, dosyalar dağıtılmaz).

## Bulgu

Bu DXF'ler **mekanik montaj çizimleridir**, elektrik şeması sembolü değil: panel PC'lerin ve kumanda panellerinin kesim/montaj ölçüleri,
birden çok görünüm (ön, yan, kesit), ölçü okları, tarama, binlerce çizgi ve spline (örn. `CP2907-0010_Landscape.dxf`: 2016 çizgi,
285 spline, 415 × 297 mm; `C3320-0010.dxf`: 1565 × 1168 mm, 23 blok). Uç (POINT) yoktur ya da ölçü işaretidir.

Bu yüzden:

- **Şema sembolü olarak içe aktarılmaz.** Bir görünümü `--alan` ile seçip sembol yapmak mümkündür (`ake symbol inspect` + `--alan`),
  ama ölçülü teknik çizim şemada işe yaramaz.
- **Asıl kullanım Faz 8 (pano yerleşimi, 2B):** cihazın `Mounting` bilgisi (genişlik, yükseklik, derinlik) ve pano kesim/montaj ölçüsü.
  Bunun için ön görünümün dış çerçevesi otomatik bulunmalı; görünüm seçimi ve ölçek bu fazın işidir.
- Elektrik sembolü/ürün kaydı için doğru kaynaklar `Embedded_PC` (CX serisi) ve `Fieldbus_Components` (EK/EL klemensleri, EtherCAT Box)
  klasörleridir; bunlar da mekanik çizimdir, ama ürün başına kutu boyutu ve ürün kodunu verirler. Ürün kütüphanesi (ADR-0007) kaydı
  için üretici ürün verisi (uç sayısı, adları) ayrıca gerekir; çizimlerden uydurulmaz.

## Sonraki adım (önerilen)

1. Faz 8 başlarken: DXF'ten `Mounting` çıkaran araç (ön görünüm dış çerçevesi, birim dönüşümü, derinlik yan görünümden ya da dosya adından).
2. Hazır ürün kütüphanesi için Beckhoff'un ürün veri sayfaları (uç adları) ya da kullanıcının getireceği ESI/ürün dosyaları.

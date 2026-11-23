# ADR-0038: Marka (ad, logo), sarı tema ve Hakkında penceresi

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-18
- **İlişki:** ADR-0001 (lisans: Apache-2.0), ADR-0023 (şerit ve tema)

## Bağlam

Proje sahibi uygulamanın tamamen sarı olmasını ("sarı çizmeli Mehmet Ağa gibi"), adının **aktapokus-electric** olmasını, bir logosunun
ve Hakkında penceresinin bulunmasını, açık kaynak olduğunun görünmesini istedi.

## Karar

| Konu | Karar |
|---|---|
| Ad | **aktapokus-electric** (arayüzde, pencere başlığında, Hakkında'da, derleme meta verisinde). |
| Renk dili | Marka sarısı `#FFD500`, koyu mürekkep `#1A1400`; pencere `#FFF3B0`, paneller `#FFFBE0`, panel başlıkları `#FFD93D`, şerit `#FFE066`, çizgiler `#D9B800`, vurgu metni `#8F6400`. **Çizim kâğıdı beyaz kalır**; kâğıdın çevresi sarı-altın (`#EAD268`), ızgara noktaları sıcak tonda. Seçim vurgusu kehribar (`#D98200`). |
| Uygulama | `App.axaml` kaynakları: pencere, panel, şerit, durum çubuğu, sekmeler; Fluent vurgu ailesi sarıya çevrildi (işaretli kutular koyu işaret taşır); Dock'un sabit VS mavisi seçili sayfa sekmesi `DocumentTabStripItem` stiliyle marka sarısı. Şemanın kendi içeriği (tel renk kodları, kablo türleri) tema renginden bağımsızdır: tel renkleri elektrik anlamı taşır. |
| Logo | Sarı yuvarlak kare, koyu yıldırım ve iki devre ucu. Kaynak `assets/logo.svg`; `logo-256.png` ve çok boyutlu `logo.ico` (16–256) aynı çizimden üretildi. `src/Ake.Editor/Assets/` içindeki kopya uygulamaya gömülür: pencere simgesi (`Window.Icon`), exe simgesi (`ApplicationIcon`), Hakkında. |
| Hakkında | **Araçlar > Yardım > Hakkında**: logo, ad, sürüm, "Açık kaynak" (Apache License 2.0), sembol kaynakları ve atıf, kullanılan açık kaynak bileşenler ve lisansları, **Lisansı göster** (gömülü LICENSE metni), garanti reddi. İçerik `AboutInfo` içindedir ve testle sınanır. |
| Açık kaynak | Lisans Apache-2.0 (kendi kodumuz; GPL bağımlılık eklenmez). Standart sembol setinin lisansı da Apache-2.0'dır. Üçüncü taraf sembol kütüphaneleri kaynak, lisans ve atıfla kayıtlıdır. |

## Yapılmayanlar

- Koyu tema: yalnızca açık (sarı) tema var.
- Depo adresi ve katkı sayfası Hakkında'da yok (adres belirlenince eklenir); uydurma bağlantı yazılmadı.

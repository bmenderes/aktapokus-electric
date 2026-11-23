# ADR-0045: Krem tema (sarı temanın yerine)

Durum: kabul edildi (2026-11-19, proje sahibi: "sarı tema iptal, her zamanki renk, krem")

## Bağlam
ADR-0038 tamamen sarı bir editör teması getirmişti. Proje sahibi sarıyı beğenmedi ve eski, sakin görünüme (krem) dönülmesini istedi.

## Karar
- ADR-0038'in **tema** kısmı geçersizdir; logo, pencere simgesi, Hakkında penceresi ve açık kaynak bilgisi (ADR-0038'in geri kalanı) aynen kalır.
- Palet: pencere `#F2EDE1`, panel ve şerit gövdesi `#FBF8F1`, başlık `#E8E0CC`, çizgi `#D6CDB8`, metin `#2B2619`, soluk metin `#6B6455`. Tek vurgu rengi mavi `#1F6FD8`; seçili sekme ve basılı düğme dolgusu açık mavi `#DCEAFB`.
- Çizim alanının zemini krem-gri `#D9D2BF`; kâğıt beyaz kalır. Seçim vurgusu mavi, ızgara noktaları sıcak gri.
- Sağ tık menüleri, açılır listeler ve ipuçları da krem. Renkler `App.axaml` kaynaklarında ve `SceneView`'de tek yerde tanımlıdır; başka dosyada renk gömülmez.
- Logo (marka sarısı) olduğu gibi kalır; yalnızca arayüz teması değişti.

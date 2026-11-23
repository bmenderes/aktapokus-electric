# ADR-0043: Kablo ve tel etiketleri

Durum: kabul edildi

## Bağlam
Montaj ve bakımda iletkenin iki ucunda da kimliği ve gittiği yer okunabilmeli (EN 60204-1: iletkenler şemadaki gibi tanımlanır). Etiket yazıcılarının yazılımları (Weidmüller M-Print, Phoenix CLIP PROJECT, Brady, Brother vb.) CSV içe aktarır ve satır başına bir etiket basar.

## Karar
- Yeni rapor `labels` (`ake report labels <proje> --format csv|xlsx`): numaralı her tel ve her kablo damarı için iki satır, her uç için bir etiket.
- Sütunlar: Etiket (tel numarası ya da `-kablo:damar`), Uç (A/B), Bu uç (etiketin takıldığı bağlantı noktası), Karşı uç, Renk, Kesit.
- Numarasız köprüler etiketlenmez. Sıralama bağlantı listesiyle aynı: önce sayısal tel numaraları, sonra metinler, sonra kablo damarları.
- Belirli bir yazıcı yazılımına özel biçim yok; CSV ayracı `--separator` ile seçilir. Yazıcı biçimi netleşince ayrı bir eklenti olarak eklenir.
- Baskıya hazır PDF etiket sayfası bu ADR'nin kapsamı dışındadır (yazıcı/şerit ölçüsü bilinince).

# ADR-0027: DXF/DWG çiziminden sembol içe aktarma

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-17
- **İlişki:** ADR-0001 (lisans zorunluluğu), ADR-0013 (kullanıcı sembolü), ADR-0016 (QElectroTech içe aktarma),
  ADR-0022 (ACadSharp)

## Bağlam

Proje sahibi üreticilerin (Schneider PacDrive, Rockwell, Beckhoff) TraceParts/CADENAS'tan indirilen DXF/DWG
çizimlerini kullanmak istiyor. Bu siteler hesap ve kullanım koşulu onayı istediği için dosyaları proje sahibi
indirir; uygulama dosyaları dağıtmaz. ROADMAP iki ayrı işi ayırıyor: (1) çizimi sembole çevirmek, (2) bütün
bir çizimi sayfaya serbest grafik olarak almak. İkincisi modelde yeni bir nesne ister; bu ADR yalnızca
birincisini kapsar.

## Karar

| Konu | Karar |
|---|---|
| Okuyucu | ACadSharp (MIT; ADR-0022'de çıktı için zaten seçildi). `Ake.Library` bu pakete başvurur. |
| Ne sembol olur | Varsayılan: model alanının tamamı tek sembol, adı dosya adı. `--blocks`: adlandırılmış her blok ayrı sembol (`*` ve `_` ile başlayan sistem blokları hariç). |
| Desteklenen öğeler | Çizgi, daire, yay, elips, çoklu çizgi (bulge dahil), spline, solid, yazı, MText, POINT. Blok ekleri ve ölçüler açılarak alınır (en çok 16 düzey). Tarama, görsel, öznitelikler ve döndürülmüş yazılar alınmaz. Kapalı ya da dondurulmuş katmanlar alınmaz. |
| Eğriler | Sembol modelinde yay yoktur (ADR-0013); eğriler 32 parça/tam tur çizgiye bölünür. |
| Birim ve ölçek | `$INSUNITS` milimetreye çevrilir (birimsiz = mm), sonra `--scale` ile çarpılır (varsayılan 1). Üretici ön görünüşleri 1:1 olduğundan şemada genellikle 0,2–0,3 ölçek kullanılır. |
| Uçlar | POINT nesneleri uçtur; yönleri çizimin en yakın kenarına doğrudur. Nokta yoksa tek uç konur (model alanında sol üst köşe, blokta taban noktası) ve uyarı verilir. |
| Kimlik | Dosya yolu (ve blok adı) karmasından türetilir; aynı dosya yeniden alınınca sembol güncellenir, çoğalmaz. |
| Lisans | Zorunlu (ADR-0001). Üretici çizimi lisans taşımadığından `--license` ile kullanım koşulu verilmezse sembol alınmaz. |
| Komutlar | Yeni komut yok: `ake symbol import` ve `ake lib symbol-import` yolu uzantısına göre QElectroTech ya da DXF/DWG olarak okur; klasörde ikisi birlikte olabilir. |

## Sonuçlar

- Referans makinenin DWG çıktısı geri alınarak (CLI testi) ve elle yazılmış bir sürücü ön görünüşü DXF'iyle
  denendi: sembol sayfada doğru boyut, yön ve uçlarla çizildi.
- Gerçek bir PacDrive (LXM62/LMC) çizimiyle henüz denenmedi; dosyayı proje sahibi indirecek.
- Uç yönü sezgiseldir; yanlış çıkan uçlar blok editöründe düzeltilir.

## Yapılmayanlar

- Bütün çizimin sayfaya serbest grafik olarak alınması (modelde yeni nesne gerekir).
- Editörde DXF/DWG dosyasını sayfaya sürükleyip bırakarak içe alma.
- DXF'ten makro (birden çok cihaz ve bağlantı) üretme; çizimden elektriksel anlam çıkarılmaz.
- Renk ve çizgi kalınlığının taşınması.

## Ek: çok görünümlü üretici çizimleri

Üretici DXF/DWG dosyaları tek dosyada birden çok görünüm (ön, üst, yan) taşır. `ake symbol inspect <dosya>` içe aktarmadan sürüm, birim, sınırlar, öğe türleri, katmanlar, bloklar, uç sayısı ve alınmayacak öğeleri (tarama, görsel, 3B) gösterir. `--alan "x,y,genişlik,yükseklik"` (dosyanın çizim biriminde) yalnızca bu dikdörtgenin tamamen içinde kalan öğeleri alır; böylece tek görünüm sembol olur.

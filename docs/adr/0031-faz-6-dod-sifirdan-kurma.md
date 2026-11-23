# ADR-0031: Faz 6 DoD — referans makinenin sıfırdan kurulması ve model komutları

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-18
- **İlişki:** ADR-0003 (veri modeli), ADR-0004 (command), ROADMAP Faz 6 DoD

## Bağlam

Faz 6'nın bitiş ölçütü: "Referans proje editörde sıfırdan çizilebiliyor ve aynı raporları veriyor." Referans makinede komutlarla
kurulamayan dört parça vardı: **yapı düğümleri** (=/+ ağacı), **kablolar** ve damarları, **potansiyel işaretleri** ve **PLC
denetleyicisi** (ağ, düğüm, modül, kanal). Bunlar yalnızca proje dosyasından geliyordu; model "yalnızca Command'larla değişir"
kuralına rağmen bu parçalar komutla eklenemiyordu.

## Karar

| Konu | Karar |
|---|---|
| Yeni komutlar | `AddStructureNode`, `AddCable`, `AddPotentialSource`, `AddController` (Core). Hepsi aynı desende: kimlik tekil mi denetlenir, nesne eklenir, `Project.CheckIntegrity()` çalışır, bozulursa ekleme geri alınır ve hata döner. Eksik üst düğüm, uyumsuz aspect, çift kimlik, olmayan uç ya da cihaz modele giremez. |
| Betikle kurma | `EditorSession.Run(ICommand)`: dışarıdan hazırlanmış komutu ya da işlemi editörün kendi yolundan çalıştırır; geçmişe kayıt düşer, geri alınır, ERC yenilenir. |
| DoD testi | `ReferenceMachineRebuildTests`: boş projeyi editör oturumunda açar; referansın her parçasını **yeni kimliklerle** komuta çevirip sırayla çalıştırır (ayarlar ve yapı, cihazlar, kablolar, bağlantılar, potansiyeller, PLC, sayfalar, yerleşimler). Sonra karşılaştırır: nesne sayıları; BOM, kablo, tel, klemens planı ve I/O raporları (CSV); ERC bulguları; bütün sayfaların sahne öğeleri (konum, yazı, tel yolları, çapraz referans bağlantıları). Ayrıca aşama aşama geri alınınca proje boşa döner ve eksik bir aşama net hata verir. |
| Kapsam | DoD **komut düzeyindedir**: model ve komut kümesi referans makineyi eksiksiz yazabilir. Bu parçaların editör arayüzündeki formları (aşağıda) ayrı iştir. |

## Sonuç

Referans makine boş projeden yalnızca komutlarla kuruldu: 19 cihaz, 65 bağlantı, 2 kablo, 7 potansiyel işareti, 1 PLC denetleyicisi,
5 sayfa, 68 yerleşim; beş rapor, ERC (hata yok) ve beş sayfanın çizimi referansla **birebir aynı**.

## Yapılmayanlar (arayüz formları, sonraki işler)

- **Yapı yönetimi** (=/+ düğümü ekle, yeniden adlandır, taşı): komut var, form yok.
- **Kablo tanımı ve damar atama** (kablo ekle, damar ekle, bağlantıya damar ata): komut `AddCable` ile `AssignWire`; form ve kablo
  navigator'ından düzenleme yok.
- **Potansiyel işareti koyma** (uca L1/24VDC/0V işareti): komut var, form yok.
- **PLC topolojisi** (denetleyici, ağ, düğüm, modül, kanal): komut var; düzenleyici Faz 7'nin işidir.
- Bu komutların silme/değiştirme karşılıkları (`RemoveStructureNode`, `RemoveCable`…): ihtiyaç doğunca.
- Fareyle çizim denenmedi; DoD başsız ve testle doğrulandı.

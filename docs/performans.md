# Performans ölçümü (Faz 6.3, madde 15)

Hedefler (ROADMAP Bölüm 0): soğuk açılış < 3 sn; 300 sayfalık projede açık sayfa dışında render yok; RAM < 300 MB (AI kapalı).
Ölçüm aracı `tools/Ake.Bench`: referans makineden makrolarla gerçekçi büyük bir proje kurar (sayfa başına cihaz, tel, yerleşim),
oturum ve pencere düzeyinde ölçer, hedefleri denetler.

```bash
dotnet run -c Release --project tools/Ake.Bench -- 300
```

`300` sayfa sayısıdır (varsayılan 300); ikinci argüman başka bir referans projedir. Hedef aşılırsa çıkış kodu 1 olur.

## Sonuç (300 sayfa: 1.791 cihaz, 2.720 bağlantı, 5.382 yerleşim, 4,7 MB dosya)

Makine: 14 çekirdek, Windows 11, Release derleme; ekran kartı yok (başsız Skia çizimi, CPU).

| Ölçüm | Önce | Sonra | Hedef |
|---|---|---|---|
| **Soğuk açılış** (süreç başlangıcından ilk kareye, proje açık) | 6,2 sn | 2,3–3,3 sn (JIT'li derleme), **1,6 sn** (ReadyToRun yayın) | < 3 sn |
| Açık sayfa dışı çizim | — | 21 sekme açıkken 21 sayfa çizildi (300 sayfadan) | yalnızca açık sayfa |
| Yönetilen bellek / çalışma kümesi | 70 / 230 MB | 54 / 216 MB | < 300 MB |
| Sayfa çizimi (`SceneFor`), ortanca / en kötü | 47 / 160 ms | 42 / 179 ms | — |
| Sayfa geçişi (aç + çiz), ortanca / en kötü | 212 / 332 ms | 234 / 318 ms | — |
| Komut (taşı) + ERC, ortanca | 34 ms | 44–61 ms | — |
| Cihaz navigator'ı (soğuk → sıcak) | 723 ms | 34 → 3 ms | — |
| Mesajlar | 375 ms | 7 → 2 ms | — |
| Klemens navigator'ı (soğuk → sıcak) | 434 ms (üç navigator) | 46 → 5 ms | — |
| Sayfa ağacı | 344 ms ve açılışta yaklaşık 2,8 sn pencere süresi | satır listesi 1 ms | — |
| Proje yükle / ERC | 404 ms / 44 ms | aynı | — |
| Kaydet | 173 ms | aynı | — |

"Önce" sütunu ilk ölçümdür (bu işten önceki kod). Soğuk açılış çalıştırmalar arasında ±1 sn oynar (disk önbelleği, arka plan
yükü); araç üç çalıştırmanın ortancasını yazar. İlk çalıştırma yeni derlenmiş dosyalarda daha yavaş olabilir (5,5 sn görüldü).

## Bulunan ve düzeltilen darboğazlar

1. **`EditorSession.Locate` her çağrıda bütün cihaz ve yerleşim listelerini tarıyordu** (O(n)); cihaz, klemens, PLC ve kablo
   navigator'ları ile mesaj paneli her satır için çağırdığından toplam O(n²) idi. Artık model değişikliği başına bir kez
   kurulan indeks kullanılıyor (O(1)).
2. **Sayfa ağacı `TreeView` idi**: Avalonia'nın `TreeView`'ı sanallaştırılmaz, 300 sayfada her düğüm için denetim kurmak açılışa
   yaklaşık 2,8 sn ekliyordu. Ağaç artık düzleştirilmiş, girintili ve katlanabilir satırlarla sanallaştırılan bir listede
   (`PageTreeRows`, ADR gerekmedi: görünüm değişikliği). Grup satırına tıklamak katlar/açar; süzgeç etkinken her şey açılır.
3. **Klemens navigator'ı her klemens için bütün bağlantıları tarıyordu**; bağlantılar nokta başına bir kez indeksleniyor.

## Ölçeklenme (300 → 600 sayfa)

600 sayfada (3.591 cihaz) proje yükleme 1,2 sn, sayfa çizimi 86 ms (ortanca), komut 119 ms, bellek 91 MB / 261 MB. Soğuk açılış
JIT'li derlemede 6,3 sn'ye çıkıyor; boyuta göre doğrusaldan biraz fazla. Hedefin dışındadır (300 sayfa), ama büyük projeler için
sırasıyla şu işler bilinen iyileştirme yerleridir:

- Sayfa çizimi her sayfada bütün cihazlardan bir arama tablosu kuruyor (`SchematicRenderer.PlacePins`); sayfa başına
  indeks ile sayfa boyuna bağlı hâle getirilebilir.
- Makro yerleştirme süresi proje boyuyla doğrusal artıyor (10 ms → 117 ms, 300 sayfada); tag ayırma ve kimlik kümesi her komutta
  yeniden kuruluyor.
- ERC ve potansiyel hesabı her değişiklikte tüm projeyi yeniden çalıştırıyor (300 sayfada 35–45 ms); artımlı hâle getirilebilir.

## Yayın önerisi

Soğuk açılış hedefi **ReadyToRun** yayında rahat sağlanıyor (1,6 sn, derlenmiş-JIT 2,6–2,8 sn). Dağıtım self-contained olduğu için:

```bash
dotnet publish src/Ake.Editor -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true
```

(Bu komut henüz projede ayarlı değildir; dağıtım işi geldiğinde `Ake.Editor.csproj`'a ya da yayın betiğine eklenir.)

## Ölçülmeyenler ve CI bütçe doğrulaması

- `.github/workflows/ci.yml` içinde `--verify` bayrağıyla koşturulur (`dotnet run -c Release --no-build --project tools/Ake.Bench -- 300 --verify`); 300 sayfa üzerinde RAM (`< 300 MB`), sahne çizimi (`< 100 ms`), taşı/geri al (`< 250 ms`), sayfa geçişi (`< 500 ms`), açık sekme (`21/300`) ve JIT soğuk açılış (`< 6 sn`) eşiklerini denetler.
- **4 GB RAM sanal makinede** yerel ölçüm (ortam hazır oldukça) ve gerçek ekran kartı + fare akıcılığı.
- 300 sayfalık projede PDF çıktısı süresi.

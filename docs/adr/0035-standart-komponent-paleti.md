# ADR-0035: Standart komponent paleti ve POINT'siz DXF sembollerinde uç çıkarımı

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-18
- **İlişki:** ADR-0007 (komponent kütüphanesi), ADR-0016 (QElectroTech), ADR-0027 (DXF/DWG), ADR-0033 (uç özellikleri), ROADMAP Faz 6b-3

## Bağlam

Semboller sekmesi, kullanıcı bir kütüphane dosyası açana kadar boştu; hazır kütüphane yalnızca 6 üründü. Standart bir E-CAD'de
düğme, lamba, kontaktör, motor, klemens, sensör gibi temel elemanlar hazır gelir.

## Karar

| Konu | Karar |
|---|---|
| Standart set | `StandardSymbols` (Ake.Library): 41 elle çizilmiş sembol, 9 kategori (Kumanda, Sinyal, Koruma, Röle ve kontaktör, Motor ve aktüatör, Güç, Klemens ve konnektör, Sensör, Otomasyon). Üçüncü taraf koleksiyondan alınmadı; lisans "Apache-2.0 (aktapokus-electric ile birlikte)". Her sembol uç adlarını, uç özelliklerini (NO/NC/CO, M8/M12/RJ45, güç/sinyal/haberleşme), cihaz adını ve tag harfini taşır (ADR-0033). |
| Kimlik | Addan türetilir; her kurulum aynı sembolü günceller, çoğaltmaz. Kullanıcının kendi sembollerine dokunulmaz. |
| Kurulum | Editör açılırken varsayılan kütüphane (`%LOCALAPPDATA%\aktapokus-electric\library.db`) yoksa oluşturulur, standart set içine konur ve açılır. |
| Palet | Semboller sekmesi kategoriye göre sıralı. Sürükle-bırak ya da yeni **Sayfaya yerleştir** düğmesi (imleç alandaysa oraya, değilse alanın ortasına). Yerleşince cihaz sembolün tag harfi, cihaz adı, uç adları ve uç özellikleriyle oluşur. |
| Koordinat düzeltmesi | Sürükle-bırak ve makro bırakma, bırakılan **sayfa** koordinatını doğrudan **yerleşim** koordinatı olarak kaydediyordu; yerleşim koordinatı çizim alanına göredir (alan 15, 15'te başlar), bu yüzden sembol bırakılan yerin 15 mm sağına/altına düşüyordu. `EditorSession` artık alan başlangıcını çıkarır. |
| POINT'siz DXF | Üretici kütüphaneleri uçları POINT ile işaretlemez. `DxfImport`, küçük çizimlerde (en çok 80 çizgi) hiçbir başka çizgiye bağlanmayan **dikey** çizgi uçlarından, sembolün en üst ve en alt kenarındakileri uç sayar (üsttekiler soldan sağa, sonra alttakiler). En az iki uç bulunamazsa eski davranış: sol üst köşeye tek uç ve uyarı. |
| Üçüncü taraf arşiv | B. Favre'nin DXF sembol kütüphanesi (CC BY 4.0) elle içe aktarılır (`docs/favre-kutuphanesi.md`); 59 MB arşiv depoya girmez (`.gitignore`). |

## Yapılmayanlar

- Çok kutuplu paket semboller (3 kutuplu şalter tek sembol): sembol başına bir function; kutuplar ayrı yerleştirilir.
- Palette arama dışında süzgeç (kategori ağacı); liste kategoriye göre sıralı ve arama ad ve kategoride çalışır.
- Favre kütüphanesinin uç adları (metinlerden) ve kontak türleri (dosya adından) çıkarılmıyor; yalnızca uç konumları.

## Ek (kullanıcı geri bildirimi, 2026-11-18)

| Konu | Karar |
|---|---|
| Gruplama | Palet düz liste yerine **ağaç**: kategori ve alt kategori düğümleri (`SymbolPalette.Build`). Standart set kategorileri olduğu gibi; üretici klasörleri Türkçe okunur gruplara çevrilir (örn. `fav0100_contact/…_NO_normal_open` → Kontaklar › Normalde açık (NO)); aynı ada düşen gruplar birleşir. Dosya adları temizlenir (`fav0100_A10_contact_NO_detent` → Contact NO detent). Arama yapılırken ağaç açık gelir. |
| Sembol düzenleme | Palette **Sembolü düzenle…**: sembol blok editöründe açılır, **Kütüphaneye kaydet**. Kullanıcının kendi sembolü yerinde güncellenir; standart set ve üretici sembolleri **kopya** olarak yeni kimlikle "Kendi sembollerim" kategorisine yazılır (asıl sembol sonraki kurulumda yeniden yazılırdı). Lisans korunur. |
| Izgara | Blok editöründe ızgara seçilebilir: 5, 2,5 (varsayılan), 1,25, 0,5 mm. Uç aracı, çizgi/daire kenarına 2 mm'den yakın bırakılırsa **kenara oturur**; daire üzerindeki uç dışarı doğru çıkar. |
| Uç gösterimi | Uçlar içi boş halka; ilk uç (başlangıç noktası) çift halka. Bağlantı tipi ucun yanında simgeyle görünür: RJ45 jak, M8/M12 halka, D-Sub yamuk, ad etiketi. Şemada RJ45/M8/M12/D-Sub uç adının yanına yazılır. |
| M12 / M8 | Standart sette yuvarlak (daire + uç çizgileri). |

## Ek 2 (kullanıcı geri bildirimi)

| Konu | Karar |
|---|---|
| Yüzeye uç | Daire ya da dikdörtgen seçili iken **Yüzeye uç ekle** (şerit, "Yüzeye uç" grubu; sayı alttaki kutudan, varsayılan 6, 1–64): uçlar çevreye **eşit aralıklarla** konur (dairede üstten saat yönünde, dikdörtgende üst kenarın başından çevre boyunca, ilk uç yarım adımda); teller yüzeyden dışarı çıkar. Tek adımda geri alınır. |
| Esc | Esc seçimi bırakır; uç paneli ya da metin kutusu odaktayken de çalışır ve odağı çizim alanına verir. |
| Bağlantı tipi simgesi | Bağlantı tipi seçilince ucun sade halkası **kalkar, tipin kendi simgesi gelir** (`ConnectorIcons`, Ake.Render): vida başı, push-in kare, faston dil, M8 ve M12 halka (M12 çift halka), RJ45 jak, D-Sub yamuk, diğer eşkenar dörtgen. Aynı simge blok editöründe ve şema çiziminde (cihaz özelliklerinden verilen tipte de) kullanılır; yanına kısa ad yazılır. |

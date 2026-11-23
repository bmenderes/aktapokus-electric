# Kısayol profili

Editördeki her komutun bir kısayolu olabilir. Varsayılan set aşağıdadır; **Araçlar > Kısayollar** penceresinden ya da
dosyayı elle düzenleyerek değiştirilir. Profil kullanıcıya özeldir, projeye yazılmaz.

## Pencereden değiştirme

- Satırdaki kutuya tıklayıp istenen tuşlara basın. Kısayol hemen yazılır; başka bir komutta kullanılıyorsa altta hangi
  komutta olduğu yazılır ve atama yapılmaz.
- **×** düğmesi kısayolu kaldırır, **↺** düğmesi komutu varsayılan kısayoluna döndürür (yalnızca değiştirilen satırlarda
  etkindir; değiştirilenler kalın yazılır). **Hepsini sıfırla** bütün komutları varsayılana alır.
- Yakalama sırasında yalnızca **Esc** yakalamayı bırakır; Esc'i bir komuta atamak için dosya düzenlenir.
- **Tamam** profili kaydeder; şerit ipuçları, Dosya menüsü ve sağ tık menüsü hemen yeni kısayolları gösterir.

## Dosya

Konum: `%LOCALAPPDATA%\aktapokus-electric\kisayollar.json`. Dosya yoksa varsayılan set kullanılır. Dosyada **yalnızca
varsayılandan farklı olanlar** yazılır; yazılmayan komut varsayılanında kalır. Kısayol `null` ise komutun kısayolu
kaldırılmıştır.

```json
{
  "surum": 1,
  "kisayollar": {
    "proje.kaydet": "Ctrl+Shift+K",
    "baglanti.tel_cek": null,
    "gorunum.izgara": "Ctrl+G"
  }
}
```

| Alan | Anlamı |
|---|---|
| `surum` | Dosya biçimi; şimdilik `1`. |
| `kisayollar` | Komut kimliği → kısayol. Kimlikler aşağıdaki tablodadır. |

Kısayol yazımı: değiştirici tuşlar (`Ctrl`, `Shift`, `Alt`) ve tuş, `+` ile (`Ctrl+Shift+S`, `Delete`, `F11`, `PageUp`).
Büyük-küçük harf önemli değildir; kayıtta tek biçime getirilir. İki komut aynı kısayolu paylaşamaz; ama iki komutun kısayolunu
dosyada birbiriyle değiştirmek (takas) geçerlidir.

**Dosya hatalıysa** (bozuk JSON, tanımsız komut, geçersiz kısayol, çakışma, yanlış `surum`) hiçbir satırı kısmen uygulanmaz:
editör varsayılan kısayollarla açılır ve durum satırında nedeni yazar. Dosya düzeltilene kadar pencereden kaydetmek dosyayı
yeniden yazar.

## Varsayılan set

Tam hâli [kisayollar.varsayilan.json](kisayollar.varsayilan.json) dosyasındadır (testle koddaki setle aynı tutulur).

### Başlangıç

| Komut | Kısayol | Kimlik |
|---|---|---|
| Yeni | `Ctrl+N` | `proje.yeni` |
| Aç | `Ctrl+O` | `proje.ac` |
| Kaydet | `Ctrl+S` | `proje.kaydet` |
| Farklı kaydet | `Ctrl+Shift+S` | `proje.farkli_kaydet` |
| Projeyi kapat | `Ctrl+W` | `proje.kapat` |
| Dışa aktar / Raporlar… | `Ctrl+P` | `proje.disa_aktar` |
| DXF/DWG al… | — | `proje.dxf_al` |
| Liste al (Excel/CSV)… | — | `proje.liste_al` |
| Proje özellikleri… | — | `proje.ozellikler` |
| PDF'ten al | — | `proje.pdf_al` |
| Revizyon ekle… | — | `proje.revizyon_ekle` |
| Yapıştır | `Ctrl+V` | `duzen.yapistir` |
| Kes | `Ctrl+X` | `duzen.kes` |
| Kopyala | `Ctrl+C` | `duzen.kopyala` |
| Geri al | `Ctrl+Z` | `duzen.geri` |
| Yinele | `Ctrl+Y` | `duzen.yinele` |
| Sil | `Delete` | `duzen.sil` |
| Özellikler | `Enter` | `duzen.ozellikler` |
| Döndür | `R` | `duzen.dondur` |
| Tümünü seç | `Ctrl+A` | `duzen.tumunu_sec` |
| Seçimi bırak | `Escape` | `duzen.secimi_birak` |

### Ekle

| Komut | Kısayol | Kimlik |
|---|---|---|
| Sembol ekle… | `Insert` | `sembol.ekle` |
| Sembol değiştir… | — | `sembol.degistir` |
| Metin ekle… | — | `ekle.yazi` |
| Blok editörü | — | `sembol.yeni` |
| Sembolü düzenle | `Ctrl+E` | `sembol.duzenle` |
| Kütüphane aç | — | `sembol.kutuphane` |
| Ürün kütüphanesi… | — | `sembol.urun_kutuphanesi` |
| Makro ekle | `Ctrl+M` | `makro.ekle` |
| Sayfadan makro | — | `makro.sayfadan` |
| Seçimden makro | — | `makro.secimden` |
| Klemens şeridi | — | `ekle.klemens_seridi` |

### Bağlantı

| Komut | Kısayol | Kimlik |
|---|---|---|
| Tel çek | `W` | `baglanti.tel_cek` |
| Baştan numarala | — | `baglanti.tel_numaralari` |
| Yeni telleri numarala | — | `baglanti.yeni_teller` |
| Numaralandırma ayarları… | — | `baglanti.numara_ayarlari` |
| Standart türleri ekle | — | `baglanti.standart_turler` |
| Kablo ve damar… | — | `baglanti.kablolar` |
| Potansiyel işareti… | — | `baglanti.potansiyel` |
| Haberleşme şeması sayfası | — | `baglanti.haberlesme_semasi` |
| Tek hat şeması sayfası | — | `baglanti.tek_hat_semasi` |
| PLC adres / tel tablosu… | — | `baglanti.plc_tablosu` |

### Sayfa

| Komut | Kısayol | Kimlik |
|---|---|---|
| Araya sayfa ekle (3.1) | — | `sayfa.araya_ekle` |
| Yeni sayfa… | — | `sayfa.ekle` |
| Sayfa özellikleri… | — | `sayfa.ozellikler` |
| Antet oluştur / düzenle… | — | `sayfa.antet_editoru` |
| Sayfa numaralarını baştan ver | — | `sayfa.numaralari` |
| Önceki sayfa | `PageUp` | `sayfa.onceki` |
| Sonraki sayfa | `PageDown` | `sayfa.sonraki` |
| Sayfayı yukarı taşı | — | `sayfa.yukari_tasi` |
| Sayfayı aşağı taşı | — | `sayfa.asagi_tasi` |
| Sayfayı en başa taşı | — | `sayfa.basa_tasi` |

### Görünüm

| Komut | Kısayol | Kimlik |
|---|---|---|
| Sayfayı sığdır | `Home` | `gorunum.sigdir` |
| Izgara | `G` | `gorunum.izgara` |
| Koyu zemin | — | `gorunum.koyu_zemin` |
| Tel uçları | — | `gorunum.tel_uclari` |
| Tam çizim alanı | `F11` | `gorunum.tam_alan` |
| Navigator | — | `gorunum.navigator` |
| Mesajlar | — | `gorunum.mesajlar` |
| Şeridi daralt | `Ctrl+F1` | `gorunum.serit` |
| Varsayılan düzen | — | `gorunum.duzen_sifirla` |

### Araçlar

| Komut | Kısayol | Kimlik |
|---|---|---|
| Tag'leri numarala | — | `araclar.tag_numarala` |
| Harf + sıra (K1) | — | `araclar.sema_harf` |
| Sayfa + harf + sıra (3K1) | — | `araclar.sema_sayfa` |
| Grafikten cihaz / bağlantı tanı… | — | `araclar.grafik_tani` |
| Yapay zeka ile değiştir | — | `araclar.yapay_zeka` |
| Kısayollar | — | `araclar.kisayollar` |

### Şeritte olmayanlar

| Komut | Kısayol | Kimlik |
|---|---|---|
| Çıkış | — | `proje.cikis` |
| Hakkında | — | `yardim.hakkinda` |

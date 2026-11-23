# ADR-0036: Bağlantı (kablo) türleri: renk ve çizgi biçimi

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-18
- **İlişki:** ADR-0003 (veri modeli), ADR-0029 (tel köşe noktaları), ADR-0032 (kavşak), ADR-0033 (uç özellikleri), ROADMAP Faz 6b-5/6

## Bağlam

Kablo ve hat renkleri önemli bir bilgidir (Profinet yeşil, Ethernet kesik-noktalı koyu yeşil, DriveCLiQ kesik gri…). Haberleşme şeması ve tek hat
şemasının otomatik üretimi bu bilgiye dayanacak. Modelde yalnızca telin IEC renk kodu (`Wire.Color`) vardı; bağlantının *türü* yoktu.

## Karar

| Konu | Karar |
|---|---|
| Model | `ConnectionKind` (Id, Ad, Renk `#RRGGBB`, `LineStyle`: düz, kesik, kesik-noktalı, noktalı; Kategori: güç/sinyal/haberleşme…). Projede `ConnectionKinds` listesi; `Connection.KindId` isteğe bağlı. Ad projede tekil (büyük/küçük harf duyarsız). |
| Komutlar | `PutConnectionKind` (ekle/yerinde değiştir), `RemoveConnectionKind` (atanmış bağlantılar türsüz kalır, geri almak getirir), `SetConnectionKind`. Kavşakla bölünen bağlantının iki yarısı da türü korur. |
| Standart türler | `ConnectionKind.Standard`: Profinet, Ethernet, EtherCAT, CANopen, RS-485/Modbus, DriveCLiQ, Güvenlik, 24 VDC, 0 V; kimlikler addan türetilir (tekrar eklemek çoğaltmaz). Editörde **Bağlantı > Standart türleri ekle** ve tel sağ tık menüsü. |
| Çizim | Türü olan bağlantı türün rengi ve çizgi biçimiyle çizilir; tür yoksa telin renk kodu, o da yoksa siyah düz. Çizgi biçimi PDF ve PNG'de (kesik desen), SVG'de (`stroke-dasharray`) görünür; DXF/DWG'de renk korunur, çizgi biçimi şimdilik yok (düz). |
| Gösterge | Sayfada kullanılan türler çizim alanının sağ üstünde **otomatik gösterge** olarak listelenir (çizgi örneği + ad); tür kullanılmıyorsa gösterge yoktur. |
| Dosya | `connectionKinds` listesi ve bağlantıda `kind` yalnızca kullanılıyorsa yazılır; çizgi biçimi ve kategori varsayılandaysa yazılmaz. Şema numarası artmaz; eski dosyalar aynen açılır. |
| Editör | Seçili teller için sağ tık > **Bağlantı türü** (tür listesi, "Türsüz"); çoklu seçimde tek adım. |

## Yapılmayanlar

- Tür oluşturma/düzenleme penceresi (renk seçici, çizgi biçimi); şimdilik standart set ve komutlar. Özel tür CLI/yama ile eklenebilir.
- Makro yakalama/yerleştirme türü taşımaz.
- DXF/DWG'de çizgi tipi (linetype); haberleşme şeması sayfası (sıradaki iş) ve tek hat şeması bu bilgiyi kullanacak.

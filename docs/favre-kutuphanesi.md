# B. Favre DXF sembol kütüphanesi (fav0001_dxf_symbol)

Arşiv `docs/fav0001_dxf_symbol/` altında durur ve **depoya girmez** (`.gitignore`; 59 MB).

- **Lisans:** Creative Commons Attribution 4.0 (CC BY 4.0). Yazar anılmalıdır: *B. Favre*. Ticari kullanım, dağıtım ve uyarlama serbest.
  Tek istisna: `fav0611_a2…a5.pdf` (üçüncü tarafın hakları saklı; kullanılmaz). Lisans metni: `licence.pdf`.
- **İçerik:** `fav0001_dxf_library/fav0090_dxf_bloc_library` (262 blok: kontak, röle bobini, buton, sinyal, sensör, zaman rölesi, motor,
  RLC, yarı iletken, trafo, devre kesici, sigorta, klemens…), pnömatik, bağlantı şemaları, tek hat şeması, PLC/sürücü.
- **Aktarma** (semboller ayrı bir kütüphane dosyasına gider, projeye dokunmaz). Kendi kütüphane yolunuzla:

```powershell
dotnet run --project src/Ake.Cli -- lib symbol-import docs/fav0001_dxf_symbol/fav0001_dxf_library/fav0090_dxf_bloc_library --license "CC BY 4.0, B. Favre (fav0001_dxf_symbol)" --library "$env:LOCALAPPDATA\aktapokus-electric\library.db"
```

  Çerçeve ve antet dosyalarını (`fav0090_drawing_frames_iso216`, `fav0095_title_bloc_iso7200`) almamak için ilgili alt klasörleri tek tek
  verin (örn. `.../fav0100_contact`). Uçlar çizgi uçlarından çıkarılır (ADR-0035); çizimde "?" yer tutucuları ve tag metinleri sembolle
  birlikte gelir.
- Ölçek: çizimler mm, sembol yüksekliği 15 mm (bizim ızgarayla uyumlu); `--scale` gerekmez.

# Windows Kişisel App Launcher — Geliştirme Spesifikasyonu

> Bu doküman bir coding agent'a verilmek üzere hazırlanmıştır. Tüm gereksinimler, davranışlar ve kabul kriterleri aşağıdadır. Belirsiz kalan noktalarda "Sade ve klavye öncelikli" ilkesine göre karar ver ve kararını README'de belgele.

---

## 1. Özet

Windows için, global kısayol tuşu ile açılan, **tamamen kullanıcının kendi tasarladığı** hafif bir launcher.

- Windows'ta kurulu uygulamalar **otomatik olarak listelenmez**. Launcher boş başlar; içeriği kullanıcı oluşturur.
- Kullanıcı **iç içe klasörler** oluşturur ve bu klasörlerin altına **uygulama, dosya/klasör yolu, shell komutu** ekler.
- Her node'un **ikonu değiştirilebilir**.
- Launcher açıkken yazmaya başlanırsa, **kullanıcının eklediği tüm node'lar içinde** arama yapılır.
- Tamamen **klavye ile** kullanılabilir.
- Görsel dil: **sade, minimal** — Linux Omarchy'nin launcher'ı (Walker) gibi: ekranın ortasında tek bir panel, üstte arama satırı, altında liste. Süsleme, animasyon yığını, sekme, sidebar yok.

---

## 2. Hedefler ve Hedef Olmayanlar

### Hedefler
- Kısayol tuşuna basıldıktan sonra launcher **< 100 ms** içinde görünür olmalı (arka planda çalışan süreç, pencere gizle/göster).
- Boşta bellek kullanımı düşük olmalı (hedef < 80 MB).
- Tüm işlemler (gezinme, arama, çalıştırma, node ekleme/düzenleme/silme) **mouse'a dokunmadan** yapılabilmeli. Mouse da desteklenmeli ama ikincil.
- Konfigürasyon **insan tarafından okunabilir tek bir JSON dosyasında** tutulmalı; elle düzenlenebilmeli, yedeklenebilmeli, başka makineye kopyalanabilmeli.

### Hedef Olmayanlar
- Kurulu uygulamaları, Başlat menüsünü, dosya sistemini otomatik indekslemek.
- Web araması, hesap makinesi, plugin sistemi, eklenti mağazası.
- Bulut senkronizasyonu (JSON dosyası kullanıcı tarafından OneDrive vb. ile senkronlanabilir; bunun için ekstra iş yapılmayacak).
- Çoklu tema motoru (sadece açık/koyu, sistem temasını takip).

---

## 3. Teknoloji Seçimi

**Önerilen:** C# / **.NET 8** + **WPF**, tek dosya (self-contained, single-file) yayın.

Gerekçe:
- Global hotkey (`RegisterHotKey` Win32 API), ikon çıkarma (`SHGetFileInfo` / `Icon.ExtractAssociatedIcon`), `ShellExecute`, tray icon gibi Windows'a özgü işlerin hepsi doğal ve olgun.
- Pencere gizle/göster ile anında açılış kolay.
- Harici runtime bağımlılığı olmadan tek `.exe` dağıtılabilir.

**Kabul edilebilir alternatif:** Tauri 2 (Rust + hafif web UI). Agent bunu seçerse gerekçesini belgelemeli; gereksinimler değişmez.

**Kullanılmayacaklar:** Electron (açılış süresi ve bellek hedefleriyle uyumsuz).

Önerilen yardımcı kütüphaneler (opsiyonel, gerekirse):
- Tray icon için `H.NotifyIcon.Wpf` veya WinForms `NotifyIcon`.
- Fuzzy arama için kendi basit skorlama algoritman yeterli (bkz. §7); ağır kütüphane ekleme.

---

## 4. Veri Modeli

### 4.1 Node Tipleri

| Tip | Açıklama | Enter davranışı |
|---|---|---|
| `folder` | Başka node'ları içerir. Sınırsız derinlikte iç içe olabilir. | İçine girer (alt listeyi açar) |
| `app` | Çalıştırılabilir dosya (`.exe`, `.lnk`, `.bat`, `.cmd`, `.msc` vb.) | Uygulamayı başlatır |
| `path` | Herhangi bir dosya veya dizin yolu | Varsayılan uygulama ile açar (`ShellExecute`); dizinse Explorer'da açar |
| `command` | Shell komutu | Seçilen shell'de çalıştırır |
| `url` *(opsiyonel, küçük ek iş)* | Web adresi | Varsayılan tarayıcıda açar |

### 4.2 Ortak Alanlar

```jsonc
{
  "id": "uuid",              // benzersiz, değişmez
  "type": "folder|app|path|command|url",
  "name": "Görünen ad",
  "icon": null,              // bkz. §4.4
  "keywords": ["opsiyonel", "arama", "etiketleri"],
  "description": "opsiyonel kısa açıklama, listede ikincil satır olarak gösterilir"
}
```

### 4.3 Tipe Özel Alanlar

```jsonc
// folder
{ "children": [ /* node[] */ ] }

// app
{
  "target": "C:\\Program Files\\...\\app.exe",
  "arguments": "",
  "workingDirectory": null,   // null ise target'ın dizini
  "runAsAdmin": false
}

// path
{ "target": "D:\\Projeler\\rapor.xlsx" }

// command
{
  "command": "git pull && npm run build",
  "shell": "pwsh|powershell|cmd",   // varsayılan: pwsh varsa pwsh, yoksa powershell
  "workingDirectory": null,
  "window": "visible|hidden",        // hidden: arka planda sessiz çalışır
  "keepOpen": true,                  // visible ise, komut bitince pencere açık kalsın mı
  "runAsAdmin": false
}

// url
{ "target": "https://..." }
```

### 4.4 İkon Modeli

```jsonc
"icon": null                                   // otomatik (varsayılan)
"icon": { "kind": "file",  "value": "C:\\icons\\x.png" }   // png, ico, svg (svg opsiyonel)
"icon": { "kind": "exe",   "value": "C:\\...\\a.exe", "index": 0 } // exe/dll içinden ikon
"icon": { "kind": "glyph", "value": "\uE8B7" }  // Segoe Fluent Icons / Segoe MDL2 glyph
"icon": { "kind": "emoji", "value": "📁" }
```

Otomatik ikon kuralları (`icon: null`):
- `folder` → varsayılan klasör glyph'i
- `app` / `path` → hedef dosyanın sistem ikonu (`SHGetFileInfo`)
- `command` → terminal glyph'i
- `url` → dünya/link glyph'i

İkonlar bellekte önbelleğe alınmalı; kullanıcı klasörüne kopyalanan özel ikonlar `%APPDATA%\<AppName>\icons\` altında saklanmalı (kaynak dosya silinse bile çalışsın).

### 4.5 Konfigürasyon Dosyası

- Konum: `%APPDATA%\<AppName>\config.json`
- Yapı:

```jsonc
{
  "version": 1,
  "settings": {
    "hotkey": "Alt+Space",
    "theme": "system|light|dark",
    "startWithWindows": true,
    "closeAfterLaunch": true,
    "maxVisibleItems": 8,
    "defaultShell": "pwsh"
  },
  "root": {
    "id": "root",
    "type": "folder",
    "name": "Root",
    "children": []
  }
}
```

- Kaydetme **atomik** olmalı (geçici dosyaya yaz → rename). Her kayıttan önce bir önceki sürüm `config.backup.json` olarak tutulmalı.
- Dosya dışarıdan değiştirilirse (`FileSystemWatcher`) yeniden yüklenmeli.
- Bozuk JSON durumunda uygulama çökmemeli; kullanıcıya uyarı gösterip yedekten yüklemeyi önermeli.
- `version` alanı ileride migration için kullanılacak.

---

## 5. Arayüz

### 5.1 Pencere

- Kenarlıksız, görev çubuğunda görünmeyen, her zaman üstte, **aktif monitörün** ortasında (üst üçte bir hizasında) açılan tek panel.
- Genişlik ~640 px, yükseklik içeriğe göre (en fazla `maxVisibleItems` satır, fazlası scroll).
- Hafif köşe yuvarlama, ince kenarlık, Windows 11'de mümkünse Mica/Acrylic arka plan; değilse düz renk.
- Odak kaybedilince (başka pencereye tıklanınca) otomatik gizlenir.
- Açıldığında arama kutusu her zaman odaklı ve boş olur; son açık klasörde değil **root'ta** açılır (ayar olarak "son konumu hatırla" eklenebilir, varsayılan kapalı).

### 5.2 Yerleşim

```
┌──────────────────────────────────────────────────┐
│  🔍  ara...                          Root › Dev   │  ← arama satırı + breadcrumb
├──────────────────────────────────────────────────┤
│ ▸ 📁  SAP                                         │
│ ▸ 📁  Scripts                                     │
│   🟦  VS Code                                     │  ← seçili satır vurgulu
│   ⌨  Build & Deploy          git pull && npm ...  │
│   📄  Haftalık Rapor.xlsx                         │
├──────────────────────────────────────────────────┤
│ ↵ aç   → içine gir   ← geri   Ctrl+N ekle   F2 düzenle │  ← küçük, soluk ipucu satırı
└──────────────────────────────────────────────────┘
```

- Her satır: ikon (20–24 px), ad, sağda soluk renkte ikincil bilgi (klasörse alt öğe sayısı, komutsa komutun kısaltılmış hali, arama modunda ise **breadcrumb yolu**).
- Klasörler listede her zaman üstte, sonra diğer node'lar; kendi aralarında kullanıcının belirlediği sırada (sıralama kullanıcı tarafından değiştirilebilir, bkz. §6.3).
- Boş klasörde: "Bu klasör boş — Ctrl+N ile ekle" mesajı.
- İpucu satırı ayarlardan kapatılabilir.

---

## 6. Klavye Davranışı

### 6.1 Gezinme Modu (arama kutusu boş)

| Tuş | Davranış |
|---|---|
| `↑` / `↓` | Seçimi hareket ettir (listenin sonunda başa sar) |
| `Enter` | Klasörse içine gir; değilse node'u çalıştır |
| `→` / `Tab` | Klasörse içine gir |
| `←` / `Backspace` | Üst klasöre çık (root'ta etkisiz) |
| `Esc` | Launcher'ı kapat (hangi klasörde olursa olsun; üst klasöre çıkmak `Backspace`) |
| `Home` / `End` | İlk / son öğe |
| `PageUp` / `PageDown` | Sayfa kaydır |
| Herhangi bir yazılabilir karakter | Arama moduna geç (karakter kutuya yazılır) |

> Not: `Esc` davranışı: arama doluysa → aramayı temizle; değilse → kapat (alt klasörde de; kullanıcı kararı 2026-09-27).

### 6.2 Arama Modu (arama kutusu dolu)

| Tuş | Davranış |
|---|---|
| `↑` / `↓` | Sonuçlar arasında gezin (odak arama kutusunda kalır, yazmaya devam edilebilir) |
| `Enter` | Seçili sonuç klasörse → aramayı temizle ve **o klasörün içine git**; değilse çalıştır |
| `Ctrl+Enter` | Seçili sonucun **bulunduğu klasörü** aç (sonucu orada seçili göster) |
| `Esc` | Aramayı temizle, gezinme moduna dön (bulunulan klasöre) |
| `Backspace` | Normal metin silme; kutu boşalınca gezinme moduna döner |

### 6.3 Düzenleme Kısayolları (her iki modda seçili node üzerinde)

| Tuş | Davranış |
|---|---|
| `Ctrl+N` | Bulunulan klasöre yeni node ekle (tip seçimi ile) |
| `Ctrl+Shift+N` | Bulunulan klasöre yeni klasör ekle (hızlı yol) |
| `F2` | Seçili node'u düzenle |
| `Delete` | Seçili node'u sil (onay iste; klasörse içeriğiyle birlikte silineceğini belirt) |
| `Ctrl+↑` / `Ctrl+↓` | Node'u klasör içinde yukarı/aşağı taşı |
| `Ctrl+X` / `Ctrl+V` | Node'u kes ve başka klasöre yapıştır (taşıma) |
| `Ctrl+D` | Node'u çoğalt |
| `Ctrl+I` | İkon değiştir |
| `Ctrl+,` | Ayarlar |

### 6.4 Global

- Kısayol tuşu (varsayılan `Alt+Space`, ayarlardan değiştirilebilir): launcher gizliyse göster, görünürse gizle.
- Kısayol başka bir uygulama tarafından kullanılıyorsa (RegisterHotKey başarısız): tray bildirimi ile kullanıcıyı uyar ve ayarlar ekranını aç.

---

## 7. Arama

- Arama kapsamı: **tüm ağaç** (bulunulan klasörden bağımsız), tüm node tipleri dahil (klasörler de).
- Eşleşme alanları (öncelik sırasıyla): `name` > `keywords` > `description` > (komutlar için) `command` metni.
- **Fuzzy** eşleşme: karakterler sırayla geçiyorsa eşleşir (`vsc` → "**V**isual **S**tudio **C**ode").
- Skorlama (basit ve deterministik olmalı):
  1. Tam eşleşme
  2. Önek eşleşmesi (ad bu metinle başlıyor)
  3. Kelime başı eşleşmeleri (baş harfler)
  4. Ardışık karakter eşleşmesi (substring)
  5. Dağınık fuzzy eşleşme
  - Eşit skorda: **son kullanım sıklığı/yakınlığı** (bkz. §8.4) yüksek olan önce, sonra ağaçta daha sığ olan önce, sonra alfabetik.
- **Türkçe karakter duyarsızlığı:** `i/İ/ı/I`, `ş/s`, `ğ/g`, `ü/u`, `ö/o`, `ç/c` normalize edilerek eşleşmeli (ör. `rapor` → "Haftalık **Rapor**", `sifre` → "**Şifre** Yöneticisi"). Büyük/küçük harf duyarsız; Türkçe kültüre göre `ToLower` yap.
- Eşleşen karakterler sonuç listesinde **vurgulanmalı** (kalın veya accent renk).
- Her sonucun sağında **breadcrumb yolu** gösterilir (`SAP › Sistemler › Dev`) ki aynı adlı node'lar ayırt edilebilsin.
- Arama her tuş vuruşunda anlık çalışmalı; 5.000 node'da bile < 16 ms hedeflenmeli (ağaç bellekte düz bir indekse dönüştürülüp önbelleğe alınır, config değişince yenilenir).

---

## 8. Çalıştırma Davranışı

### 8.1 Genel
- Çalıştırma sonrası `closeAfterLaunch` true ise launcher gizlenir.
- Çalıştırma hatası (dosya yok, erişim reddi vb.) launcher'ı çökertmemeli; panelin altında kısa bir hata satırı gösterilmeli ve launcher açık kalmalı.
- Hedefi artık var olmayan `app`/`path` node'ları listede soluk/uyarı ikonu ile gösterilmeli.

### 8.2 Tipe Göre
- `app`: `Process.Start` + `UseShellExecute = true`, `arguments`, `workingDirectory`. `runAsAdmin` ise `Verb = "runas"`. UAC iptal edilirse sessizce yoksay.
- `path`: `ShellExecute` ile varsayılan uygulamada aç. Dizinse Explorer.
- `command`:
  - `pwsh` → `pwsh -NoLogo [-NoExit] -Command "<command>"`
  - `powershell` → `powershell -NoLogo [-NoExit] -Command "<command>"`
  - `cmd` → `cmd /c "<command>"` veya `keepOpen` ise `cmd /k "<command>"`
  - `window: hidden` ise pencere açılmaz (`CreateNoWindow = true`); komut hata koduyla biterse tray bildirimi göster.
  - Tırnak/escape işlemleri güvenli yapılmalı; komut metni kullanıcının kendi girdiğidir ama bozuk quoting yüzünden yanlış çalışmamalı. Bunun için birim testleri yaz.
- `url`: `ShellExecute`.

### 8.3 Ortam Değişkenleri
- `target`, `workingDirectory` ve `command` alanlarında `%USERPROFILE%` gibi ortam değişkenleri çalıştırma anında genişletilmeli.

### 8.4 Kullanım İstatistiği
- Her çalıştırmada node için `lastUsed` ve `useCount` güncellenir (config'e değil, ayrı `usage.json` dosyasına — config dosyası kullanıcı tarafından okunabilir ve temiz kalsın).
- Sadece arama sıralamasında eşitlik bozucu olarak kullanılır; gezinme modundaki sırayı değiştirmez.

---

## 9. Node Ekleme / Düzenleme Diyaloğu

Launcher panelinin **içinde** açılan sade bir form (ayrı büyük pencere değil). Klavye ile tamamen doldurulabilir; `Tab` ile alanlar arası geçiş, `Enter` kaydet, `Esc` iptal.

Adımlar:
1. **Tip seçimi** (`Ctrl+N` sonrası): Klasör / Uygulama / Dosya-Dizin / Komut / URL — ok tuşları + Enter veya baş harf (K/U/D/O/L gibi, çakışmayacak şekilde belirle).
2. **Alanlar** tipe göre gösterilir (§4.3). Sadece zorunlu alanlar görünür; gelişmiş alanlar (argümanlar, çalışma dizini, admin, pencere modu) "Gelişmiş ▸" altında katlanır.
3. `target` alanlarında **Gözat…** butonu (dosya seçici). Seçilen dosyadan `name` otomatik doldurulur (exe için dosya açıklaması / ürün adı, yoksa dosya adı uzantısız).
4. **İkon alanı**: önizleme + "Değiştir" (dosya seç, exe/dll'den seç, glyph seç, emoji gir) + "Varsayılana dön".
5. Kaydetme anında doğrulama: ad boş olamaz, `app`/`path` hedefi yoksa uyarı ver ama kaydetmeye izin ver.

**Sürükle-bırak (mouse ile ek kolaylık):**
- Explorer'dan bir dosya/exe/kısayol launcher paneline bırakılırsa, bulunulan klasöre otomatik olarak doğru tipte node eklenir (`.exe/.lnk` → app, diğer dosya/dizinler → path). `.lnk` dosyalarında hedef, argüman ve ikon çözülmeli.
- Liste içinde node'ları sürükleyerek sıralama / klasöre taşıma.

**Sağ tık menüsü:** Aç, Düzenle, İkon Değiştir, Taşı, Çoğalt, Sil, Dosya konumunu aç.

---

## 10. Sistem Entegrasyonu

- **Tek instance:** İkinci kez başlatılırsa mevcut instance'ı öne getirip çıkar (named mutex + pipe/mesaj).
- **Tray ikonu:** Göster, Ayarlar, Config dosyasını aç, Config klasörünü aç, Yeniden yükle, Çıkış.
- **Windows ile başlat:** `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` kaydı; ayardan açılıp kapatılır.
- **Çoklu monitör / DPI:** Per-monitor DPI aware; panel, fare imlecinin veya aktif pencerenin bulunduğu monitörde açılır.
- **Import/Export:** Ayarlar ekranından config'i dışa/içe aktar (içe aktarmada "birleştir" veya "değiştir" seçeneği).

---

## 11. Ayarlar Ekranı

Launcher içinde açılan sade bir sayfa (`Ctrl+,`):
- Kısayol tuşu (tuşa basarak kaydet)
- Tema: Sistem / Açık / Koyu
- Windows ile başlat
- Çalıştırınca kapat
- Görünür satır sayısı
- Varsayılan shell
- Son konumu hatırla
- İpucu satırını göster
- Config klasörünü aç / Dışa aktar / İçe aktar

---

## 12. Proje Yapısı (öneri)

```
/src
  /Launcher.App          # WPF uygulaması, pencere, tray, hotkey
    /Views
    /ViewModels          # MVVM (CommunityToolkit.Mvvm)
    /Services
      HotkeyService.cs
      ConfigService.cs   # load/save/watch/backup/migration
      SearchService.cs   # indeks + fuzzy skorlama
      LaunchService.cs   # tipe göre çalıştırma
      IconService.cs     # çıkarma + önbellek
      UsageService.cs
  /Launcher.Core         # UI'dan bağımsız model + arama + komut oluşturma (test edilebilir)
/tests
  /Launcher.Core.Tests   # xUnit
README.md
```

Arama, config serileştirme ve shell komut satırı oluşturma **Core** projesinde olmalı ve UI olmadan test edilebilmeli.

---

## 13. Kabul Kriterleri

Aşağıdakilerin hepsi sağlanmadan iş tamamlanmış sayılmaz:

1. İlk çalıştırmada launcher **boş** root ile açılır; hiçbir kurulu uygulama otomatik görünmez.
2. Kısayol tuşu ile açılır/kapanır; açılış göze çarpan bir gecikme olmadan gerçekleşir.
3. Kullanıcı klavye ile en az 4 seviye iç içe klasör oluşturabilir ve her seviyeye app, dosya, komut ekleyebilir.
4. Her node'un ikonu değiştirilebilir ve değişiklik yeniden başlatmadan sonra da korunur.
5. Root'tayken yazmaya başlandığında, 3. seviyedeki bir node arama sonuçlarında breadcrumb ile görünür.
6. Arama sonucundaki bir klasöre Enter'a basınca o klasörün içi açılır; son node'a Enter'a basınca çalışır.
7. `Esc` davranışı §6.1'deki sırayı izler.
8. Türkçe karakter normalizasyonu çalışır (`sifre` → "Şifre", `IZMIR` → "İzmir").
9. `pwsh`, `powershell` ve `cmd` komutları; görünür/gizli ve keepOpen kombinasyonlarıyla doğru çalışır.
10. Config dosyası elle düzenlenip kaydedildiğinde launcher değişikliği yeniden başlatmadan yansıtır.
11. Bozuk config uygulamayı çökertmez.
12. Explorer'dan sürüklenen bir `.lnk` dosyası doğru hedef ve ikonla app node'u olarak eklenir.
13. Core projesi için birim testleri geçer (arama skorlaması, Türkçe normalizasyon, komut satırı oluşturma, config round-trip).

---

## 14. Geliştirme Aşamaları

1. **MVP:** Hotkey, pencere göster/gizle, JSON'dan ağaç yükleme, klavye ile gezinme, Enter ile çalıştırma (4 tip).
2. **Arama:** Düz indeks, fuzzy skorlama, Türkçe normalizasyon, vurgulama, breadcrumb.
3. **Düzenleme:** Ekle/düzenle/sil/taşı/sırala formları ve kısayolları, atomik kayıt + yedek.
4. **İkonlar:** Otomatik çıkarma, önbellek, özel ikon seçimi (dosya/exe/glyph/emoji).
5. **Sistem:** Tray, tek instance, Windows ile başlat, çoklu monitör/DPI, dosya izleme.
6. **Cila:** Sürükle-bırak, `.lnk` çözümleme, ayarlar ekranı, import/export, kullanım istatistiği, tema.

Her aşama sonunda uygulama çalışır durumda olmalı ve README güncellenmeli.

---

## 15. Teslimatlar

- Kaynak kod ve çözüm dosyası
- `dotnet publish` ile self-contained, single-file `win-x64` `.exe`
- README: kurulum, kısayollar tablosu, config dosya formatı, örnek config
- Örnek `config.example.json` (birkaç klasör, app, komut içeren)

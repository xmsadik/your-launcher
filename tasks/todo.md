# Your Launcher — Uygulama Planı

Kaynak: `launcher-spec.md`. Bu plan onaylanmadan implementasyona başlanmaz.

## 0. Kararlar (onaylandı 2026-09-26)

| # | Konu | Öneri | Gerekçe |
|---|---|---|---|
| D1 | Runtime | **.NET 10 (LTS)** + WPF, spec'teki .NET 8 yerine | Makinede sadece SDK 10.0.401 var; .NET 8 desteği **10 Kasım 2026'da bitiyor** (~6 hafta). Gereksinimler değişmez. |
| D2 | Uygulama adı | **Your Launcher** (kullanıcı kararı, 2026-09-26). Görünen ad / exe / `%APPDATA%\Your Launcher\` = "Your Launcher"; kod ad alanı ve proje adları boşluksuz `YourLauncher` | Kullanıcı launcher'ı kendisi tasarlıyor → daha çarpıcı isim |
| D3 | MVVM | CommunityToolkit.Mvvm (spec önerisi) | Source generator, sıfır reflection maliyeti |
| D4 | Tray | WinForms `NotifyIcon` (`UseWindowsForms=true`) | Ek paket yok; H.NotifyIcon'dan hafif |
| D5 | JSON | `System.Text.Json` + source-gen context, polimorfik node (`type` discriminator), yorum/trailing comma toleranslı okuma | Hızlı, reflection'sız, elle düzenlemeye dayanıklı |
| D6 | Tip seçimi | Her tip için küçük vektörel ikon (Segoe Fluent glyph) + İngilizce kısayol harfi: **F**older / **A**pp / **P**ath / **C**ommand / **U**RL | Kullanıcı kararı: arayüz İngilizce, harf yerine ikon |
| D8 | Arayüz dili | **İngilizce** (tüm UI metinleri, mesajlar, ipucu satırı). Arama TR normalizasyonu aynen kalır | Kullanıcı kararı 2026-09-26 |
| D7 | Yayın | self-contained single-file win-x64, **ReadyToRun** açık, trimming kapalı (WPF desteklemiyor) | Açılış hızı; bellek hedefi (<80 MB) WPF ile sınırda → ölçülecek |

Bilinen riskler:
- `Alt+Space` Windows pencere menüsü / PowerToys Run ile çakışabilir → RegisterHotKey hata yolu (tray uyarısı + ayarlar) zaten spec'te; ilk testte doğrulanacak.
- WPF boşta bellek ~50–70 MB; hedef sınırda. Gizlenirken `SetProcessWorkingSetSize` ile working set kırpma opsiyonu.
- Mica/Acrylic: Win11'de `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)`; başarısızsa düz renk.

## 1. Çözüm Yapısı

```
YourLauncher.sln
src/Launcher.Core/            (net10.0, UI yok)
  Model/        Node, FolderNode, AppNode, PathNode, CommandNode, UrlNode, IconSpec, Settings, LauncherConfig
  Config/       ConfigSerializer (STJ), ConfigMigrator(version), TreeOps (ekle/sil/taşı/çoğalt/sırala, parent lookup)
  Search/       TextNormalizer (TR), FlatIndex, FuzzyScorer, SearchEngine
  Launch/       CommandLineBuilder (pwsh/powershell/cmd quoting), EnvExpander, LaunchPlan (ProcessStartInfo'ya UI'sız karşılık)
  Usage/        UsageStats model
src/Launcher.App/             (net10.0-windows, WPF)
  App.xaml(.cs) tek instance, DI kurulumu, tray
  Interop/      Win32 (RegisterHotKey, SHGetFileInfo, ExtractIconEx, MonitorFromPoint, DWM, IShellLink)
  Services/     HotkeyService, ConfigService, SearchService, LaunchService, IconService, UsageService,
                SingleInstanceService, StartupService, ThemeService, TrayService, ShortcutResolver
  ViewModels/   MainViewModel, ListItemViewModel, EditorViewModel, TypePickerViewModel, IconPickerViewModel, SettingsViewModel
  Views/        MainWindow (panel), EditorView, TypePickerView, IconPickerView, SettingsView (hepsi panel içi UserControl)
tests/Launcher.Core.Tests/    (xUnit)
README.md, config.example.json
```

## 2. Aşamalar ve Görevler

Her aşama sonunda: build yeşil, testler yeşil, uygulama çalışır, README güncel.

### Aşama 1 — MVP
- [x] Solution + 3 proje iskeleti, `Directory.Build.props` (nullable, warnings-as-errors, LangVersion latest)
- [x] Core model + STJ polimorfik serileştirme; boş config yoksa oluştur (root boş — AC1)
  - Not: yerleşik `[JsonPolymorphic]`/`[JsonDerivedType]` kaynak-üretimi discriminator'ın JSON'da **ilk** alan olmasını gerektiriyor; config elle düzenlendiği ve spec örnekleri "id"yi "type"tan önce koyduğu için bunun yerine sıraya bağımsız çalışan elle yazılmış `NodeJsonConverter` kullanıldı (bkz. README "Key decisions" D5).
- [x] ConfigService: load (bozuk JSON → çökme yok, uyarı; AC11 temel), save (temp → `File.Replace`, önceki → `config.backup.json`)
- [x] MainWindow: kenarlıksız, `ShowInTaskbar=false`, Topmost, 640 px, arama kutusu + breadcrumb + liste + ipucu satırı; `Deactivated` → gizle
- [x] HotkeyService (RegisterHotKey + HwndSource hook), toggle; açılışta root + boş arama + odak
- [x] Gezinme klavyesi §6.1 (↑↓ wrap, Enter/→/Tab, ←/Backspace, Esc zinciri, Home/End, PgUp/PgDn, yazılabilir karakter → arama)
- [x] Klasörler üstte sıralama; boş klasör mesajı; ikincil bilgi (alt öğe sayısı / komut özeti)
- [x] CommandLineBuilder + LaunchService (app/path/command/url), env genişletme, runas + UAC iptali (Win32Exception 1223) sessiz
- [x] Hata satırı (panel altında), launcher açık kalır; `closeAfterLaunch`
- [x] Testler: config round-trip, CommandLineBuilder (quoting: tırnak, `&&`, `%`, `"` içeren, boşluklu yol, keepOpen/NoExit kombinasyonları) — 37/37 yeşil

Aşama 1 dışında bırakılanlar (bilerek, plana göre): arama yalnızca bulunulan klasörde basit `Contains` filtresi (gerçek fuzzy/whole-tree arama Aşama 2), Ctrl+N/F2/Delete gibi düzenleme kısayolları henüz yok (Aşama 3, ama hint bar metninde referans veriliyor), özel ikonlar (Aşama 4), tray/tek-instance/dosya izleme (Aşama 5).

### Aşama 2 — Arama
- [x] TextNormalizer: `tr-TR` ToLower + ı→i, ş→s, ğ→g, ü→u, ö→o, ç→c (İ/I → i). Orijinal↔normalize indeks eşlemesi korunur (vurgulama için)
- [x] FlatIndex: ağaç → düz dizi (node, normalize alanlar, breadcrumb, derinlik); config değişince yeniden kur
- [x] FuzzyScorer: kademe (tam > önek > kelime başı > substring > dağınık) × alan ağırlığı (name > keywords > description > command); eşleşen pozisyonları döndür
- [x] Eşitlik bozucu: usage skoru → derinlik → alfabetik (TR kültürü)
- [x] Arama modu klavyesi §6.2 (Enter klasöre gir, Ctrl+Enter bulunduğu klasörde seçili aç, Esc, Backspace)
- [x] UI: eşleşen karakterleri accent ile vurgula (TextBlock Inlines), sağda breadcrumb
- [x] Testler: skor sıralaması, `vsc`→Visual Studio Code, `sifre`→Şifre, `IZMIR`→İzmir, `rapor`→Haftalık Rapor, 5.000 node < 16 ms (benchmark testi)

### Aşama 3 — Düzenleme (tamamlandı 2026-09-26)
- [x] TreeOps (Core, test edilir): ekle, sil, yukarı/aşağı, kes/yapıştır (kendi alt ağacına yapıştırmayı engelle), çoğalt (yeni id'ler, derin kopya)
- [x] Panel içi TypePicker (vektörel ikon + ad; ok + Enter + F/A/P/C/U) ve Editor formu: tipe göre alanlar, "Gelişmiş ▸" katlanır bölüm, Tab/Enter/Esc
- [x] Gözat… (OpenFileDialog / klasör seçici); exe'den ad çıkarma (FileVersionInfo.FileDescription → ProductName → dosya adı)
- [x] Doğrulama: boş ad engeli; hedef yoksa uyarı ama kaydet
- [x] Kısayollar §6.3: Ctrl+N, Ctrl+Shift+N, F2, Delete (panel içi onay; klasör uyarısı), Ctrl+↑/↓, Ctrl+X/V, Ctrl+D
- [x] Her değişiklik → atomik kayıt; kendi yazımımızı FileSystemWatcher'da yoksay (ConfigService.LastSelfWriteUtc, `// Phase 5` notu)
- [x] Testler: TreeOps (+ TargetNameHelper)

### Aşama 4 — İkonlar (tamamlandı 2026-09-26)
- [x] IconService: `SHGetFileInfo` (sistem ikonu), `SHDefExtractIconW`/`ExtractIconEx` (exe/dll + index), png/ico dosya, glyph (Segoe Fluent Icons → MDL2 fallback), emoji (Segoe UI Emoji)
- [x] Bellek önbelleği (anahtar: `IconKey`), `Task<ImageSource?>` ile `GetOrAdd` (eşzamanlı istekleri birleştirir, başarısızlığı da önbelleğe alır), ayrıca shell ikon konumuna göre paylaşılan bitmap önbelleği; `BitmapSource.Freeze()`; arka planda `Task.Run` + `SemaphoreSlim(2)` (elle STA thread yerine, spec §7.3)
- [x] Özel dosya ikonlarını `<config dizini>\icons\` altına kopyala (hash adlı, `IconFileStore`) — AC4; config'te **göreli** yol saklanır (taşınabilirlik, spec §7.5)
- [x] Ctrl+I / IconPicker: Glyph (filtre + ~70 ikonluk ızgara), Emoji (tek grapheme doğrulaması), File (Gözat… → import), Exe/DLL (yol kutusu + Gözat…/Load + ızgara, 1024 sınırı, kademeli doldurma); sekmeler arası Ctrl+Tab/Ctrl+1..4, Ctrl+0 varsayılana dön
- [x] Hedefi olmayan app/path: soluk (Opacity 0.55) + uyarı rozeti + tooltip; `TargetCheck`/`PathResolver` ile bare-name (`%PATH%`) çözümlemesi editör ve satır rozetinde aynı

### Aşama 5 — Sistem
- [ ] Tek instance: named mutex + named pipe "show" mesajı
- [ ] Tray: Göster, Ayarlar, Config dosyasını aç, Config klasörünü aç, Yeniden yükle, Çıkış; gizli komut hata kodu bildirimi
- [ ] Windows ile başlat (HKCU Run, değer adı "Your Launcher", exe yolu tırnaklı): `settings.startWithWindows` ile senkron; açılışta registry durumu ayarla eşitlenir (exe taşındıysa yol güncellenir)
- [ ] Per-monitor DPI v2 manifest; açılışta imlecin monitörü, üst üçte bir konum
- [ ] FileSystemWatcher (debounce ~200 ms, kilitli dosyaya retry) → yeniden yükle (AC10); bozuksa uyarı + "yedekten yükle" (AC11)
- [ ] Hotkey başarısızsa tray uyarısı + ayarları aç
- [ ] Mica/Acrylic + köşe yuvarlama (Win11), fallback düz renk

### Aşama 6 — Cila
- [ ] Drag&drop Explorer'dan: `.exe/.lnk` → app, diğerleri → path; `.lnk` çözümleme (IShellLinkW: hedef, argüman, çalışma dizini, ikon) — AC12
- [ ] Liste içi sürükle: sıralama + klasöre bırak
- [ ] Sağ tık menüsü (Aç, Düzenle, İkon Değiştir, Taşı, Çoğalt, Sil, Dosya konumunu aç)
- [ ] Ayarlar sayfası §11 (hotkey kaydı tuşa basarak, tema, **"Start with Windows" açma/kapama anahtarı** (kullanıcı talebi 2026-09-26; değişince registry anında güncellenir), closeAfterLaunch, maxVisibleItems, defaultShell, son konumu hatırla, ipucu satırı)
- [ ] Import/Export (birleştir: id çakışmasında yeni id / değiştir)
- [ ] UsageService (`usage.json`, debounce'lu kayıt) → arama eşitlik bozucusu
- [ ] ThemeService: sistem teması (registry `AppsUseLightTheme` + `SystemEvents.UserPreferenceChanged`), açık/koyu ResourceDictionary

### Teslimat
- [ ] `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishReadyToRun=true`
- [ ] README: kurulum, kısayol tablosu, config formatı, kararlar (D1–D7)
- [ ] `config.example.json` (iç içe klasörler, app, path, komut örnekleri)

## 3. Doğrulama (kabul kriterleri)

| AC | Nasıl doğrulanacak |
|---|---|
| 1, 10, 11 | Temiz `%APPDATA%` ile başlat; config'i elle düzenle / bozarak kaydet |
| 2 | Hotkey → görünürlük süresi Stopwatch log'u (< 100 ms) |
| 3, 4 | Klavye ile 4 seviye oluştur, ikon değiştir, yeniden başlat |
| 5, 6, 7, 8 | Core testleri + elle senaryo |
| 9 | 3 shell × {visible, hidden} × {keepOpen true/false} matrisi elle + CommandLineBuilder testleri |
| 12 | Başlat menüsünden bir `.lnk` sürükle |
| 13 | `dotnet test` yeşil |
| Bellek | Publish edilmiş exe, boşta Working Set ölçümü |

## 4. Çalışma Şekli
- Core (arama, serileştirme, komut satırı, TreeOps) test-first.
- Her aşama ayrı onay noktası; aşama sonunda kısa özet + review bölümü bu dosyaya eklenir.

## Review

### Aşama 1 — MVP (tamamlandı 2026-09-26)
- Build: `dotnet build` (Debug) — 0 uyarı, 0 hata, 3 proje.
- Test: `dotnet test` — 37/37 yeşil (config round-trip incl. 4 seviye iç içe + 5 node tipi, comment/trailing-comma toleransı, unknown-property toleransı, corrupt→typed-failure; ConfigStore create-default/backup/corrupt-untouched; CommandLineBuilder pwsh/powershell/cmd × visible-keepOpen/visible-noKeepOpen/hidden matrisi, quoting/env-expansion/pwsh-fallback; config.example.json parse smoke test).
- Smoke test: exe başlatıldı, 3 sn sonra `HasExited=False`, `%APPDATA%\Your Launcher\config.json` oluşturuldu (önceki gerçek config yedeklenip sonra geri yüklendi); `YOURLAUNCHER_CONFIG_DIR` ile ayrı bir scratch dizinine bozuk JSON yazılıp uygulamanın çökmediği, dosyanın değişmediği doğrulandı.
- Sapma: STJ'nin yerleşik polimorfik discriminator kaynak-üretimi "type" alanının JSON'da ilk sırada olmasını şart koyuyor; config elle düzenlenebilir olduğu ve spec örnekleri "id"yi önce koyduğu için bunun yerine sıraya bağımsız `NodeJsonConverter` yazıldı (README D5).
- Bilerek bırakılanlar: gerçek arama (Aşama 2), düzenleme kısayolları (Aşama 3), özel ikonlar (Aşama 4), tray/tek-instance/dosya izleme (Aşama 5) — hepsi plana uygun.

### Aşama 1 — Orkestratör incelemesi (2026-09-26)
Sonnet çıktısı incelendi; build 0 uyarı, 37/37 test yeşil doğrulandı. Düzeltilenler:
- Liste metni soluk görünüyordu (ListBox sistem Foreground'u alıyor) → `Foreground` açıkça verildi.
- Seçim vurgusu görünmüyordu (Items.Clear sonrası aynı SelectedIndex değeri bildirim tetiklemiyordu) → önce -1'e sıfırlanıyor.
- `maxVisibleItems` uygulanmamıştı → sabit satır yüksekliği (36) × N ile `MaxHeight`; seçim değişince `ScrollIntoView`.
- Glyph'ler görünmez PUA karakterleriydi → `\uXXXX` kaçışları.
- `Process` nesneleri dispose edilmiyordu → düzeltildi.
Görsel duman testi: örnek config ile panel, ↓ gezinme, Enter ile klasöre giriş ve breadcrumb doğrulandı.
Not: Debug build boşta ~147 MB Working Set; bellek hedefi Release/R2R ile Aşama 5'te ölçülecek.

### Aşama 2 — Arama (tamamlandı 2026-09-26)
- Build: `dotnet build` (Debug) — 0 uyarı, 0 hata.
- Test: `dotnet test` — 72/72 yeşil (Aşama 1'in 37'si + arama için 35 yeni: TextNormalizer, FuzzyScorer
  kademe testleri (Exact/Prefix/WordStart/Substring/Fuzzy sıralaması + `vsc`→"Visual Studio Code" vs.
  "vscode-notes.txt" vs. "Visual Basic" senaryosu), SearchEngine (alan ağırlıkları, eşitlik bozucu
  zinciri — usage > derinlik > TR alfabetik, çok kelimeli sorgu, 3. seviye node + breadcrumb),
  5.000-node performans testi).
- Performans: 5.000 node, 20 karışık sorgu (1–6 karakter) — **medyan ≈ 8,19 ms, p95 ≈ 9,83 ms**
  (bütçe: medyan < 16 ms). `SearchPerformanceTests.Search_5000Nodes_MedianUnder16Milliseconds`.
- Sapmalar / netleştirmeler:
  - Plandaki "orijinal↔normalize indeks eşlemesi korunur" ifadesi ayrı bir eşleme yapısına değil, daha
    basit bir çözüme dönüştü: `TextNormalizer.Normalize` karakter-karakter, çıktı girişle **tam aynı
    uzunlukta** olacak şekilde çalışıyor (her karakter 1 karaktere eşleniyor), böylece normalize
    metindeki bir eşleşme indeksi doğrudan orijinal metindeki indekstir — ayrı bir index map'e gerek
    kalmadı.
  - WordStart kademesi kelime-başı akronim/greedy-prefix eşleşmesini backtracking'li DP ile buluyor
    (`FuzzyScorer.TryMatchWordStart`); başarısız durumlar memoize edilerek üstel patlama engelleniyor.
    camelCase kelime-başı tespiti sadece `name` alanı için (orijinal, büyük/küçük harfli metinden)
    önceden hesaplanıyor (`FlatIndex.NameWordStarts`); diğer alanlar (keywords/description/command)
    normalize (küçük harfli) metin üzerinden ayraç-bazlı kelime başı kullanıyor — camelCase zaten
    anlamsız çünkü o alanlar zaten küçük harfe indirgenmiş.
  - `vsc` → "vscode-notes.txt" (Prefix) her zaman "Visual Studio Code" (WordStart) üstünde çıkıyor;
    görev tanımında da bu davranış "beklenen ve doğru" olarak işaretlenmişti.
  - Kullanım (usage) skoru için `SearchEngine` constructor'ı `Func<string,double>?` alıyor, varsayılan
    `null` → her node için 0; Aşama 6'da `usage.json` bu fonksiyonu doldurup geçecek.
  - UI: Arama modu/gezinme modu ayrımı tamamen `SearchText` boş mu değil mi'ye bağlı
    (`MainViewModel.IsSearchMode`); ipucu satırı ve boş-liste mesajı buna göre değişen bindable
    property'ler oldu (`HintText`, `EmptyMessage`). Ctrl+Enter ve arama modunda ←/→/Home/End/Tab'ın
    normal TextBox davranışına bırakılması `MainWindow.xaml.cs`'te mod kontrolüyle yapıldı.
  - `config.example.json`'a `keywords`/`description` alanları eklendi (VS Code, Notepad, Build & Deploy,
    Şifre Yöneticisi, Haftalık Rapor) — anahtar kelime ve açıklama araması elle denenebilir hale geldi;
    `ExampleConfigSmokeTest` yeşil kaldı.
- Bilerek bırakılanlar (plana uygun): düzenleme kısayolları (Aşama 3), özel ikonlar (Aşama 4),
  tray/tek-instance/dosya izleme (Aşama 5), gerçek kullanım istatistiği ve tema (Aşama 6).

### Aşama 2 — Orkestratör incelemesi (2026-09-26)
Build 0 uyarı, 72/72 test yeşil (perf: 5.000 node medyan ~8 ms, p95 ~10 ms) doğrulandı. SearchEngine/TextNormalizer/tuş işleyicisi incelendi, düzeltme gerekmedi.
Görsel test: `sifre` → "Şifre Yöneticisi" (vurgulu), `vsc` → "VS Code" + "Dev › Tools" breadcrumb, Esc aramayı temizliyor.
Bilinen sınırlama: çok kelimeli sorguda her kelime aynı alanda eşleşmeli (ör. biri adda, biri keyword'de olan eşleşmez) — şimdilik kabul.

### Aşama 3 — Düzenleme (tamamlandı 2026-09-26)
- Build: `dotnet build` (Debug) — 0 uyarı, 0 hata.
- Test: `dotnet test` — 113/113 yeşil (Aşama 1+2'nin 72'si + Aşama 3 için 41 yeni: `TreeOpsTests`
  — FindParent/FindParentById (root seviyesi, iç içe, ağaçta yok, root'un kendisi), Add (index'li/
  index'siz), Remove (root seviyesi, klasör + alt ağacı, ağaçta yok), MoveUp/MoveDown (grup başı/sonu,
  karışık klasör+öğe grupları — öğe kendi grubunun dışına "sıçramıyor", tek elemanlı grup, parent'ı
  olmayan node), MoveTo (farklı klasöre taşı, zaten hedefteyse no-op, hedef node'un kendisi → reddedilir,
  hedef kendi alt ağacında → reddedilir, ilgisiz klasöre taşı), Duplicate (yaprak, klasör derin kopya +
  bağımsızlık kanıtı — klonu değiştirmek orijinali etkilemiyor, birden fazla çoğaltmada tüm ağaçta
  benzersiz id, ağaçta olmayan node, icon/description bağımsız kopyalanıyor), NewId; `TargetNameHelperTests`
  — boş/whitespace, var olan dizin (sonda `\` ile/siz), var olan dosya (exe: FileDescription lookup var/
  null/hiç verilmemiş → dosya adına düş; exe olmayan: uzantısız ad), URL (http/https, şema'sız çıplak host),
  var olmayan Windows yolu/UNC/exe yolu, şema/yol şekli olmayan düz metin → metnin kendisi.
- Sapmalar / netleştirmeler:
  - **Bozuk config'te düzenleme**: görev metni "block saves ... (edits are refused)" ifadesini iki farklı
    şekilde okumaya açıktı — sadece kaydetmeyi mi engellemeli (bellekte değişiklik kalsın), yoksa
    düzenlemenin başlamasını mı? Önce ilkini uyguladım (node bellekte eklenip listede görünüyordu, sadece
    dosyaya yazılmıyordu); orkestratör/kendi incelememde bunun kafa karıştırıcı olduğuna (yeniden
    başlatınca sessizce kaybolan "hayalet" bir node) ve ikinci, daha kelimesi kelimesine okumanın
    ("edits are refused") daha güvenli olduğuna karar verdim. Son haliyle: `MainViewModel.BlockIfReadOnly()`
    her mutasyon giriş noktasının (Ctrl+N, Ctrl+Shift+N, F2, Delete, Ctrl+D, Ctrl+V, Ctrl+↑/↓) en başında
    çalışıyor ve `TreeOps`'a hiç dokunmadan hata satırını gösterip çıkıyor — F2 özel bir risk taşıyordu
    çünkü `EditorViewModel.RequestSave()` düzenlenen node'u `Saved` event'i tetiklenmeden **önce** yerinde
    mutasyona uğratıyor; bu yüzden F2 için engelleme editör hiç açılmadan, `BeginEditSelected()`'ın en
    başında yapılıyor. `Ctrl+X` (salt kesme, ağaca dokunmuyor) bilerek engellenmedi — gerçek mutasyon
    `Ctrl+V`'de oluyor ve orada engelleniyor.
  - **TypePicker/Editor'ın DataContext + Visibility birlikte kullanımı**: İlk denemede
    `<local:EditorView DataContext="{Binding Editor}" Visibility="{Binding ShowEditorPage}" />` yazılmıştı;
    ikisi de aynı elemanda olduğu için `Visibility` binding'i (kendi DataContext'i artık `Editor`'a
    çevrildiğinden) `EditorViewModel` üzerinde var olmayan bir `ShowEditorPage` özelliği arıyordu, sessizce
    başarısız oluyordu ve `Visibility` varsayılanı olan `Visible`'da kalıyordu — bu da TypePicker ve
    Editor'ın ekranda üst üste binmesine yol açıyordu (canlı duman testinde ekran görüntüsüyle
    yakalandı). Düzeltme: `Visibility` bağlamayı DataContext'i değişmeyen bir sarmalayıcı `Grid`'e taşımak
    (bkz. `MainWindow.xaml` yorumu). Aynı sınıf hata `EmptyMessage` TextBlock'unda da vardı (sayfa
    fark etmeksizin `IsEmpty` true olduğunda görünür kalıyordu) — `MultiDataTrigger` ile `IsListPage`
    şartı eklendi.
  - **Alt+A**: WPF'te Alt basılıyken tuş olayları "system key" olarak gelir — `KeyEventArgs.Key` değeri
    `Key.System` olur, gerçek tuş `SystemKey`'dedir (WinForms'taki ayrı `SystemKeyDown` olayının aksine).
    `EditorView`'ın kısayol işleyicisi bunu hesaba katıyor.
  - **ComboBox koyu tema**: WPF'in varsayılan ComboBox ControlTemplate'i basit Background/Foreground
    Setter'larını kapalı kutu ve popup'ın iç kromuna yansıtmıyor (sistem/tema renkleri sabit kalıyor) —
    Shell ve Window seçicileri ilk halde okunaksız çıkıyordu (görüntüyle doğrulandı). Küçük bir özel
    ControlTemplate (`EditorView.xaml`) ile düzeltildi.
  - Kimlikler (id) Aşama 1'deki gibi `Guid.NewGuid().ToString()` ("D" formatı, tireli) — `TreeOps.NewId()`
    aynısını kullanıyor.
  - Editor'da F2 ile düzenleme, node'u **yerinde** (aynı referans, aynı id, aynı konum) mutasyona uğratıyor;
    Ctrl+N ile ekleme ise `Kind`'e göre taze bir node inşa edip `TreeOps.Add` ile klasöre ekliyor. İkisi de
    aynı `EditorViewModel.RequestSave()` → `Saved` event akışından geçiyor, sadece hedef node farklı.
  - Panel "sayfaları" (`PanelPage`): `List`/`ConfirmDelete` arama kutusunu ve listeyi göstermeye devam
    ediyor (ConfirmDelete salt bir alt bilgi çubuğu ekliyor); `TypePicker`/`Editor` tamamen ayrı
    `UserControl`'lere geçiyor ve kendi tuş işleyicilerine sahipler — MainWindow'un arama kutusu tuş
    işleyicisi zaten o an odakta olmadığından ayrıca "çalışmasın" diye bir kontrol gerekmedi (odak o
    TextBox'ta değilken olay zaten ona gelmiyor).
- AC3 duman testi (`YOURLAUNCHER_CONFIG_DIR` → geçici scratch dizini, `System.Windows.Forms.SendKeys` +
  `SetProcessDPIAware`): 4 seviyeli klasör zinciri (Ctrl+Shift+N ×4 + →), en derin seviyede app
  (`C:\Windows\notepad.exe`, Tab ile ad "Notepad" olarak otomatik dolduruldu), command (`Get-Date`,
  Ctrl+Enter ile kaydedildi), path (`...\hosts`, ad "hosts" olarak otomatik dolduruldu) eklendi; path
  node'u F2 ile "Hosts File" olarak yeniden adlandırıldı, Ctrl+D ile çoğaltıldı, Ctrl+↑ ile taşındı,
  Delete+Enter ile kopyası silindi, Ctrl+X → üst klasöre git (←) → Ctrl+V ile taşındı. Sonuç
  `config.json` beklenen ağaçla birebir eşleşti: `Root › Level1 › Level2 › Level3 › { Level4: [Notepad
  (app), Build Test (command)], Hosts File (path) }`. Tip seçici ve editör (App/Command-Gelişmiş-açık/
  Path) ekran görüntüleri incelendi, düzeltmelerden (yukarıdaki sapmalar) sonra temiz çıktı.
- Bozuk config duman testi: ayrı bir scratch dizinine `{ this is not valid json, oops` yazıldı; uygulama
  başlatıldı, Ctrl+Shift+N basıldı → "Config is read-only because config.json could not be read. Fix or
  restore it first." hata satırı anında görüldü, hiçbir node eklenmedi (liste boş kaldı); işlem
  sonlandırıldıktan sonra dosyanın SHA-256 karması öncesiyle birebir aynı çıktı.
- Bilerek bırakılanlar (plana uygun): özel ikon seçici (Aşama 4), tray/tek-instance/dosya izleme
  (Aşama 5), sürükle-bırak/`.lnk`/ayarlar sayfası/import-export/kullanım istatistiği/tema (Aşama 6).

### Aşama 3 — Orkestratör incelemesi (2026-09-26)
Build 0 uyarı, 114/114 test yeşil. Görsel test: tip seçici (ikon + F/A/P/C/U), App editörü (Tab ile ad otomatik doldu), kayıt. Düzeltilenler:
- "Target not found — saved anyway." uyarısı editör kapanınca kayboluyordu → listedeki mesaj satırında gösteriliyor.
- URL şema kontrolü `://` arıyordu (`mailto:` bozuluyordu) → gerçek şema kontrolü; `host:port` ve düz alan adı `https://` alır.
- config.json'da alan sırası okunaksızdı (id/name sonda) → `JsonPropertyOrder`: id, type, name, tipe özel, keywords, description, icon, children. Testi eklendi.

### Aşama 4 — İkonlar (tamamlandı 2026-09-26)
- Build: `dotnet build` (Debug, her iki proje) — 0 uyarı, 0 hata.
- Test: `dotnet test` — **172/172 yeşil** (Aşama 1–3'ün 114'ü + ikonlar için 58 yeni, hepsi Core):
  `IconKeyTests` (glyph/emoji → null, file/exe anahtar kararlılığı ve ayrışması, index null==0,
  env-expand + case-insensitive normalize, auto app/path → `sys|...`, auto folder/command/url →
  `glyph|default-*`), `IconFileStoreTests` (hash adlandırma, aynı içerik → tekilleştirme, farklı içerik →
  ayrı dosya, izin verilen/verilmeyen uzantı matrisi, dizin otomatik oluşturma), `TargetCheckTests`
  (`ShouldCheckExistence` matrisi: yerel tam yol/UNC/URI-şema/bare-name/boş/göreli; `IsMissing`: var olan
  dosya/dizin, yok olan yerel yol, UNC/URL hiç kontrol edilmiyor, bare-name PATH'te var/yok, env expand),
  `PathResolverTests` (uzantılı tam eşleşme, uzantısız + PATHEXT, çoklu dizin sırası, bulunamadı, gerçek
  `notepad.exe` → System32 üzerinden PATH'te bulunuyor — canlı ortam sağlaması).
- Canlı doğrulama (`YOURLAUNCHER_CONFIG_DIR` → `%TEMP%\YourLauncherPhase4Scratch`, `config.example.json`
  kopyası + hedefi olmayan bir `app` node'u (`C:\nope\missing.exe`) + bare-name `notepad.exe` + özel glyph/
  emoji ikonlu iki node eklendi): `SetProcessDPIAware` + tam ekran `System.Drawing` yakalama ile
  ekran görüntüleri alındı ve incelendi — liste gerçek sistem ikonlarını gösteriyor (Documents → belge,
  Haftalık Rapor → Excel, bare `notepad.exe` → doğru PATH çözümlemesiyle Notepad ikonu), hedefi olmayan
  3 satır (`Şifre Yöneticisi`, `Missing App`, `Haftalık Rapor` — üçü de bu makinede gerçekten yok) soluk +
  turuncu uyarı rozetiyle işaretlendi; IconPicker'ın Glyph sekmesi (filtre + ızgara + mevcut ikon
  ön-seçili) ve Exe/DLL sekmesi (`shell32.dll`'den 335 ikon, kademeli dolan 10 sütunlu ızgara) görsel
  olarak doğrulandı. Uçtan uca kayıt testi: Glyph sekmesinde farklı bir ikon seçilip Enter'a basıldı →
  liste satırı anında güncellendi → `config.json`'da `icon: { "kind": "glyph", "value": "\uE8B7" }` olarak
  atomik kaydedildi; Emoji sekmesinde metin değiştirilip Enter'a basıldı → aynı şekilde
  `icon: { "kind": "emoji", "value": "★" }` olarak kaydedildi. `Esc` ile iptalde ikon değişmeden listeye
  dönüldü, seçim korundu.
- **Bulgu (spec §7.10'un istediği doğrulama): emoji render rengi.** .NET 10 WPF + Segoe UI Emoji,
  gerçek resimsel emojileri (🚀 gibi) **tam renkli** çiziyor; ★ gibi sembol/dingbat karakterler ise
  **tek renkli (mono)** çiziliyor — bu Windows'un emoji fontunun kendi ayrımı (hangi kod noktalarının
  "renkli sunum" kümesinde olduğu), WPF'e özgü bir sınırlama değil. Kullanıcıya gösterilecek metinde
  ("emoji girin") bu ayrım belirtilmedi; pratikte insanların "emoji" olarak seçtiği neredeyse her şey
  (yüzler, nesneler, bayraklar) tam renkli çıkıyor.
- Sapmalar / netleştirmeler:
  - **Elle yapılan P/Invoke seçimi**: `SHGetFileInfo`/`ExtractIconEx`/`SHDefExtractIconW`, `SHFILEINFO`
    struct'ının sabit uzunluklu string alanı (`szDisplayName`) yüzünden `LibraryImport` kaynak
    üretecinin doğrudan desteklemediği bir marshalling istiyor; bunlar için mevcut dosyadaki diğer
    P/Invoke'ların aksine klasik `DllImport` kullanıldı (`Win32.cs`'te not edildi) — build'i 0 uyarıda
    tutmaya devam etti (SYSLIB1054 önerisi burada tetiklenmedi).
  - **STA thread yerine Task.Run+Semaphore**: Görev metninin orijinal §2'si "tek adanmış STA thread"
    istiyordu; §7.3 revizyonu bunu override edip `Task.Run` + `SemaphoreSlim(2)`'ye çevirdi — uygulandığı
    gibi bu. `SHGetFileInfo`/`SHDefExtractIconW` genelde MTA thread pool thread'lerinden de çalışıyor;
    canlı testte hiçbir ikon çıkarma hatası gözlenmedi, ancak nadir bir shell namespace uzantısının COM
    STA gerektirebileceği teorik bir risk olarak not düşülüyor (spec'in kendi revizyonu tarafından kabul
    edilmiş bir risk).
  - **Editördeki salt-okunur "Icon" satırı** (spec §7.13): sadece glyph/emoji + kısa açıklama gösteriyor,
    özel `file`/`exe` ikonları için gerçek bitmap'i göstermiyor — bunun için `IconService`'in
    `EditorViewModel`'e bağlanması gerekirdi ve editörün başka hiçbir yeri buna ihtiyaç duymuyor; ayrıca
    launcher-spec.md §9 adım 4'ün tarif ettiği tam inline ikon değiştirme arayüzü zaten Aşama 3'te Ctrl+I'a
    ertelenmişti. Satır ayrıca Advanced bölümünün *dışına* alındı (spec metni "Advanced section" diyordu)
    çünkü Advanced sadece App/Command tiplerinde var, ama ikon her tipte var — Folder/Path/URL'de de
    görünmesi daha tutarlı.
  - **Test otomasyonunda `SendKeys` Ctrl+harf sorunu (üründe değil, doğrulama script'inde)**: PowerShell
    `System.Windows.Forms.SendKeys` ile `^i`/`^n` gönderimi bu paylaşımlı masaüstü oturumunda güvenilmez
    çıktı (Ctrl basılı değilmiş gibi düz harf olarak "i"/"n" yazıldı, hatta bir seferinde odak Chrome'a
    kayıp `Ctrl+N` yeni sekme açtı) — gerçek uygulama kodunda bir sorun değil; doğrulama script'i ham
    `keybd_event` (VK_CONTROL + harf, ayrı basılı/bırakılmış olaylarla) kullanacak şekilde değiştirildi ve
    güvenilir çalıştı. Bu bulgu ileride benzer otomasyon script'leri için not edildi.
  - Uygulanmadı (bilerek, plana uygun): `IconService.Invalidate()` var ama hiçbir yerden çağrılmıyor —
    kod içinde `// Phase 5` notu var (config dosya izleyicisi geldiğinde oradan çağrılacak);
    `icons\` klasöründeki öksüz dosyalar (bir node özel dosya ikonundan başka bir şeye geçtiğinde) temizlenmiyor
    (README'de belirtildi); Ayarlar ekranından "Change icon…" butonu gerekli değildi (spec zaten belirtmiyor).
- Bilerek bırakılanlar (plana uygun): tray/tek-instance/dosya izleme/DPI manifest/Mica (Aşama 5),
  sürükle-bırak/`.lnk`/ayarlar sayfası/import-export/kullanım istatistiği/tema (Aşama 6).

## Devam noktası (ara verildi 2026-09-26)
- Aşama 1–4 tamam, 172 test yeşil; `main` → https://github.com/xmsadik/your-launcher (private), commit `99fb3ca` (Aşama 1-3; Aşama 4 henüz commit edilmedi — orkestratör commit etmeyecek şekilde talimat verdi).
- Sıradaki: **Aşama 5 — Sistem** (tray, tek instance, Windows ile başlat, çoklu monitör/DPI manifest, dosya izleme — bu geldiğinde `IconService.Invalidate()`'i çağıracak yer burası). Sonra 6 (cila; ayarlarda "Start with Windows" anahtarı dahil).
- Açık konular: çok kelimeli aramada alanlar arası eşleşme yok; Debug build ~147 MB (bellek hedefi Release'te Aşama 5'te ölçülecek); tray gelene kadar çıkış `Ctrl+Q`; `icons\` klasöründe öksüz dosya temizliği yok.

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

### Aşama 5 — Sistem (tamamlandı 2026-09-26)
- [x] Tek instance: named mutex + named pipe "show" mesajı
- [x] Tray: Göster, Ayarlar, Config dosyasını aç, Config klasörünü aç, Yeniden yükle, Çıkış; gizli komut hata kodu bildirimi
- [x] Windows ile başlat (HKCU Run, değer adı "Your Launcher", exe yolu tırnaklı): `settings.startWithWindows` ile senkron; açılışta registry durumu ayarla eşitlenir (exe taşındıysa yol güncellenir)
- [x] Per-monitor DPI v2 manifest; açılışta imlecin monitörü, üst üçte bir konum
- [x] FileSystemWatcher (debounce ~200 ms, kilitli dosyaya retry) → yeniden yükle (AC10); bozuksa uyarı + "yedekten yükle" (AC11)
- [x] Hotkey başarısızsa tray uyarısı + ayarları aç
- [x] Mica/Acrylic + köşe yuvarlama (Win11), fallback düz renk

### Aşama 6 — Cila

**Aşama 6A (Part A, tamamlandı 2026-09-26):**
- [x] ThemeService: sistem teması (registry `AppsUseLightTheme` + panelin mevcut `HwndSource` hook'unda `WM_SETTINGCHANGE`/`ImmersiveColorSet`, `SystemEvents` değil — §10 madde 4), açık/koyu ResourceDictionary (`Themes/Dark.xaml`, `Themes/Light.xaml`), tüm sabit renkler `DynamicResource`'a taşındı
- [x] Ayarlar sayfası §11 (hotkey kaydı tuşa basarak, tema, **"Start with Windows" açma/kapama anahtarı** (kullanıcı talebi 2026-09-26; değişince registry anında güncellenir), closeAfterLaunch, maxVisibleItems, defaultShell, son konumu hatırla, ipucu satırı)
- [x] Import/Export (birleştir: id çakışmasında yeni id / değiştir)
- [x] UsageService (`usage.json`, debounce'lu kayıt) → arama eşitlik bozucusu

**Aşama 6B (Part B, tamamlandı 2026-09-27 — canlı fare testi kullanıcının son manuel testine kaldı):**
- [x] Fare önkoşulu: `ListBoxItem.Focusable=False`, tıklama = seçim, çift tıklama = aç (§10 madde 5)
- [x] Drag&drop Explorer'dan: `.exe/.lnk` → app, diğerleri → path; `.lnk` çözümleme (IShellLinkW: hedef, argüman, çalışma dizini, ikon) — AC12
- [x] Liste içi sürükle: sıralama + klasöre bırak
- [x] Sağ tık menüsü (Aç, Düzenle, İkon Değiştir, Kes, Buraya Yapıştır, Çoğalt, Sil, Dosya konumunu aç)

### Teslimat
- [x] `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishReadyToRun=true`
- [x] README: kurulum, kısayol tablosu, config formatı, kararlar (D1–D7)
- [x] `config.example.json` (iç içe klasörler, app, path, komut örnekleri)

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

### Aşama 5 — Sistem (tamamlandı 2026-09-26)

Bu aşamanın kodu önceki bir oturumda tam olarak yazılmıştı ama doğrulama yarım kalmıştı; bu oturum sadece
doğrulama, iki gerçek hatanın düzeltilmesi ve dokümantasyon içindi — kod yeniden yazılmadı.

- Build: `dotnet build` (Debug **ve** Release) — 0 uyarı, 0 hata. Test: `dotnet test` — **180/180 yeşil**
  (Aşama 1–4'ün 172'si + Aşama 5 için `StartupSyncTests` 8 yeni: Core'un saf registry write/delete/none
  kararı — enabled+eksik→Write, enabled+aynı yol (tırnaklı/tırnaksız/büyük-küçük harf farklı)→None,
  enabled+farklı yol→Write, disabled+mevcut→Delete, disabled+eksik→None, `Quote`).
- Canlı doğrulama, `YOURLAUNCHER_CONFIG_DIR` → `%TEMP%\YLPhase5VerifyCfg` (gerçek `%APPDATA%\Your Launcher`
  ve gerçek `HKCU\...\Run\Your Launcher` değerine hiç dokunulmadı — ikisi de önce kontrol edilip boş
  olduğu doğrulandı):
  1. **Tek instance + tray + dış düzenlemeden yeniden yükleme + bozuk JSON** zaten önceki oturumda canlı
     doğrulanmıştı (ekran görüntüleri mevcut) — bu oturumda tekrarlanmadı.
  2. **Ctrl+Shift+R kurtarma (iki yol da)**: (a) Uygulama açıkken config.json dışarıdan bozuldu →
     panelde "config.json has errors — kept the previous version. Ctrl+Shift+R: keep current version"
     anında görüldü (panel gerçekten ön plandayken ekran görüntüsü alındı, `GetForegroundWindow`ile
     doğrulandı) → Ctrl+Shift+R → `config.corrupt-<ts>.json` doğru içerikle arşivlendi, config.json
     bellekteki ağaçla (MyFolder dahil) geri yazıldı, hata satırı temizlendi. (b) Uygulama **açılışta**
     zaten bozuk bir config.json ile başlatıldı → panel bu kez **açılış anında** "Config could not be
     read: ... Ctrl+Shift+R: restore backup" gösterdi (aşağıdaki Hata 2'ye bakın) → Ctrl+Shift+R →
     corrupt dosya arşivlendi, config.json `config.backup.json`'dan birebir geri yüklendi (`diff` ile
     doğrulandı), panel boş root'a döndü, hata satırı temiz.
  3. **Uygulama içinden kayıt → gereksiz yeniden yükleme yok**: Root'ta Ctrl+Shift+N ile "MyFolder"
     eklendi, içine girildi (breadcrumb "Root › MyFolder"), tekrar Ctrl+Shift+N ile "SubItem" eklendi —
     her iki kayıttan sonra da panel **aynı klasörde** kaldı (breadcrumb resetlenmedi, hata satırı
     çıkmadı). Kod incelemesiyle de doğrulandı: `ConfigService.Save()` `LastWrittenHash`'i
     `ConfigStore.Save()`'in diske yazdığı **aynı** `ConfigSerializer.Serialize` çıktısından hesaplıyor,
     bu yüzden `ConfigWatcherService`'in kendi-yazımı algılaması bit-bit eşleşiyor (zamanlama tahminine
     gerek yok).
  4. **Gizli komut çıkış kodu → tray balonu**: `command` node'u `command: "exit 3"`, `shell: "pwsh"`,
     `window: "hidden"` ile eklendi, Enter ile çalıştırıldı. Hem `Debug.WriteLine` izinin (geçici olarak
     aynı satıra bir dosyaya da yazdırıldı, doğrulama bitince kaldırıldı) `Shell_NotifyIcon NIM_MODIFY
     ok=True` döndürdüğü, hem de gerçek bir Windows bildirimi ("Your Launcher — 'ExitTest' exited with
     code 3.") ekran görüntüsüyle doğrulandı.
  5. **StartupService gerçek registry karşı testi**: Release derlemesindeki `YourLauncher.dll`
     reflection ile yüklenip atma değer adı **"Your Launcher TEST"** ile `StartupService` örneklendi (bu
     ad Debug derlemesinde `#if DEBUG` her zaman atlıyor, bu yüzden Release kullanıldı) — sync(true) eksik
     değeri yazdı, tekrar sync(true) değişiklik yapmadı (None), değeri elle eski bir yola değiştirip
     sync(true) doğru yolla üzerine yazdı (exe taşınması senaryosu), sync(false) değeri sildi. Test sonunda
     değer silindi; gerçek "Your Launcher" değeri baştan sona hiç var olmadı (kontrol edildi).
  6. **Publish + bellek**: `dotnet publish -c Release -r win-x64 --self-contained
     -p:PublishSingleFile=true -p:PublishReadyToRun=true` başarılı; scratch config dizini ile çalıştırıldı,
     bir kez gösterilip gizlendi, 5 sn (ve ek 5 sn daha, kararlılık için) beklendi:
     **Working Set ≈ 148 MB, Private Bytes ≈ 85 MB** (hedef: Working Set < 80 MB). **Hedef tutmadı** —
     spec'in kendi riski zaten bunu işaret ediyordu (§0: "WPF boşta bellek ~50–70 MB; hedef sınırda");
     `SetProcessWorkingSetSize` gibi bir hack bilerek uygulanmadı (spec §8, §10 madde 14 açıkça
     yasaklıyor), sadece Release'te `ConcurrentGarbageCollection=false`/`TieredPGO=false`/
     `UseSystemResourceKeys=true`/`SatelliteResourceLanguages=en` var. Self-contained WPF'nin kendi
     CLR+renderer'ı taşıması bu tabanı zaten 80 MB'ın üzerine çıkarıyor; daha fazla küçültme (trimming,
     framework-dependent yayın, WPF dışı bir UI) Aşama 5'in kapsamı dışında — README'de not edildi.
  7. **DPI/çoklu monitör**: bu makinede tek monitör olduğu için `PositionOnCursorMonitor`/`OnDpiChanged`
     yalnızca **kod incelemesiyle** doğrulandı, canlı test edilmedi (README'de açıkça belirtildi).

- **Bulunan ve düzeltilen 2 gerçek hata** (ikisi de bu doğrulama sırasında canlı testte ortaya çıktı,
  önceki oturumun ekran görüntülerinde görünmüyordu çünkü ilgili senaryolar hiç tetiklenmemişti):
  1. **`ConfigService.KeepCurrentVersion()` yedeği bozuyordu** (`src/Launcher.App/Services/
     ConfigService.cs`, `KeepCurrentVersion()`): Kurtarma "corrupt dosyayı arşivle, sonra bellekteki
     ağacı kaydet" sırasıyla çalışıyordu, ama `Save()`'in çağırdığı `ConfigStore.Save()` atomik
     `File.Replace(temp, configPath, backupPath)` kullanıyor — bu API, `configPath`'teki **o anki**
     içeriği `backupPath`'e taşıyor. `Save()` çağrıldığı anda `configPath` hâlâ az önce arşivlenen bozuk
     JSON'du, yani her "Keep my current version" kurtarması `config.backup.json`'ı geçersiz JSON ile
     eziyordu — bir sonraki gerçek ihtiyaçta "Restore from backup" bozuk bir dosya geri yüklerdi. Canlı
     testte doğrudan yakalandı (`config.backup.json` 38 byte'lık çöp metin oldu). Düzeltme:
     `ArchiveCorruptFile()`'dan sonra, `Save()`'den önce `configPath`'i sil — böylece `ConfigStore.Save()`
     `File.Move` dalına düşüyor (yedeklenecek bir "eski" dosya yok), gerçek yedek dokunulmadan kalıyor.
     Silme başarısız olursa (best-effort, aynı try/catch stiliyle) davranış öncekiyle aynı kalır, kurtarma
     yine de başarılı olur.
  2. **Açılışta bozuk config mesajı hiç görünmüyordu** (`src/Launcher.App/ViewModels/MainViewModel.cs`,
     `ClearErrorMessage()`): Constructor, `configService.LoadError` doluysa doğru mesajı ayarlıyordu, ama
     `App.OnStartup`'ın en sonunda çağrılan `MainWindow.ShowLauncher()` her zaman
     `_viewModel.ClearErrorMessage()`'ı **koşulsuz** çağırıyor — bu da mesajı panel hiç gösterilmeden
     sildi. Sonuç: açılışta config.json zaten bozuksa kullanıcı boş bir root görüyordu, hiçbir ipucu
     olmadan (sadece tray menüsünü açar veya bir düzenleme dener ise fark ederdi). `SetCorruptErrorMessage`
     zaten var olan bir yardımcıydı (reload-corrupt ve BlockIfReadOnly aynı deseni kullanıyor); düzeltme
     `ClearErrorMessage()`'ı `_configService.IsReadOnly` iken mesajı boşaltmak yerine
     `SetCorruptErrorMessage(_configService.LoadError!)` ile yeniden kurup geri koyacak şekilde değiştirdi
     — böylece her show/hide döngüsünde ve her tuş vuruşunda (ki `ClearErrorMessage` ikisinde de
     çağrılıyor) kalıcı bir salt-okunur durum artık silinmiyor, sadece geçici uyarılar (launch hatası,
     "target not found" vb.) siliniyor. Canlı testle önce-sonra karşılaştırmalı doğrulandı (düzeltmeden
     önce panel boş açılıyordu, düzeltmeden sonra hata satırı açılış anında görünüyor).
  - Her iki düzeltme sonrası `dotnet build`/`dotnet test` tekrar 0 uyarı/180 yeşil kaldı.
- **D4 kararı değişti** (README'de detaylı gerekçe var): planlanan WinForms `NotifyIcon` yerine elle
  yazılmış `Shell_NotifyIcon` interop'u (`TrayService.cs`) kullanıldı — bellek (WPF yanında WinForms
  runtime parçalarını da taşımamak), koyu temalı context menü (WPF `ContextMenu` + özel `ControlTemplate`,
  WinForms `ContextMenuStrip`'in kendi renderer'ına göre daha temiz), ve zaten plan aşamasında risk olarak
  işaretlenen `UseWindowsForms`+`UseWPF` tip çakışmasını (Application, KeyEventArgs, MessageBox, ...) baştan
  hiç yaşamamak.
- Sapmalar/netleştirmeler: Yok — kod zaten spec'in §10 revizyonlarını (foreground/Alt-key kurtarma, reload
  in-place, watcher debounce/hash, corrupt semantiği, deferred reload, Shell_NotifyIcon, app.ico, hidden
  exit event, startup registry gate'leri, DPI, Mica/Acrylic, tek instance detayları, hotkey, bellek
  ayarları, doğrulama notları) satır satır uyguluyordu; bu oturum sadece yukarıdaki 2 hatayı buldu ve
  düzeltti.
- Bilerek bırakılanlar (plana uygun): sürükle-bırak/`.lnk`/ayarlar sayfası/import-export/kullanım
  istatistiği/tema/sağ tık menüsü (Aşama 6). "Settings…" tray öğesi ve hotkey-hatası balonunun tıklaması
  şimdilik config.json'ı varsayılan düzenleyicide açıyor (`// Phase 6` notu).

### Aşama 6A — Cila, Part A: tema/ayarlar/import-export/kullanım istatistiği (tamamlandı 2026-09-26)

`tasks/phase6-spec.md` §1–§4 ve §9 (Part A doğrulaması), §10 revizyonları esas alınarak, §10'da belirtilen
sıra ile uygulandı: **tema → ayarlar sayfası → import/export → kullanım istatistiği**. Part B (§5–§8: sağ
tık menüsü, Explorer'dan sürükle-bırak, liste içi sürükle-sıralama, ve fare önkoşulu) orkestratör talimatı
gereği bu oturumda **bilerek yapılmadı**.

- Build: `dotnet build` (Debug **ve** Release) — 0 uyarı, 0 hata. Test: `dotnet test` — **206/206 yeşil**
  (Aşama 1–5'in 180'i + Aşama 6A için 26 yeni: `UsageScorerTests` 17 — kayıt/skor/yaş-kovası (Theory, 6
  vaka)/belirlenimlilik/`Prune`/bozuk-boş dosya/round-trip —, `SearchEngineTests`'e 1 yeni eşitlik-bozucu
  testi, `ConfigImportTests` 8 — çakışmasız ekleme, üst/iç içe id çakışması yeniden adlandırma, iki çakışan
  id'nin farklı yeni id alması, sıra korunumu, kaynakla takma ad paylaşmama, Replace ayarları koruyor,
  `CountDescendants`).
- Canlı doğrulama, her seferinde `YOURLAUNCHER_CONFIG_DIR` → `%TEMP%` altında ayrı bir scratch klasörü
  (gerçek `%APPDATA%\Your Launcher\` ve gerçek `HKCU\...\Run\Your Launcher` değerine hiç dokunulmadı;
  Windows'un kendi tema ayarı da hiç değiştirilmedi):
  1. **Tema (koyu/açık dönüşüm, piksel karşılaştırması)**: `git stash -u` ile Aşama 6A öncesi temiz build
     alınıp liste/editör/ikon seçici sayfalarının koyu temada "önce" ekran görüntüleri çekildi, `git stash
     pop` ile Aşama 6A geri getirilip aynı sayfaların "sonra" görüntüleri çekildi — üçü de piksel bazında
     özdeş görünüyor (DynamicResource dönüşümü mevcut koyu görünümü bozmamış). Açık temada da panel
     acrylic üzerinde okunaklı.
  2. **Ayarlar sayfası (`Ctrl+,`)**: açılış/düzenleme/kaydetme hem koyu hem açık temada ekran görüntüsüyle
     doğrulandı; hotkey kutusuna odaklanınca canlı global hotkey devre dışı kalıyor (aksi halde
     `WM_HOTKEY` tuş kombinasyonunu yutuyor), yeni kombinasyon yakalanıp kaydedildikten sonra panel eski
     kısayol artık açmıyor, yeni kısayol açıyor. Visible rows alanı canlı uygulanıyor: `Ctrl+A` ile
     seçilip "3" yazılınca liste anında 3 satıra küçüldü (kaydetmeden önce, editördeki gibi).
  3. **Salt-okunur (bozuk config) davranışı**: bozuk `config.json` ile başlatılan bir örnekte Ayarlar
     sayfası yine de açılıyor (§1'in "page opens but Save is refused" kuralı), ama `Ctrl+S` kaydetmeyi
     reddediyor — `config.json`'ın SHA256'sı başlangıçtan sonuna birebir aynı kaldı (dosyaya hiç
     dokunulmadı).
  4. **Import/Export round-trip**: Export… ile geçerli config bir `.json`'a yazıldı (`SaveFileDialog`,
     varsayılan ad deseni doğru); aynı dosya Import… ile geri okunup **Merge** seçildi — id çakışan
     düğümler yeniden adlandırılarak eklendi, mevcut ağaç korundu, durum satırı "Imported N items
     (merged)" gösterdi. Ayrı bir çalıştırmada aynı dosya **Replace** ile içe aktarıldı — ağaç tamamen
     değişti, mevcut `settings` (hotkey, tema, vb.) **değişmeden kaldı** (spec §4'ün açık kuralı),
     `config.json`'ın önceki hâli normal atomik kaydetme yoluyla `config.backup.json`'a gitti.
  5. **Kullanım istatistiği ve arama eşitlik bozucusu**: bir örnekte bir öğe başlatıldı (`usage.json`
     debounce'lu ve atomik yazıldı, id bazlı `{ useCount, lastUsedUtc }`); uygulama kapatılıp
     **aynı config dizini** ile taze bir örnek açıldı, aynı adı taşıyan iki eşit-skorlu sonucu döndüren bir
     arama yapıldı — daha önce kullanılan öğe üstte çıktı (frecency eşitlik bozucusu doğru çalışıyor;
     odak-çalma nedeniyle ilk denemedeki bir SendKeys yan etkisi ikinci, taze bir çalıştırmayla
     çözüldü — ayrıntı aşağıda "Sapmalar"da).
  6. **"Son konumu hatırla" (§1.2, bu oturumda ek olarak bulunup düzeltilen gerçek hata — bkz. aşağı)**:
     `rememberLastLocation: true` iken Root › Dev › Tools'a girilip panel gerçekten gizlenince (pencere
     deactivate olunca, `MainWindow_OnDeactivated` → `HideLauncher()`), ikinci bir örnek başlatılıp (named
     pipe "show" mesajı) panel yeniden gösterildiğinde **doğrudan Root › Dev › Tools'a** döndüğü ekran
     görüntüsüyle doğrulandı (ilk denemede Esc'in nav-modda önce bir üst klasöre çıkıp ancak kökteyken
     gizlediği fark edilmeden test edilmiş, bu yüzden ilk koşu yanlış pozitif "hata" gibi görünmüştü — asıl
     hata aşağıda anlatılıyor ve gerçek).
- **Bulunan ve düzeltilen 1 gerçek hata**: `MainViewModel.RememberCurrentLocation()` (spec §1.2, doc
  yorumunda zaten "Called by MainWindow.HideLauncher()" yazıyordu) **hiçbir yerden çağrılmıyordu** —
  `MainWindow.HideLauncher()` sadece `ClearCutState()` ve `FlushPendingReloadIfAny()` çağırıyordu, bu da
  "son konumu hatırla" ayarını fiilen tamamen işlevsiz bırakıyordu (her zaman köke dönerdi). Düzeltme:
  `src/Launcher.App/Views/MainWindow.xaml.cs`'de `HideLauncher()`'ın en başına
  `_viewModel.RememberCurrentLocation();` eklendi; ayrıca `ShowLauncher()`'ın artık yanlış olan "always
  resets to root" doc yorumu güncellendi. Yukarıdaki madde 6'da canlı doğrulandı; düzeltmeden sonra
  `dotnet build`/`dotnet test` tekrar 0 uyarı/206 yeşil kaldı.
- Sapmalar/netleştirmeler:
  - Ekran görüntüsü karşılaştırması `git stash`/`git stash pop` ile yapıldı (commit atılmadı,
    orkestratörün "commit etme" talimatına uyuldu) — spec'in istediği "before/after dark screenshot diff"
    hedefine ulaşmanın en basit yolu.
  - Global hotkey ile panel yeniden gösterme testi bu geliştirme ortamında güvenilir değildi (bu makinedeki
    başka bir yazılımın düşük seviyeli klavye kancası `Alt+Space`'i YourLauncher'a ulaşmadan yutuyor gibi
    görünüyor); tüm "yeniden göster" doğrulamaları bunun yerine tek-instance named pipe mekanizmasıyla
    yapıldı (ikinci bir `YourLauncher.exe` başlatmak ilkine "show" sinyali gönderip hemen çıkıyor) — gerçek
    kullanıcı deneyimini (Ctrl+, dahil tüm klavye kısayolları için ayrı ayrı) etkilemez, sadece bu oturumun
    otomasyon script'lerinin fiziksel Alt+Space tuş simülasyonuna güvenmediği anlamına gelir.
  - Kullanım istatistiği testinde bir alt-süreç (launch edilen Notepad) ön plan odağını çaldığı için
    `SendKeys` bir sonraki arama metnini yanlış pencereye yazdı; bu koşu tekrarlanmadı, bunun yerine
    aynı config dizini ile taze bir örnek açılarak kalıcı `usage.json` verisiyle doğru sıralama ayrı bir
    çalıştırmada doğrulandı — üretim kodunda bir hata değil, tamamen script/otomasyon kısıtı.
  - Visible rows canlı-uygulama testinde imleç seçili metnin başına değil TextBox'ın mevcut içeriğinin
    (`8`) üzerine `Ctrl+A` ile tam seçim yapılarak yazıldı; ilk deneme (Backspace + yazma) beklenmedik
    şekilde "38"e klemlendi (kaydetme anındaki 3–20 klemplemenin de doğru çalıştığını yan ürün olarak
    gösterdi) ama asıl "canlı satır sayısı küçülüyor mu" sorusunu net cevaplamadığı için tekrarlandı.
- Bilerek bırakılanlar (plana uygun, orkestratör talimatıyla): Part B'nin tamamı — fare önkoşulu, sağ tık
  menüsü, Explorer'dan sürükle-bırak (`.lnk`/`.url` çözümleme dahil), liste içi sürükle-sıralama. Kod bu
  parçaların üzerine oturacağı `PanelPage`/tema/ayarlar altyapısını yeniden şekillendirmeden ekleyecek
  şekilde yapılandırıldı (README'de belirtildi).

### Aşama 6B — Cila, Part B: fare, sağ tık, sürükle-bırak, teslimat (tamamlandı 2026-09-27)
- Kod bir Sonnet ajanı tarafından yazıldı (canlı ekran otomasyonu sırasında kullanıcı durdurdu); bu oturumda
  **ekrana dokunmadan** tamamlandı: orkestratör incelemesi + bağımsız adversarial inceleme (Fable) + düzeltmeler,
  build/test, README/publish (Sonnet).
- İçerik: `ListBoxItem.Focusable=False` + tıkla-seç/çift tıkla-aç; tema uyumlu sağ tık menüsü
  (`Services/ThemedMenuFactory.cs`, tepsi menüsüyle ortak; öğe menüsü + boş alan menüsü, hepsi klavye
  kısayollarının aynı VM metodlarını çağırır); Explorer'dan sürükle-bırak (`Deactivated` anında sol tuş basılıysa
  gizlemeyi erteleyen 50 ms `GetAsyncKeyState` yoklaması, `Core/Config/DropMapper.cs`, `Interop/ShellLink.cs`
  classic `[ComImport]`, `SLGP_RAWPATH`, `Resolve` yok); liste içi sürükleme (`DropIndicator` + `TreeOps.MoveToGroupIndex`);
  "Open file location" için `TargetCheck.ResolveExistingPath`.
- İncelemede bulunan ve düzeltilen hatalar:
  1. Liste içi sürükleme de `_externalDragOverPanel`'i true yapıyordu, iç drop onu hiç sıfırlamıyordu → sonraki
     "başka pencereye tıkla" paneli gizlemiyordu. Artık sadece `FileDrop` için set ediliyor.
  2. Bozuk `.lnk` → `IPersistFile.Load` `COMException` → drop handler'dan istisna. Artık boş `ShellLinkInfo`
     dönüyor, `.lnk`'in kendisi app olarak ekleniyor.
  3. Klasör/öğe grupları arası bırakma sessizce hiçbir şey yapmıyordu → spec'teki "clamp": öğe → öğe grubunun
     başı, klasör → klasör grubunun sonu.
  4. (Fable) Çift tıklamayla klasöre girildikten sonra buton basılıyken sürükleme, eski adayla yeni klasördeki
     satırlara göre yanlış klasörü taşıyıp kaydedebiliyordu → çift tıkta aday temizleniyor, hareket eden satır
     aday değilse sürükleme başlamıyor, `MoveDraggedNode` kaynak/hedef aynı ebeveynde değilse reddediyor.
  5. (Fable) Reddedilen dış drop'ta (başka sayfa/salt okunur) buton bırakıldığında yoklama, gecikmeli gelen
     `DragLeave` ile yarışıp paneli pasif Topmost bırakabiliyordu → buton bırakıldıktan sonra `Drop`/`DragLeave`
     bayrağı temizleyene kadar (en fazla 2 sn) beklemeye devam ediyor.
  6. (Fable) Drop, satırdaki (alt eleman geçişlerinde `DragLeave` ile sıfırlanabilen) göstergeyi okuyordu → drop
     noktasından yeniden hesaplanıyor.
  7. (Fable) Salt okunurda dış drop hata satırı göstermiyordu (drop hiç gelmiyor) → `DragEnter`'da gösteriliyor.
  8. (Fable) ConfirmDelete/ConfirmImport sayfalarında satırlara tıklama/sağ tık seçimi değiştirip menü açıyordu →
     fare handler'ları sadece List sayfasında çalışıyor.
- Teslimat (§8): `config.example.json`'a eksik `powershell` shell komutu eklendi (mevcut smoke testi parse
  ediyor); README: Install bölümü, fare tablosu, sürükle-bırak eşlemesi, bilinen kısıtlar listesi.
  Publish başarılı: tek exe **~134 MB**; boşta bellek **~176 MB Working Set / ~104 MB Private Bytes** (panel
  açıkken ölçüldü — Aşama 5'in ~148/~85 MB ölçümü panel gizliyken yapılmıştı, README'de belirtildi).
- Doğrulama: `dotnet build` Debug+Release 0 uyarı; 242/242 test yeşil. `Search_5000Nodes_MedianUnder16Milliseconds`
  bir koşuda yük altında zamanlama nedeniyle düştü, tekrarlarda geçti (bilinen dalgalı test, değişiklikle ilgisiz).
- **Canlı doğrulanmadı** (kullanıcı tercihi: fare/klavye otomasyonu yok): sağ tık menüsünün görünümü/eylemleri,
  Explorer'dan gerçek sürükle-bırak (`.lnk`/`.url`/`.exe`/klasör/txt), liste içi sıralama/klasöre taşıma,
  drop sonrası panelin önde kalması. Bunlar kullanıcının son manuel testine kaldı.

## Devam noktası (güncellendi 2026-09-27, tüm aşamalar + ek özellikler push edildi)
- Aşama 1–5 commit+push: `94454b6`. Aşama 6A **yerel commit** `0597745` (push edilmedi).
- Aşama 6B `7925d86`; sonrasında: Esc paneli kapatır (`ebedc11`), öğe başına "Ask before launching" onayı (`d3155aa`), yer imi içe aktarma (bu commit).
- 2026-09-27 ek işler (hepsi push edildi): Chrome `AccountBookmarks` desteği + aynı adlı profilleri ayırma (`719fbde`),
  liste satırında isim önceliği + yolun baştan kısaltılması (`78157ad`, `Controls/NameAndPathPanel.cs`,
  `Controls/PathText.cs`, `Core/Search/PathTrimmer.cs`). 305 test yeşil. Exe (Release publish klasöründeki `YourLauncher.exe`) son koddan publish edildi.
- Kullanıcı 2026-09-27'de manuel teste başladı ve ara verdi. Sıradaki: kullanıcının test geri bildirimlerini düzeltmek
  (özellikle sağ tık, sürükle-bırak, gerçek Alt+Space hotkey).
- Açık konular: çok kelimeli aramada alanlar arası eşleşme yok; boşta bellek 80 MB hedefinin üzerinde (bilerek
  hack'lenmedi); `icons\` öksüz dosya temizliği yok; DPI/çoklu monitör sadece kod incelemesiyle doğrulandı;
  MSI "advertised" kısayollarda `GetPath` bazen `C:\Windows\Installer\…` ikon yolu dönebilir (MSI API'si
  kullanılmadı) — manuel testte Start menüsünden birkaç kısayol denenmeli.

### Ek özellik: yer imi içe aktarma (tamamlandı 2026-09-27)

`tasks/bookmarks-spec.md` esas alındı — tek seferlik yer imi içe aktarma (Chromium ailesi otomatik keşif +
herhangi bir tarayıcının HTML dışa aktarma dosyası), aynı kaynağın yeniden içe aktarılması önceki içe
aktarmanın yerine geçiyor (Merge/Replace sorusu yok, doğrudan uygulanıyor).

- **Ne eklendi**:
  - `src/Launcher.Core/Bookmarks/`: `ChromiumBookmarkLocator` (Chrome/Edge/Brave/Vivaldi/Chromium
    `%LOCALAPPDATA%\...\User Data\{Default,Profile *}`, Opera/Opera GX `%APPDATA%\Opera Software\...`,
    `Local State`'ten profil adı, eksik/bozuk dizin/JSON'da sessizce atlama), `ChromiumBookmarkParser`
    (ham JSON, `JsonDocument`, sabit kök adları "Bookmarks bar"/"Other bookmarks"/"Mobile bookmarks"),
    `NetscapeBookmarkParser` (HTML dışa aktarma — Firefox/Chrome/Edge/Safari ortak formatı, tolere edici
    regex tokenizer, `<H3>`+`<DL>`=klasör/`<A HREF>`=yer imi/`</DL>`=kapanış, HTML entity decode),
    `BookmarkImport` (`Apply`: aynı `sourceKey`'e ait önceki içe aktarmayı ağaçta her nerede olursa olsun
    bulup sadece içeriğini değiştirir — ad/ikon/konum korunur; yoksa hedef klasöre yeni klasör olarak
    ekler), `BookmarkImportException`, `BookmarkTreeHelpers` (boş klasör budama, javascript:/boş
    başlık/boş klasör adı kuralları — her iki ayrıştırıcı da paylaşıyor).
  - App: `SettingsViewModel`'e `DiscoverBookmarkSources`/`RequestImportBookmarksFromSource`/
    `RequestImportBookmarksFromHtml` (dosya okumaları `FileShare.ReadWrite|Delete` ile — tarayıcı dosyayı
    açık tutabilir) + `BookmarksParsed` event'i; `SettingsView.xaml`'a Export…/Import…'ün yanına üçüncü
    buton **"Import bookmarks…"** (aynı `CanExportImport` kuralı); tıklanınca `ThemedMenuFactory` ile
    temaya uygun, butonun altına açılan bir `ContextMenu` (bulunan her kaynak + ayraç + "From HTML
    file…"; hiç kaynak yoksa sadece HTML seçeneği). `MainViewModel.OnBookmarksParsed`: config-import'un
    Merge/Replace sorusunun aksine **hemen uygular** — Ayarlar açılırken gösterilen klasöre ekler ya da
    (yeniden içe aktarmaysa) önceki içe aktarmanın şu an gerçekte bulunduğu klasöre gidip onu seçili
    gösterir; `BlockIfReadOnly()` ile salt-okunur koruması; `_pruneUsage()` çağrısı (replace eski id'leri
    düşürebilir).
  - README: yeni "Bookmark import" bölümü (kaynaklar, Firefox HTML dışa aktarma ipucu, değiştirme
    semantiği, javascript: bookmarklet'lerin atlanması, boş klasörlerin düşürülmesi, tek seferlik/senkron
    yok) + Ayarlar sayfası açıklamasına buton eklendi.
- **Testler**: `dotnet test` — **289/289 yeşil** (var olan 243'ün hepsi + `Bookmarks/` altında 46 yeni:
  `ChromiumBookmarkLocatorTests` 10 — tek/çoklu profil, `Local State`'ten ad okuma, bozuk/eksik `Local
  State` sessizce dizin adına düşme, `Bookmarks` dosyası olmayan profilin atlanması, Opera/Opera GX tek
  profil, birden çok tarayıcı birlikte; `ChromiumBookmarkParserTests` 14 — basit/iç içe yapı, sıralama,
  üç kök, javascript: atlama, boş başlık→host, boş klasör adı→"(unnamed)", iç içe boş klasörün özyinelemeli
  budanması, hedefsiz/bilinmeyen node atlama, bozuk JSON/eksik "roots"→`BookmarkImportException`, her
  node'a taze id; `NetscapeBookmarkParserTests` 13 — üst düzey/iç içe yapı, sıra korunumu, `<H1>` yok
  sayma, büyük/küçük harf duyarsız etiketler, HTML entity decode, javascript:/href'siz atlama, boş
  başlık/boş klasör adı, iç içe boş klasör budaması → sonuçta hiç yer imi kalmayınca istisna, kapatılmamış
  `<DT>`/`<p>` toleransı; `BookmarkImportTests` 9 — ilk içe aktarma (hedef klasöre ekleme, kök olması
  şart değil), yeniden içe aktarma (yerinde değiştirme — ad/id/konum korunur), taşınmış klasörü bulma,
  farklı `sourceKey`'in eşleşmemesi, sayımın sadece `UrlNode` sayması, deterministik id/HTML anahtarı.
- **Build**: `dotnet build` Debug **ve** Release — **0 uyarı, 0 hata**.
- **Canlı doğrulama (salt-okunur, sadece okuma — spec §4 kısıtları)**: Uygulama hiç başlatılmadı, fare/
  klavye otomasyonu yapılmadı, ekran görüntüsü alınmadı, `%APPDATA%\Your Launcher\` hiç dokunulmadı.
  Bunun yerine scratch dizininde ayrı bir konsol projesi (`Launcher.Core`'a `ProjectReference`) ile gerçek
  makinedeki dosyalar salt-okunur okunup ayrıştırıldı:
  - `ChromiumBookmarkLocator.Discover` gerçek `%LOCALAPPDATA%`/`%APPDATA%` ile çalıştırıldı: **Chrome**
    (`Default`, 1 profil) ve **Edge** (`Default` + `Profile 1`, ikisi de "Person 1" adıyla — Local State'te
    ikisine de aynı ad verilmiş, kodun kendi hatası değil) olmak üzere **3 kaynak** bulundu; Brave/Vivaldi/
    Chromium/Opera/Opera GX bu makinede kurulu değil, sessizce atlandı (hata yok).
  - `ChromiumBookmarkParser` ile okundu: Chrome'un `Bookmarks` dosyası var ama üç kök de boş (**0 yer imi**
    — kullanıcı Chrome'u yer imi için hiç kullanmamış, kod tarafında sorun değil, boş kökler doğru şekilde
    düşürüldü). Edge `Default`: **218 yer imi** (Bookmarks bar 212 + Other 1 + Mobile 5, iç içe 14 alt
    klasör) doğru sayıldı. Edge `Profile 1`: **0 yer imi** (kullanılmayan ikincil profil).
  - `NetscapeBookmarkParser` ile, kullanıcının `Downloads` klasöründe zaten duran gerçek bir HTML dışa
    aktarma dosyası (`bookmarks_9_27_26.html`, oluşturulmadı — sadece okundu) ayrıştırıldı: **232 yer imi**,
    çok seviyeli iç içe klasörler (Work › Müşteri Sistem › ClientA/ClientB/ClientC/... , SAP › Yararlı/
    Okunacak/RAP, Development › Frontend, vb.), Türkçe karakterli klasör/yer imi adları (Işıklı,
    Gümüşlük, e-donüşüm, İstanbul, Eğitim) doğru decode edildi, en az bir gerçekten boş klasör
    ("Eski VPN") beklendiği gibi sessizce budandı (çıktıda görünmüyor).
- **Sapmalar / netleştirmeler**:
  - Spec'in verdiği imza `NetscapeBookmarkParser.Parse(string html, string folderName)` sadece iki
    parametre aldığından, "No bookmarks found in `<file>`." mesajındaki `<file>` gerçek dosya yolu değil,
    çağıranın geçtiği `folderName` (HTML importunda zaten "Bookmarks (`<dosya adı>`)" şeklinde) —
    fonksiyonun dosya yolunu bilmesi için imzayı genişletmek gerekirdi, spec'in imzasına sadık kalındı.
  - Durum satırındaki "Imported N bookmarks from `<source>`..." ifadesindeki `<source>` tarayıcı
    kaynakları için `DisplayName` ("Chrome", "Edge (Work)"), HTML dosyası için dosya adı (uzantılı) —
    spec bunu tam olarak belirtmiyordu, en okunaklı seçenek uygulandı.
  - "returns to the List page at that folder" ifadesi, yeniden içe aktarmada önceki içe aktarmanın
    kullanıcı tarafından başka bir klasöre taşınmış olabileceği durumu da kapsayacak şekilde genelleştirildi:
    panel her zaman sonuç klasörünün **gerçek o anki ebeveynine** gider (yeni ekleme için bu zaten Ayarlar
    açılırkenki klasörle aynı; taşınmış bir yeniden içe aktarma için taşındığı yere gider), spec metni
    tam olarak bunu ayırt etmiyordu ama `BookmarkImport.Apply`'ın "anywhere under root" ifadesiyle tutarlı.
  - `SettingsViewModel`/`MainViewModel` gibi App katmanı sınıfları için (mevcut projede zaten kural)
    birim testi yazılmadı — pure Core mantığı (`Bookmarks/` klasörünün tamamı) %100 test edildi, App
    katmanındaki akış sadece kod incelemesi + build ile doğrulandı (var olan `SettingsViewModel`/
    `MainViewModel` de test edilmiyor, aynı kod tabanı kuralı).
- **Canlı doğrulanmadı** (kullanıcı tercihi ve spec §4 kısıtı: uygulama başlatılmadı, otomasyon/ekran
  görüntüsü yok): "Import bookmarks…" butonunun gerçek UI'da tıklanması, açılan `ContextMenu`'nün
  konumlanması/teması, OpenFileDialog akışı, `MainViewModel`'in ayarlar sayfasından gelen olayı işleyip
  panelin doğru klasöre dönmesi — bunlar kullanıcının kendi manuel testine kalıyor (`test-at-end` kuralı).
- Do not commit talimatına uyuldu — hiçbir değişiklik commit edilmedi.
- Orkestratör + Fable incelemesi sonrası düzeltmeler: arama modundayken açılan Ayarlar'dan içe aktarmada liste eski
  arama sonuçlarını gösteriyordu (arama temizleniyor); yeniden içe aktarma kesilmiş (Ctrl+X) bir yer imini ağaçtan
  koparınca `CutNode` asılı kalıyordu (temizleniyor); nesne olmayan JSON kökü/`children` öğesi veya bozuk `Local State`
  `InvalidOperationException` ile UI'ı çökertebiliyordu (atlanıyor / `BookmarkImportException`); `bookmarks:<kaynak>` id'si
  elle bir klasör-olmayan öğeye verilmişse aynı id'li ikinci düğüm oluşuyordu (yeni id). +5 regresyon testi → 294 test.

## Microsoft Store yayını — plan (2026-09-28, onay bekliyor)

Referans: `desktop-tiler-cmdpal` Store gönderimi (2026-09-25). Aynı kalıp: MSIX, Store'a imzasız bundle, GitHub'a aynı kimlikle self-signed.

- [x] **0. Partner Center:** `ABAPer.YourLauncher`, Publisher `CN=48947E55-115C-445B-9841-39A5BCF271DD`, PublisherDisplayName `ABAPer` (Desktop Tiler ile aynı hesap).
- [x] **1. Spike (2026-09-28, Win11 26100):** aynı kimlikle paketlenmiş probe exe, `shell:AppsFolder` ile gerçek aktivasyon. Sonuçlar:
  - Paketli süreç `%APPDATA%` altında **yeni klasör** açarsa → `LocalCache\Roaming\...`'e yönleniyor (Explorer/harici editör göremez). **Var olan** gerçek klasördeki dosyaları değiştirme, `File.Replace` ve o klasöre yeni dosya → **gerçek** konuma yazılıyor.
  - Paketli sürecin HKCU yazımı **sanallaştırılıyor** (gerçek `Run` anahtarına ulaşmaz) → StartupTask şart.
  - Başlatılan child process'ler (CreateProcess ve ShellExecute) container **dışında** çalışıyor (HKCU yazımları gerçek) → launcher'dan açılan uygulamalar normal davranır. Windows 10'da doğrulanmadı.
  - Chrome `Local State` okunabiliyor (bookmark içe aktarma çalışır). Hotkey/tray bu spike'ta ölçülmedi (standart Win32, düşük risk; 5. adımda).
- [x] **2. Paketleme (commit 3d87ef4):** `packaging/AppxManifest.xml` (Win11+, en-US, runFullTrust, StartupTask `YourLauncherStartup`), `packaging/Assets` (18 PNG, `scripts/make-icons.ps1`), `scripts/pack.ps1 [-Sign]`, `scripts/pack-store.ps1`; sürüm tek kaynak `Directory.Build.props` `<Version>0.1.0`. x64 msix 65 MB, bundle ~120 MB.
- [x] **3. Paketli mod (kod):** `Services/PackageContext.cs` (IsPackaged, StartupTask aktivasyonu = sessiz başlangıç, `ToPhysicalPath` ile config/error.log açma); `StartupService` paketliyken StartupTask kullanır, `DisabledByUser/Policy` notu ayarlar sayfasında görünür. TFM `net10.0-windows10.0.19041.0` (WinRT projeksiyonu), `SupportedOSPlatformVersion` 19041 (düz exe Win10'da çalışmaya devam eder). Projeksiyon ReadyToRun dışı bırakıldı (klasör publish'te 52.8→23.7 MB). Açık konu: single-file exe (GitHub) 134→198 MB — single-file modunda R2R exclude uygulanmıyor; sıkıştırma 78 MB ama +90 MB RAM, reddedildi.
- [x] **4. Yasal/listing:** `PRIVACY.md` (public: https://github.com/xmsadik/your-launcher/blob/main/PRIVACY.md), `tasks/store-submission.md` (açıklama, arama terimleri, runFullTrust gerekçesi 469 karakter, sertifikasyon notları, IARC). Repo public yapıldı; müşteri adları geçmişten temizlendi (eski geçmiş: private `your-launcher-archive` + `C:\Users\tr11677\your-launcher-backup-2026-09-28.bundle`).
- [x] **5. Doğrulama:** yerel WACK şirket makinesinde yönetici izni olmadığı için çalıştırılamadı (Partner Center aynı testleri yüklemede yapıyor). Elle kontrol: bundle üretimi başarılı, x64/arm64 exe mimarileri doğru, pdb yok, `en-US`, gerçek ikonlar, 312 test yeşil. Paketli uçtan uca test yapılmadı (isteğe bağlı: Developer Mode ile `Add-AppxPackage -Register dist\layout-x64\AppxManifest.xml`).
- [x] **6. Gönderim (2026-09-28):** kullanıcı `YourLauncher_0.1.0.0_Bundle.msixbundle`'ı Partner Center'a yükledi; **sertifikasyon onayı bekleniyor.**
- [x] **7. Yayında (2026-09-29):** Store onayı geldi — https://apps.microsoft.com/detail/9p98gw6rjhh2 (Store ID `9P98GW6RJHH2`). README'ye Store linki eklendi; GitHub release `v0.1.0` (imzalı x64 MSIX + `.cer`).
  Eski not — **Onay sonrası:** ret gelirse mesajı analiz et (olası konular: runFullTrust gerekçesi, ilk açılışta başlangıca otomatik ekleme, yalnızca Windows 11). Onaylanırsa: README'ye Store linki; GitHub için `pack.ps1 -Sign` ile imzalı MSIX release (198 MB tek dosya exe sorununu da önemsizleştirir).

## 0.1.1 (2026-09-29)

- [x] **winget:** README'de `winget install 9P98GW6RJHH2 --source msstore` (Store kaydı winget'te otomatik). `winget-pkgs`'e GitHub MSIX manifesti gönderilmedi — self-signed paket winget doğrulamasından geçmez; Store kaynağı yeterli.
- [x] **arm64 GitHub paketi:** release'e x64 + arm64 imzalı MSIX + `.cer`.
- [x] **"What's new":** `Core/Diagnostics/VersionMarker.cs` — config klasöründe `last-version.txt`; eski sürüm (veya marker yok ama config.json var = 0.1.0'dan güncelleme) → tray balonu "Updated to X. Click to see what's new." → tıklayınca GitHub release sayfası. Taze kurulum/aynı sürüm/downgrade/bozuk marker → balon yok. ConfigService'ten önce çağrılır (taze kurulumda config.json oluşmadan). 7 test.
- [x] **Kararsız testler:** kök neden — `TargetCheckTests` ve `PathResolverTests` process geneli `PATH`'i paralel değiştiriyordu; perf testi paralel yük altında zamanlanıyordu. `TestCollections.cs`: `Process environment` ve `Timing` koleksiyonları (`DisableParallelization`); perf testi sorgu başına 5 ölçümün en iyisini alır (16 ms bütçe aynı).
- Doğrulama: Release build 0 hata; `dotnet test` 3 kez art arda 319/319 yeşil. Duman testi: scratch config + `last-version.txt`=0.1.0.0 ile `--silent` → marker 0.1.1.0 oldu, uygulama çalıştı (balonun ekranda görünmesi gözle doğrulanmadı).
- [x] **Store gönderimi (2026-09-29):** kullanıcı `YourLauncher_0.1.1.0_Bundle.msixbundle`'ı Partner Center'a yükledi; **sertifikasyon onayı bekleniyor.**
- [ ] **Onay sonrası:** Store sayfasında 0.1.1'i doğrula; ret gelirse mesajı analiz et. Sıradaki (0.2) aday: açılışta argüman (`{}` yer tutuculu URL/komut düğümleri).

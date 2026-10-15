# Panelin ekrandaki konumu için ayar

**Durum:** TAMAMLANDI
**Tarih:** 2026-10-15 (son güncelleme: 2026-10-15)
**Etkilenen yer:** `src/Launcher.Core/Model/Settings.cs`, `src/Launcher.App` (MainWindow, ayarlar sayfası, metinler), testler, README, `config.example.json`. SAP'a yazma yok.

## A. BRIEF (işe başlamadan önce)
### Özet
YourLauncher paneli bugün her zaman fare imlecinin bulunduğu ekranda, yatayda ortada ve üst kenarı ekran
yüksekliğinin üçte birinde açılıyor. Zebar ile hazırlanan "caelestia" mockup'ında panel alt kenara yapışık
duruyor. Bu yüzden panelin konumunu seçen bir ayar (orta, üst kenar, alt kenar) ve kenardan bırakılacak boşluk
ayarı eklenecek.

### Arka plan
- **Zebar:** Windows için HTML/CSS ile yazılan masaüstü çubuğu (bar) aracı. Kullanıcı bununla bir "ricing"
  (masaüstünü özelleştirme) düzeni hazırlıyor.
- **Mockup:** `%USERPROFILE%\.glzr\zebar\mockups\caelestia\index.html`. Panel (`#launch`) yatayda ortada,
  `bottom: var(--edge)` ile alt çerçeve kenarına yapışık, yalnız üst köşeleri yuvarlak.
- **Çalışma alanı (work area):** Ekranın görev çubuğu ve alan ayıran çubuklar çıkarıldıktan sonra kalan kısmı.
  Konum bu alana göre hesaplanır, yani panel görev çubuğunun altına girmez.
- Panel yüksekliğini içeriğe göre kendisi ayarlıyor (`SizeToContent="Height"`). Liste uzayıp kısaldıkça WPF
  üst kenarı sabit tutar; alt kenara yapışık modda bu yanlış olur, panel aşağı taşar.

### Kapsam / Kapsam dışı
- Kapsam: `settings.position` (`center` | `top` | `bottom`, varsayılan `center` = bugünkü davranış),
  `settings.edgeMargin` (0-200, varsayılan 0; üst/alt kenar ile panel arasındaki boşluk), ayarlar sayfasında
  iki alan, alt modda yükseklik değişince alt kenarın yerinde kalması, testler ve belgeler.
- Kapsam dışı: mockup'taki görünüm (arama kutusunun altta olması, yalnız üst köşelerin yuvarlak olması).
  Windows 11 köşeleri ya hepsini yuvarlar ya hiçbirini; bu ayrı bir tasarım işi.

### Plan
- [x] 1. Model: `PanelPosition` enum'u, `Position` ve `EdgeMargin` ayarları.
- [x] 2. MainWindow: konum hesabı, alt modda yükseklik değişince yeniden hizalama, ayar değişince yeniden konumlama.
- [x] 3. Ayarlar sayfası ve İngilizce/Türkçe metinler.
- [x] 4. Testler (varsayılan değerler, config'e yazılıp geri okunma).
- [x] 5. README ve `config.example.json`.
- [x] 6. Derleme ve testlerin çalıştırılması.

### Riskler ve varsayımlar
- Varsayım: Zebar çubuğu çalışma alanını ayırmıyorsa (alan ayırmayan çerçeve), panel çerçevenin üstüne biner.
  Bunun için `edgeMargin` var.
- Risk: alt modda panel ilk açılışta bir kare boyunca eski yükseklikle konumlanıp sonra düzeltilebilir.

### ⚠ Senden beklenenler (başlamadan önce)
Senden beklenen bir şey yok.

### Kararlar
- 2026-10-15: Varsayılan `center`, böylece mevcut kullanıcıların davranışı değişmez.

## B. RAPOR (iş bittikten sonra)
### Sonuç
Panel artık ayarlardan seçilen yerde açılıyor: ortada (eski davranış, varsayılan), üst kenarda veya alt kenarda.
Üst ve alt kenarda, kenar ile panel arasına istenen kadar boşluk bırakılabiliyor.

### Yapılanlar
- `settings.position` (`center` | `top` | `bottom`) ve `settings.edgeMargin` (0-200 px) eklendi. Eski config
  dosyalarında bu alanlar yoksa `center` ve 0 kullanılır, yani mevcut kullanıcıda hiçbir şey değişmez.
- Panel her zaman fare imlecinin bulunduğu ekranda, yatayda ortalı açılır; dikey yer bu ayara göre hesaplanır.
  Hesap ekranın tamamına değil çalışma alanına göre yapılır, yani alan ayıran görev çubuğu ve Zebar çubukları
  hesaba katılır.
- Alt kenar modunda panelin yüksekliği değiştiğinde (liste uzayıp kısalınca, başka sayfaya geçince) alt kenar
  yerinde kalır ve panel yukarı doğru büyür. Bu gerekliydi, çünkü WPF içeriğe göre boyutlanan pencerede üst
  kenarı sabit tutar.
- Ayarlar sayfasına "Panel konumu" (açılır liste) ve "Kenar boşluğu" (sayı kutusu) eklendi; Türkçe ve İngilizce
  metinler yazıldı. Değişiklik Kaydet ile uygulanır; panel açıksa hemen yer değiştirir.
- README ve `config.example.json` güncellendi.

### Nasıl doğrulandı
- Derleme: 0 uyarı, 0 hata. Testler: 794/794 geçti (2 yeni test: varsayılan değerler ve config'e yazılıp geri
  okunma).
- Gerçek uygulama: ayrı bir config klasörüyle ikinci bir kopya çalıştırıldı (kullanıcının açık YourLauncher'ı
  etkilenmedi), panel "show" mesajıyla açıldı, yeri Win32 `GetWindowRect` ile ölçüldü. Fare ve klavye
  kullanılmadı. 10 px boşlukla ölçüldü. Düzeltme (2026-10-15): ekran aslında 3840x2160, %150 ölçekte; ölçen
  PowerShell DPI farkında olmadığı için değerler 1,5'e bölünmüş (sanal) piksel. Oranlar doğru:

| Konum | Panelin üst kenarı | Panelin alt kenarı | Beklenen |
|---|---|---|---|
| bottom (boş liste) | 1321 | 1430 | Alt kenar 1440 - 10 = 1430 |
| bottom (6 öğe, panel daha uzun) | 1141 | 1430 | Alt kenar yine 1430 |
| top | 10 | 299 | Üst kenar 0 + 10 = 10 |
| center | 480 | 769 | Üst kenar 1440 / 3 = 480 |

- %150 ölçekte gerçek piksellerle ölçüm Zebar oturumunda ve `tasks/2026-10-15-caelestia-gorunumu.md` işinde
  yapıldı. Farklı ölçekli iki ekran arasında geçiş denenmedi; bu makinede tek ekran var.

### Değişen nesneler / dosyalar
- `src/Launcher.Core/Model/Settings.cs` (`PanelPosition`, `Position`, `EdgeMargin`, `MaxEdgeMargin`)
- `src/Launcher.App/Views/MainWindow.xaml.cs` (konum hesabı, alt kenara sabitleme)
- `src/Launcher.App/ViewModels/MainViewModel.cs`, `SettingsViewModel.cs`
- `src/Launcher.App/Views/SettingsView.xaml`, `SettingsView.xaml.cs`
- `src/Launcher.App/Localization/Strings.cs`, `Strings.resx`, `Strings.tr.resx`
- `tests/Launcher.Core.Tests/ThemeCatalogTests.cs`, `README.md`, `config.example.json`

### Yapılmayanlar ve açık kalanlar
- Mockup'taki görünüm yapılmadı: arama kutusunun altta, listenin üstünde olması ve yalnız üst köşelerin yuvarlak
  olması. Windows 11 pencere köşelerini ya hepsini yuvarlar ya hiçbirini; bunun için ayrı bir tasarım işi gerekir.
- Değişiklik commit edilmedi ve sürüm çıkarılmadı; kullanıcı istemedi.

### ⚠ Senden beklenenler (şimdi)
| # | Eylem | Neden | Aciliyet | İşi durduruyor mu? (Evet/Hayır) |
|---|---|---|---|---|
| 1 | Ayarlar → Panel konumu → Alt kenar seçip Zebar düzeniyle birlikte gözle denemek | Ölçüm doğru, ama Zebar çerçevesiyle birlikte nasıl durduğu ancak gözle görülür | Düşük | Hayır |
| 2 | Commit ve sürüm isteyip istemediğini söylemek | Değişiklik şu an yalnız çalışma kopyasında | Düşük | Hayır |

### Sonraki oturum için not
Zebar çerçevesi çalışma alanını ayırmıyorsa panel çerçevenin üstüne biner; o zaman `edgeMargin` çerçeve
kalınlığına (mockup'taki `--edge`) eşitlenmeli. Ölçüm sırasında çalışma alanı birkaç kez değişti (Zebar o sırada
yeniden yükleniyordu), bu normal.

## Zebar (Caelestia) oturumundan bulgular — 2026-10-15

Test, notlardaki adımlarla yapıldı: ayrı config klasörü, `Ctrl+Shift+Alt+F12`, `startWithWindows: false`, `position: "bottom"`, `edgeMargin: 10`. Fare/klavye sürülmedi; panel ikinci `Start-Process` ile açıldı. Test kopyası kapatıldı, test klasörü silindi; kurulu sürüme (Store, `Win+Alt+Space`) dokunulmadı. Not: kurulu sürümün kısayolu `Alt+Space` değil, `Win+Alt+Space`.

### Ölçüm (fiziksel piksel, %150 ölçek, 3840x2160)
| Değer | Sonuç | Yorum |
|---|---|---|
| Panel | L=1485 T=1658 R=2445 B=2145 | B = 2160 − 15 → `edgeMargin: 10` Caelestia çerçevesinin iç kenarına tam oturuyor |
| Çalışma alanı | L=90 T=0 R=3840 B=2160 | Sol çubuk (`cl-bar`, 60 mantıksal px) yer ayırıyor; alt/üst/sağ çerçeve ayırmıyor |
| Yatay merkez | panel 1965, çerçeve boşluğu 1957,5 | 7,5 px (5 mantıksal) sağda; gözle fark edilmiyor. İstenirse `settings.offsetX` gibi bir ince ayar |

### Caelestia görünümü için istekler (öncelik sırasıyla)
1. **Alta yapışık şekil:** alt iki köşe düz, üst iki köşe 28 mantıksal px yuvarlak; panelin alt-sol ve alt-sağ dışında 22 px'lik içbükey "akış" köşeleri (çerçeveyle birleşme). Pencere her iki yanda 22 px saydam pay alıp köşeleri kendisi çizebilir; Windows 11 yuvarlamasını `DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_DONOTROUND` ile kapatmak gerekir. Kenarlık (border) olmasın.
2. **Renk:** Zebar'daki Caelestia panelleriyle aynı, opak çerçeve rengi. Dark Glass teması için: zemin `#101013` (opak), metin `#e7e7ec`, soluk metin `#8f8f99`, vurgu `#c6c3ff`, satır üzeri `rgba(231,231,236,0.06)`, seçili satır `rgba(231,231,236,0.11)`, yazı tipi Rubik. Bir "Caelestia (Dark Glass)" teması olarak eklenebilir.
3. **Arama kutusu altta** (yalnız `position: "bottom"` iken, isteğe bağlı ayar): Caelestia başlatıcısında arama en altta, liste onun üstünde.
4. **Açılış animasyonu:** alttan yukarı kısa kayma (~300 ms).
5. **Zebar'dan açma:** Zebar çerçevesinin alt-orta şeridine fare gelince başlatıcıyı açmak istiyoruz. Bunun için Zebar'ın çalıştırabileceği sabit bir yol gerekiyor (Store sürümünün yolu sürümle değişiyor). Bir "app execution alias" (ör. `yourlauncher.exe`) ya da `YourLauncher.exe --show` benzeri bir komut varsa yazın.
6. **(Sonra) Tema eşlemesi:** Zebar teması değişince başlatıcının da değişmesi. Zebar tarafı seçili temayı bir dosyaya (ör. `%USERPROFILE%\.glzr\zebar\theme.txt`) yazabilir; başlatıcı o dosyayı izleyebilirse eşleme kurulur. Önce 1-5.

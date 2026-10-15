# Caelestia görünümü: kenara yapışık panel stili

**Durum:** TAMAMLANDI
**Tarih:** 2026-10-15 (son güncelleme: 2026-10-15)
**Etkilenen yer:** `src/Launcher.App` (MainWindow, ayarlar, temalar, menü stili), `src/Launcher.Core` (Settings, ThemeCatalog), `packaging/AppxManifest.xml`, testler, README. SAP'a yazma yok.

## A. BRIEF (işe başlamadan önce)
### Özet
Zebar ile hazırlanan Caelestia masaüstü düzeninde başlatıcının alt çerçeveye yapışık ve çerçeveyle kaynaşmış
görünmesi isteniyor. Bunun için panele isteğe bağlı bir "kenara yapışık" (docked) stil, Caelestia renklerinde bir
tema, kısa bir açılış animasyonu ve Zebar'ın paneli açmak için çağırabileceği sabit bir komut adı eklenecek.
Varsayılan görünüm ve davranış değişmeyecek, yani Store'daki mevcut kullanıcılar hiçbir fark görmeyecek.

### Arka plan
- **Zebar:** HTML/CSS ile yazılan Windows masaüstü çubuğu. **Caelestia:** kullanıcının Zebar ile kurduğu düzen.
  Mockup: `%USERPROFILE%\.glzr\zebar\mockups\caelestia\index.html`.
- İstekler Zebar oturumundan geldi: `tasks/2026-10-15-panel-konumu.md`, "Zebar (Caelestia) oturumundan
  bulgular" bölümü. Öncelik sırası: 1 şekil, 2 renk, 3 arama kutusu altta, 4 açılış animasyonu, 5 Zebar'dan
  açma, 6 tema eşlemesi (sonraya).
- Önceki iş (`settings.position` ve `settings.edgeMargin`) bitti. Zebar oturumu %150 ölçekte `bottom` +
  `edgeMargin: 10` ile panelin çerçevenin iç kenarına tam oturduğunu ölçtü.
- **Mica:** Windows 11'in pencere arkasını bulanık gösteren arka planı. Panel bugün bunu kullanıyor. Mica,
  pencerenin saydam (layered) olmamasını şart koşuyor.
- **Layered pencere:** Piksel başına saydamlığı olan pencere (WPF'te `AllowsTransparency=True`). Tamamen saydam
  pikseller fare tıklamasını alttaki pencereye geçirir. Bu ayar pencere oluşmadan önce verilmek zorundadır;
  sonradan değiştirilemez.
- **İçbükey köşe (fillet):** panelin yapışık kenardaki köşelerinin dışında, çerçeveye kıvrılarak bağlanan
  22 px'lik çeyrek daire. Çizebilmek için pencerenin iki yanında saydam bir pay gerekir.
- **App execution alias:** Store uygulamasına `yourlauncher.exe` gibi sabit bir komut adı veren manifest kaydı
  (Windows Terminal'in `wt.exe`'si gibi). Store paketinin klasör yolu her sürümde değiştiği için Zebar ancak
  bu yolla güvenilir şekilde çağırabilir.

### Kapsam / Kapsam dışı
Kapsam:
- Yeni ayar `settings.panelStyle`: `"floating"` (varsayılan, bugünkü görünüm) | `"docked"`.
  - `floating` seçiliyken kod yolu bugünküyle aynı kalır: Mica, opaklık ve dört köşe yuvarlak.
  - `docked` seçiliyken pencere saydam açılır ve Mica kullanılmaz. Zemin opaktır, bu yüzden opaklık ayarı bu
    stilde etkisizdir ve ayarlar sayfasında bu belirtilir. Şekil `position` ayarını canlı izler:
    - `bottom` veya `top` iken yapışık kenardaki iki köşe düz, karşı iki köşe 28 px yuvarlak olur; yanlarda
      22 px içbükey köşe bulunur ve kenarlık olmaz.
    - `center` iken dört köşe yuvarlak olur ve içbükey köşe çizilmez.
  - Arama kutusu yapışık kenar tarafına geçer. `bottom` iken en altta durur, liste onun üstünde kalır.
  - Açılışta içerik yapışık kenardan içeri kayar (yaklaşık 250 ms). Windows'ta animasyonlar kapalıysa kayma
    olmaz. Gizlenme anında olur.
- Yeni tema "Caelestia (Dark Glass)". Renkler: zemin `#101013`, metin `#e7e7ec`, soluk metin `#8f8f99`, vurgu
  `#c6c3ff`. Satır üzeri `#1D1D20`, seçili satır `#28282B`; bu ikisi istenen yarı saydam değerlerin `#101013`
  üstündeki opak karşılığıdır. Tema her iki stille de seçilebilir.
- Rubik yazı tipi uygulamaya gömülecek. Sabit Regular ve Medium dosyaları kullanılacak, çünkü WPF değişken
  (variable) yazı tiplerini iyi desteklemiyor. Lisans dosyası (OFL) da pakete girecek. Yalnız bu temada
  kullanılacak; sağ tık menüleri de aynı yazı tipini alacak.
- Store manifestine `yourlauncher.exe` app execution alias'ı eklenecek. Komut çalışan kopyaya "show" gönderir
  ve paneli açar. Panel zaten açıksa yalnız öne getirir, kapatmaz.
- Ayarlar sayfası, TR/EN metinler, testler, README, `config.example.json`.

Kapsam dışı:
- 6. istek (Zebar teması değişince başlatıcının da değişmesi). Zebar oturumu da sonraya bıraktı.
- Yatayda 7,5 px kayma (`offsetX`). Gözle fark edilmiyor.
- Panel genişliği. Mockup'ta 760 px, uygulamada 640 px; istenmedi.
- Zebar tarafındaki fare algılama ve komut çağrısı. Bunlar o oturumun işi.
- Store'a gönderim. Gönderimi kullanıcı yapar.

### Plan
- [x] 1. Model: `PanelStyle` enum'u ve ayarı (varsayılan `floating`); config testleri.
- [x] 2. Pencere modu: `docked` ise MainWindow kurucusunda, pencere tutamacı oluşmadan önce
      `AllowsTransparency=True` ve saydam pencere arka planı verilir; Mica/WindowChrome adımı atlanır; Windows
      11 köşe yuvarlaması kapatılır; metin netliği için ClearType ipucu açılır. Stil değişikliği yeniden
      başlatınca uygulanır ve ayarlar sayfası bunu dil ayarındaki gibi bir notla söyler.
- [x] 3. Şekil: zemin ve içbükey köşeler `position`'a göre çizilir. Konum hesabı yan payları hesaba katar,
      böylece görünen panel ortada ve kenarda kalır.
- [x] 4. Arama kutusu: `docked` + `bottom` iken satır sırası ters çevrilir.
- [x] 5. Açılış animasyonu: pencere değil içerik kaydırılır. Pencerenin kendisi hareket ettirilmez, çünkü alt
      kenar sabitleme her yükseklik değişiminde pencereyi yeniden konumluyor. Animasyon yalnız hotkey/show
      açılışında çalışır.
- [x] 6. Tema: palet, gömülü Rubik + OFL, menülerde ortak yazı tipi kaynağı, kontrast testleri.
- [x] 7. App execution alias: manifest kaydı. Doğrulamada komut, öndeki terminalden değil arka plandaki bir
      süreçten çağrılır, çünkü Windows öne gelme iznini buna göre veriyor.
- [x] 8. Ayarlar sayfası, TR/EN metinler, README (alias yalnız Store/MSIX kurulumunda vardır), örnek config.
- [x] 9. Derleme, testler ve gerçek uygulamada ölçüm. Ölçümde ayrı config klasörü kullanılır ve fare/klavye
      sürülmez. `floating` görünümünün değişmediği ekran görüntüsüyle karşılaştırılır. %150 ölçekte görsel
      kontrolü Zebar oturumu yapar, çünkü bu makine %100 ölçekte.

### Riskler ve varsayımlar
- **Store'daki kullanıcılar:** Varsayılan `floating` ve bu yolun kodu değişmiyor. Değişiklikler ya yeni bir
  dalda (`docked`) ya da yalnızca ekleme niteliğinde: yeni tema, yazı tipi ve alias. Asıl risk, MainWindow'daki
  ortak kodda bir hata yapıp `floating`'i bozmak. Bu yüzden 9. adımda `floating` ayrıca karşılaştırılacak.
- **Paket:** Alias, Windows Terminal'in de kullandığı standart bir manifest kaydı ve sertifikasyonda bilinen
  bir sorunu yok. Rubik paketi birkaç yüz KB büyütür.
- **Zebar'dan açma:** Her fare gelişinde kısa ömürlü bir süreç başlar (yaklaşık 0,3-0,6 sn). Windows,
  arka plandaki bir süreçten gelen öne gelme isteğini reddedebilir; o zaman panel açılmaz. Bu, 7. adımda
  gerçek koşulda denenecek. Sorun çıkarsa çözüm ya mevcut "show" kanalındaki öne getirme kodunun
  güçlendirilmesi ya da Zebar'ın hotkey'i taklit etmesidir.
- **Renk uyumu:** İçbükey köşeler tema zemin rengiyle çizilir. Zebar çerçevesi yarı saydam ve bulanık
  (`rgba(16,16,19,0.86)`), panel ise opak. Birleşme yerinde hafif bir ton farkı görülebilir. Ayrıca içbükey
  köşeler `edgeMargin`'in çerçeve kalınlığına (10) eşit olduğunu varsayar.
- **Yeniden başlatma:** Çalışırken stil değiştirmenin tek yolu pencereyi yeniden oluşturmak. Tepsi simgesi ve
  kısayol kaydı pencereye bağlı olduğu için bu riskli. Bu yüzden önerilmiyor.
- **Pencere bölgesi (`SetWindowRgn`):** Şekil vermenin diğer yolu bu. Yeniden başlatma istemez, ama kenarlar
  tırtıklı görünür (kenar yumuşatma yok). Bu yüzden önerilmiyor.

### ⚠ Senden beklenenler (başlamadan önce)
| # | Karar / eylem | Neden gerekli | İşi durduruyor mu? (Evet/Hayır) | Cevap |
|---|---|---|---|---|
| 1 | Bu planı onayla | Yayındaki uygulamaya yeni bir pencere modu ekliyor (varsayılan değişmiyor) | Evet | Onaylandı (2026-10-15) |
| 2 | Arama kutusunun alta inmesi `docked` + `bottom` iken otomatik olsun mu, ayrı bir ayar mı olsun? | Zebar oturumu "isteğe bağlı" dedi. Öneri: otomatik, çünkü yapışık stilin parçası ve ayar sayısı az kalır | Hayır | Cevap yok, öneri ile devam: otomatik |
| 3 | Rubik gömülsün mü? | Kurulu değil; gömülmezse tema Segoe UI ile görünür. Öneri: göm | Hayır | Cevap yok, öneri ile devam: gömülür |
| 4 | Bu iş bitince sürüm (0.3.0) çıkarılsın mı? | Alias ancak yeni bir paketle kullanıcıya ulaşır | Hayır (iş bitince sorulur) | |

### Kararlar
- 2026-10-15: 6. istek (tema eşlemesi), `offsetX` ve panel genişliği kapsam dışı.
- 2026-10-15: Kullanıcı, Store yapısını bozacak bir adım olursa onun Zebar tarafında yerel çözülmesini istedi.
  Değerlendirme: hiçbir adım varsayılan davranışı değiştirmiyor; hepsi ya isteğe bağlı ya da ekleme. Bu yüzden
  yerel çözüme geçilmedi.
- 2026-10-15: Plan ucuz bir modele eleştirtildi. Eklenenler: opaklığın `docked`'da etkisiz kalması, pencere arka
  planının saydam yapılması, animasyonun pencere yerine içeriğe uygulanması, alias'ın arka plan süreçten
  denenmesi, yazı tipinin menülerde de kullanılması, OFL lisans dosyası.
- 2026-10-15: Kullanıcı planı olduğu gibi onayladı (seçenek 1: 1-5 numaralı istekler tek işte).
- 2026-10-15: Kod incelemesindeki (ucuz model) 4 bulgu düzeltildi: içeriğin yuvarlak köşelere kırpılması; ayar
  kaydedilince açık panelin gereksiz yere yeniden konumlanması (varsayılan stilde de oluyordu); dosya seçme penceresi
  açıkken gelen "show" isteğinin paneli gizlemesi (yayındaki sürümde de var); ters dizmenin yalnız liste sayfasına
  uygulanması. Tanınmayan config değerinin dosyayı geçersiz kılması mevcut davranışla tutarlı olduğu için bırakıldı.

## B. RAPOR (iş bittikten sonra)
### Sonuç
Panele isteğe bağlı "kenara yapışık" stil, Caelestia (Dark Glass) teması (gömülü Rubik yazı tipiyle) ve Store/MSIX
kurulumunda `yourlauncher.exe` komut adı eklendi. Varsayılan görünüm yayındaki sürümle piksel piksel aynı kaldı.

### Yapılanlar
- `settings.panelStyle` (`floating` varsayılan | `docked`). Yapışık stilde pencere saydam, zemin opak, Mica yok;
  stil değişikliği yeniden başlatınca uygulanır (ayarlar sayfası not gösterir), opaklık ayarı bu stilde etkisizdir
  (not gösterilir).
- Şekil `position`'ı canlı izler: alt/üst kenarda o kenarın köşeleri düz, karşı köşeler 28 px yuvarlak, iki yanda
  22 px içbükey köşe; ortada dört köşe yuvarlak. İçerik bu şekle kırpılır.
- Alt kenarda liste sayfasının sırası: liste, ipucu çubuğu, ayırıcı, arama kutusu. Ayarlar ve düzenleyici
  sayfaları normal sırada kalır.
- Açılışta içerik kenardan içeri kayar (250 ms); Windows animasyonları kapalıysa kayma olmaz.
- Tema "Caelestia (Dark Glass)": istenen renkler; Rubik Regular/Medium gömülü (kaynak: github.com/googlefonts/rubik,
  `old/version-2/fonts/ttf/`, SIL OFL 1.1; lisans dosyası pakette). Panel ve sağ tık menüleri temanın yazı tipini
  kullanır; diğer temalarda yazı tipi eskisi gibi Segoe UI.
- Manifest: `uap5:AppExecutionAlias` > `uap5:ExecutionAlias Alias="yourlauncher.exe"`.
- Ayrıca: açık bir dosya seçme penceresi varken gelen "show" isteği artık paneli gizlemiyor.
- Ayarlar sayfası, TR/EN metinler, README, `config.example.json`, 6 yeni test.

### Nasıl doğrulandı
Ekran 3840x2160, %150 ölçek. Ölçümler DPI farkında bir betikle gerçek piksellerle alındı. Test kopyaları ayrı
config klasörüyle çalıştı; kurulu sürüme dokunulmadı; fare/klavye sürülmedi.

| Kontrol | Yöntem | Sonuç |
|---|---|---|
| Derleme ve testler | `dotnet build`, `dotnet test` | 0 uyarı, 0 hata; 800/800 test geçti |
| Varsayılan görünüm değişmedi | Yayındaki kod (`6118863`) ayrı klasörde derlendi; aynı config ile iki sürümün paneli art arda görüntülenip piksel piksel karşılaştırıldı (varsayılan tema, koyu opak, Catppuccin Mocha) | Son düzeltmelerden sonra üçünde de 0 farklı piksel; pencere yeri ve boyutu aynı |
| Yapışık şekil | Alt, üst, orta konumda görüntü + köşe piksellerinin renk ölçümü | İçbükey köşeler doğru eğriyle çiziliyor; seçili satır yuvarlak köşeye kırpılıyor |
| Konum | Pencere sınırları | Alt: 1026 px genişlik (684 DIP), yatayda ortalı, içerik çalışma alanının 15 px (10 DIP) üstünde bitiyor. Üst: T=15 |
| Satır sırası | Liste ve ayarlar sayfası görüntüsü | Liste sayfasında arama altta; ayarlar sayfası normal sırada |
| Türkçe metinler, Rubik | Ayarlar sayfası görüntüsü | Doğru |
| Zebar senaryosu | İlk kopya sessiz başlatıldı, "show" isteği WMI ile arka plan sürecinden gönderildi | Panel açıldı ve öne geldi; ikinci kopya kalmadı. İstekten görünmeye 2,8 sn (cmd + Debug derleme açılışı dahil) |
| Manifest | Mevcut paket düzeninin geçici bir kopyası yeni manifestle `makeappx pack` edildi | İlk biçim reddedildi (`desktop:ExecutionAlias` uap5 altında geçersiz), düzeltildi, paketleme başarılı |

Doğrulanamayanlar:
- Açılış animasyonu: bu makinede Windows animasyon efektleri kapalı, uygulama da buna uyduğu için kayma
  görülemedi. Sistem ayarını değiştirmedim.
- `yourlauncher.exe` komut adının kendisi: yalnızca paket kurulunca oluşur. Paket kurmak senin Store kurulumunu
  değiştireceği için kurmadım. Aynı yol (ikinci çalıştırma) yukarıda arka plandan denendi.
- Release derlemede açılış süresi ölçülmedi.

### Değişen nesneler / dosyalar
- Core: `Model/Settings.cs`, `Theming/ThemeCatalog.cs`
- App: `App.xaml.cs`, `Interop/Win32.cs`, `Launcher.App.csproj`, `Services/ThemeService.cs`,
  `Services/ThemedMenuFactory.cs`, `Services/PanelStyleService.cs` (yeni), `Views/MainWindow.xaml(.cs)`,
  `Views/SettingsView.xaml(.cs)`, `ViewModels/SettingsViewModel.cs`, `Localization/Strings*.{cs,resx}`,
  `Fonts/` (yeni: Rubik-Regular.ttf, Rubik-Medium.ttf, OFL.txt)
- `packaging/AppxManifest.xml`, `README.md`, `config.example.json`, `tests/Launcher.Core.Tests/ThemeCatalogTests.cs`,
  `tasks/lessons.md`

### Yapılmayanlar ve açık kalanlar
- 6. istek (Zebar temasıyla eşleme), `offsetX`, panel genişliği: kapsam dışı (plan).
- Türkçe ayarlar sayfasında "Windows vurgu rengini kullan" etiketi onay kutusunun altına taşıyor. Yayındaki
  sürümde de var; bu işten gelmiyor, dokunulmadı.
- Alt kenarda pencerenin son 1 piksel satırı saydam kalıyor (DIP yuvarlaması). Arkada Zebar çerçevesi göründüğü
  için fark edilmemeli.
- Commit ve sürüm yok; kullanıcıya soruldu.

### ⚠ Senden beklenenler (şimdi)
| # | Eylem | Neden | Aciliyet | İşi durduruyor mu? (Evet/Hayır) |
|---|---|---|---|---|
| 1 | Commit edilsin mi, 0.3.0 sürümü çıkarılsın mı? | `yourlauncher.exe` ancak yeni paketle kullanıcıya ulaşır | Orta | Hayır |
| 2 | Animasyon, Windows animasyonları kapalıyken de oynasın mı? | Bu makinede kapalı, yani istenen kayma şu an hiç görünmez. Öneri: sistem ayarına uymaya devam | Düşük | Hayır |
| 3 | Zebar oturumuna sonucu ilet | O oturum `panelStyle: docked`, tema ve `yourlauncher.exe` yolunu kullanacak | Düşük | Hayır |

### Sonraki oturum için not
Görsel kontrol yardımcısı (DPI farkında yakalama, eski/yeni karşılaştırma) bu oturumun geçici klasöründeydi;
yöntem `tasks/lessons.md` içinde. Yeni sürüm çıkarılırsa önce Store paketi, en son imzalı paketler üretilmeli
(lessons.md).

## Zebar (Caelestia) oturumundan bulgular: kenara yapışık stil — 2026-10-15

Test, yönergedeki adımlarla yapıldı: ayrı config (`%TEMP%\yl-docked-test`), `Ctrl+Shift+Alt+F11`, `startWithWindows: false`, `theme: caelestiaDarkGlass`, `panelStyle: docked`, `position: bottom`, `edgeMargin: 10`; ilk kopya `--silent`, panel ikinci `Start-Process` ile açıldı (Zebar'dan açmanın taklidi). Fare/klavye sürülmedi, MSIX kurulmadı, kurulu Store sürümüne dokunulmadı. Test sonunda kopya kapatıldı (`WaitForExit`), test klasörü silindi.

### Ölçüm (fiziksel piksel, %150, 3840x2160)
| Değer | Sonuç | Beklenen |
|---|---|---|
| Pencere | L=1452 T=1667 R=2478 B=2146, genişlik 1026 | 1026 (2 × 33 px saydam pay + 960 px panel) — tamam |
| Alt kenar | B=2146 | 2145 + 1 px saydam son satır — tamam |
| Panel zemini | rgb(16,16,19) | `#101013` — tamam |
| Paneli çevreleyen çerçeve şeridi (Zebar) | rgb(15,14,18) ölçülen noktada | `rgba(16,16,19,.86)` duvar kâğıdı üzerinde |

### Görsel değerlendirme
- Sıra mockup'la uyumlu: liste, ipucu çubuğu, en altta arama ("Search…" solda, "Root" sağda). Seçili satırın solundaki vurgu çizgisi ve Rubik yazı tipi uyumlu.
- Üst köşeler yuvarlak; alt kenar düz ve çerçeve çizgisine oturuyor.
- Ton farkı: panel opak, Zebar çerçevesi %86 saydam. Parlak ve dokulu bir duvar kâğıdında (şu anki: mor/pembe noktalı) çerçeve şeridinde ve pencereyle çerçeve arasındaki boşlukta duvar kâğıdı noktaları seçiliyor, panelde seçilmiyor. Fark hafif; çözümü Zebar tarafında (çerçeveyi daha opak yapmak) — YourLauncher'da değişiklik gerekmiyor.
- İçbükey köşeler: renk ölçümünde içbükey alan da `#101013`; koyu pencere zemininde gözle ayırt etmek zor, görüntüde bir sorun görülmedi.

### Henüz denenmeyen
- Zebar alt-orta şeridinden `.cmd` ile açma: fare sürmeyi gerektiriyor, kullanıcı izni bekleniyor. Zebar tarafında alan hazır; yeni paketteki `yourlauncher.exe` takma adını çağırıyor.

### İstek (düşük öncelik)
- Panel genişliği: mockup'taki 760 DIP'e bir ayar (`panelWidth`?) eklenebilirse Caelestia panosunun ölçüsüyle daha iyi oturur. Şu hâli de kabul edilebilir.

### Ek (aynı gün): Zebar'dan açma ve çerçeve opaklığı
- Zebar'daki Dark Glass çerçevesi %86 → %94 opak yapıldı; panelle aradaki ton farkı küçüldü. YourLauncher'da değişiklik gerekmiyor.
- `.cmd` doğrudan çalıştırılınca (`cmd /c open.cmd`) arka plandaki test kopyasının paneli açıldı: YourLauncher tarafı doğru çalışıyor.
- Zebar'ın alt-orta şeridinden fareyle açma çalışmadı: o şerit Zebar'da fare olayı üretmedi (sağ alt köşe aynı yükseklikte çalışıyor, orta çalışmıyor; nedeni bulunamadı). Kullanıcı kararıyla Zebar'dan fareyle açma kaldırıldı; başlatıcı kendi kısayoluyla (`Win+Alt+Space`) açılacak. `yourlauncher.exe` takma adına Zebar tarafında artık ihtiyaç yok.
- Test kopyası kapatıldı, test config klasörü ve `.cmd` silindi.

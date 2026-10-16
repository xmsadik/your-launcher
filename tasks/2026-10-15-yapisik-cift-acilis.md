# Yapışık panelde çift açılma, ilk satırın köşede kesilmesi ve "Dark Glass" tema adı

**Durum:** TAMAMLANDI
**Tarih:** 2026-10-15 (son güncelleme: 2026-10-15)
**Etkilenen yer:** `src/Launcher.App/Views/MainWindow.xaml.cs`, `src/Launcher.App/Launcher.App.csproj` (yorum), `src/Launcher.Core` (Settings, ThemeCatalog, Json), testler, README, Store metinleri. SAP'a yazma yok.

## A. BRIEF (işe başlamadan önce)
### Özet
Kenara yapışık (docked) stilde, panel kısayolla açılırken bir an tam açık görünüyor, kayboluyor ve aşağıdan
tekrar kayarak açılıyor. Bu düzeltilecek. Ayrıca tema listesindeki "Caelestia (Dark Glass)" adı "Dark Glass"
olacak.

### Arka plan
- **Docked stil:** `settings.panelStyle: "docked"`. Pencere saydam (layered) oluşturulur ve alt kenarda açılırken
  içerik kısa bir kayma animasyonuyla (slide-in) yerine gelir.
- **Layered pencere:** Windows bu tür pencerenin en son çizilen görüntüsünü saklar ve pencere yeniden
  gösterildiğinde, WPF yeni kareyi çizmeden önce bu eski görüntüyü bir an ekrana basar.
- **Kök neden (koddan):** `HideLauncher` pencereyi hemen gizliyor; son çizilen kare tam açık panel. Sonraki
  açılışta önce bu eski kare görünüyor (1. açılma), ardından içerik animasyon için ekran dışına alınıyor
  (kaybolma) ve kayarak geliyor (2. açılma).
- **Tema yapılandırma adı:** config'de `"theme": "caelestiaDarkGlass"`. 0.3.0 Store'da yayında olduğu için bu
  değeri yazmış kullanıcılar var; eski ad okunmaya devam etmeli.

### Kapsam / Kapsam dışı
Kapsam: docked stilde gizleme sırası; tema görünen adı ve config adı (`darkGlass`, eski `caelestiaDarkGlass`
de okunur); README, test, Store metni (tema listesi).
Kapsam dışı: floating stil (değişmiyor), 0.3.0 "Yenilikler" metni (zaten gönderildi), sürüm/paket.

### Plan
- [x] 1. Docked gizleme: içeriği saydam yap, boş kare ekrana ulaştıktan sonra (iki kare sonra) pencereyi gizle;
  bu arada gelen açma isteği bekleyen gizlemeyi iptal etsin.
- [x] 2. Tema: enum `DarkGlass`, görünen ad "Dark Glass", config adı `darkGlass`; eski `caelestiaDarkGlass`
  okunurken `darkGlass`'a eşlensin.
- [x] 3. Test, README, Store metni güncelle.
- [x] 4. Derle, testleri çalıştır.

### Riskler ve varsayımlar
- Gizleme ~30 ms gecikir. Bu sürede pencere tamamen saydam olduğu için tıklamalar alttaki pencereye geçer.
- Kök neden koddan ve layered pencerenin bilinen davranışından çıkarıldı. Ekran kaydıyla yeniden üretme denendi
  ama masaüstünde aynı anda başka bir oturum çalıştığı için ölçüm güvenilir olmadı.

### ⚠ Senden beklenenler (başlamadan önce)
Senden beklenen bir şey yok.

### Kararlar
- 2026-10-15 (ek istek): Alta yapışık modda listenin ilk satırı yuvarlak üst köşeye denk geliyor; seçili satırın
  renkli vurgu çizgisi yarım görünüyor. Kullanıcı: "olmazsa search en üstte çıksın". Seçenekler: (1) listenin üstüne
  köşe yarıçapı kadar boşluk, (2) arama kutusunu üste almak. Seçilen: (1), çünkü arama kutusunun çerçeveye yakın
  durması Caelestia düzeninin istenen özelliği; (2) yedek çözüm olarak kalıyor.
- 2026-10-15: Eski config adı (`caelestiaDarkGlass`) okunmaya devam edecek; kaydedilince `darkGlass` yazılır.
  Neden: Store 0.3.0 kullanıcılarının config'i bozulmasın (bilinmeyen tema değeri config'i okunamaz yapar).

## B. RAPOR (iş bittikten sonra)
### Sonuç
Docked stilde gizleme, pencereyi önce boşaltıp sonra gizleyecek şekilde değiştirildi; böylece sonraki açılışta
eski panel görüntüsü bir an görünmemeli. Tema artık "Dark Glass" adıyla listeleniyor; eski config değeri de okunuyor.

### Yapılanlar
- `MainWindow.HideWindow`: docked stilde içerik saydam yapılıyor ve pencere iki kare sonra gizleniyor (WPF'in
  `CompositionTarget.Rendering` olayıyla sayılıyor). Floating stil eskisi gibi hemen gizleniyor.
- Bekleyen gizleme sırasında gelen açma isteği (kısayol, tepsi, ikinci başlatma) gizlemeyi iptal edip paneli
  açıyor; bu sürede gelen ikinci gizleme isteği yok sayılıyor.
- Tema: enum `Theme.DarkGlass`, görünen ad "Dark Glass", config adı `darkGlass`. Yeni `JsonEnumAliasAttribute`
  ile `caelestiaDarkGlass` okunurken `DarkGlass`'a eşleniyor; kaydedilince `darkGlass` yazılıyor.
- Alta yapışık + liste sayfası: panelin üstüne 28 DIP (köşe yarıçapı) iç boşluk eklendi; ilk satır yuvarlak
  köşenin altından başlıyor. Bu düzende içerik kırpması (clip) kaldırıldı, çünkü hiçbir şey köşeye ulaşmıyor ve
  alt kenar zaten düz. Diğer sayfalar (ayarlar, düzenleyici) ve diğer konumlar değişmedi.
- README (tema açıklaması, örnek config, yazı tipi notu) ve Store metnindeki tema listesi (en + tr) güncellendi.

### Nasıl doğrulandı
- `dotnet build` (Debug): 0 hata, 0 uyarı.
- `dotnet test`: 801 testin tamamı geçti. Yeni test: `caelestiaDarkGlass` içeren config `DarkGlass` olarak okunuyor;
  `darkGlass` adıyla yazılıp geri okunuyor.
- **Doğrulanmadı:** Çift açılmanın gerçekten kaybolduğu ekranda görülmedi. Ekran kaydıyla ölçüm denendi ama aynı
  anda masaüstünde başka bir oturum tuş basıyordu ve yakalanan karelerde başka pencereler değişiyordu; sonuç
  güvenilir olmadı. İlk satır düzeltmesi için alınan ekran görüntüsünde de test kopyası öne gelemediği için panel
  görünmedi; bu düzeltme de ekranda doğrulanmadı. Kök neden koddan ve katmanlı pencerenin bilinen davranışından çıkarıldı.

### Değişen nesneler / dosyalar
- `src/Launcher.App/Views/MainWindow.xaml.cs`, `src/Launcher.App/Launcher.App.csproj` (yalnızca yorum)
- `src/Launcher.Core/Json/JsonEnumAliasAttribute.cs` (yeni), `src/Launcher.Core/Json/JsonCamelCaseEnumConverter.cs`
- `src/Launcher.Core/Model/Settings.cs`, `src/Launcher.Core/Theming/ThemeCatalog.cs`
- `tests/Launcher.Core.Tests/ThemeCatalogTests.cs`, `README.md`, `tasks/store-submission.md`

### Yapılmayanlar ve açık kalanlar
- 0.3.0 "Yenilikler" metnindeki "Caelestia (Dark Glass)" satırı değiştirilmedi: o metin Store'a zaten gönderildi.
- Commit, sürüm ve paket yok: kullanıcının kararı.

### ⚠ Senden beklenenler (şimdi)
| # | Eylem | Neden | Aciliyet | İşi durduruyor mu? (Evet/Hayır) |
|---|---|---|---|---|
| 1 | Debug derlemeyle kısayolu birkaç kez açıp kapat; ilk satırın ve vurgu çizgisinin tam göründüğüne bak | İki düzeltme de ekranda doğrulanamadı | Yüksek | Hayır |
| 3 | Üstteki boşluk göze fazla gelirse söyle | O durumda arama kutusunu üste alma seçeneğine geçilir | Düşük | Hayır |
| 2 | Commit edilsin ve 0.3.1 çıkarılsın mı, karar ver | Düzeltme ancak yeni paketle kullanıcılara ulaşır | Orta | Hayır |

### Sonraki oturum için not
Ekran yakalamada `CopyFromScreen` katmanlı pencereleri yakalamaz; `BitBlt` + `CAPTUREBLT` (0x40CC0020) gerekir.
Store 0.3.0 kullanıcılarının config'inde `caelestiaDarkGlass` olabilir; alias kaldırılmamalı.

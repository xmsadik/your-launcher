# Popüler geliştirici temaları ve arka plan opaklığı

**Durum:** TAMAMLANDI
**Tarih:** 2026-10-14 (son güncelleme: 2026-10-14)
**Etkilenen yer:** `src/Launcher.Core` (model, tema kataloğu), `src/Launcher.App` (tema servisi, ayarlar sayfası, tema XAML dosyaları), testler, README/örnek config. SAP'a yazma yok.

## A. BRIEF (işe başlamadan önce)
### Özet
Ayarlar sayfasındaki tema listesine Catppuccin, Tokyo Night, Dracula, Nord, Gruvbox, One Dark, Solarized,
Monokai, GitHub ve Rosé Pine gibi bilinen geliştirici temaları eklenecek; mevcut "System", "Windows Light"
ve "Windows Dark" seçenekleri listede kalacak. Ayrıca panelin arka planının ne kadar saydam olacağını
belirleyen bir opaklık (opacity) ayarı eklenecek.

### Arka plan
- Uygulamadaki her renk, 12 sabit anahtardan biriyle (`PanelBackgroundBrush`, `ForegroundBrush`,
  `AccentBrush` vb.) `DynamicResource` üzerinden okunuyor. `ThemeService` bugün `Themes/Dark.xaml` veya
  `Themes/Light.xaml` dosyasını uygulama kaynaklarına takıp çıkarıyor; bu değişim tek başına tüm paneli
  yeniden renklendiriyor.
- İki XAML dosyasında renklerin yanında ComboBox şablonu da kopya olarak duruyor. 20 tema için 20 kopya
  şablon tutmak yerine şablon tek bir `Themes/Controls.xaml` dosyasına taşınacak, renkler ise Core
  projesinde veri olarak (`ThemeCatalog`) tanımlanacak. Böylece renk paletleri birim testlerle (her temanın
  tüm renkleri var mı, metin okunabilir mi) kontrol edilebilecek.
- **Backdrop:** Windows 11 22H2 ve sonrasında panelin arkasında bulanık Acrylic/Mica efekti var. Panel
  zemini bu efektin üstünde bugün %90 opak bir renk katmanı (tint) olarak çiziliyor. Opaklık ayarı bu
  katmanın yüzdesini belirleyecek; varsayılan 90 olduğu için mevcut görünüm aynen kalır.
- **WCAG kontrast oranı:** metin rengi ile zemin rengi arasındaki okunabilirlik ölçüsü (1-21 arası).
  Normal metin için 4.5 kabul gören alt sınırdır.

### Kapsam / Kapsam dışı
Kapsam:
- `Theme` enum'una yeni temalar (config.json'da `"theme": "catppuccinMocha"` biçiminde).
- `settings.opacity` (yüzde, 40-100, varsayılan 90).
- Ayarlar sayfasında tema listesi ve "Background opacity" alanı.
- Testler, README ve `config.example.json` içindeki ayar satırları.

Kapsam dışı:
- Tema seçerken canlı önizleme (mevcut ayarlar sayfası "Kaydedince uygula" düzeninde; bu korunuyor).
- Kullanıcının kendi renklerini tanımladığı özel tema.

### Plan
- [x] 1. Core: `Theme` enum'unu genişlet, `Settings.Opacity` ekle, `ThemeCatalog` + `ThemePalette` yaz.
- [x] 2. App: ComboBox şablonunu `Themes/Controls.xaml` dosyasına taşı, `Dark.xaml`/`Light.xaml` sil,
  `ThemeService` paleti koddan fırça olarak üretsin, opaklığı tint katmanına uygulasın.
- [x] 3. Ayarlar sayfası: tema listesi katalogdan dolsun, opaklık alanı eklensin (↑/↓ ile 5'er adım).
- [x] 4. Testler: katalog bütünlüğü, kontrast, config okuma/yazma (yeni tema adı, opaklık varsayılanı).
- [x] 5. Derleme + tüm testler; README ve örnek config güncellemesi.

### Riskler ve varsayımlar
- Opaklık yalnızca backdrop'un çalıştığı sistemlerde (Windows 11 22H2+) görünür etki yapar. Windows 10'da
  pencere saydamlığı WPF'te `AllowsTransparency` gerektiriyor ve bu da backdrop'u bozuyor; bu yüzden orada
  panel opak kalır. Bu davranış README'de belirtilecek.
- Bazı temaların orijinal "yorum" renkleri açık zeminde okunmaz (ör. Solarized Light). Bu durumda renk,
  testin istediği kontrasta ulaşacak kadar koyulaştırılacak; aksi halde orijinal değerler korunacak.
- Eski bir sürüme geri dönülürse yeni tema adını içeren config okunamaz (bilinmeyen enum değeri). Bu,
  mevcut tüm enum ayarları için zaten geçerli olan davranış.

### ⚠ Senden beklenenler (başlamadan önce)
| # | Karar / eylem | Neden gerekli | İşi durduruyor mu? (Evet/Hayır) | Cevap |
|---|---|---|---|---|
| 1 | Opaklığın sadece panel zeminine uygulanması (yazılar opak kalır, Windows Terminal'deki gibi) | Tüm pencereyi saydam yapmak yazıları da soluklaştırır ve backdrop ile uyumsuz | Hayır | Varsayılan olarak bu yol izleniyor |

### Kararlar
- 2026-10-14: Opaklık, Windows Terminal'deki "background opacity" gibi yalnızca zemine uygulanıyor.
  Seçenekler: (a) yalnızca zemin, (b) tüm pencere. (a) seçildi; nedeni yukarıdaki risk maddesi.

## B. RAPOR (iş bittikten sonra)
### Sonuç
Ayarlar sayfasında artık 21 seçenekli bir tema listesi (System, Windows Light, Windows Dark ve 18 geliştirici
teması) ve 40-100 arası "Background opacity" alanı var. Kod derleniyor ve tüm testler geçiyor; kullanıcı görsel
kontrolü yaptı.

### Yapılanlar
- `Theme` enum'una 18 yeni tema eklendi. Eski değerler (`system`, `light`, `dark`) aynı sırada ve aynı
  adla kaldığı için mevcut config dosyaları değişmeden okunuyor.
- Renk paletleri `Launcher.Core/Theming/ThemeCatalog.cs` dosyasına veri olarak taşındı. Windows Light/Dark
  değerleri eski XAML dosyalarındakiyle birebir aynı, yani bu iki temanın görünümü değişmedi.
- Bazı temalarda seçili satırın rengi, okunabilirlik sınırına ulaşmak için resmi paletten biraz farklı
  tutuldu (Tokyo Night Day, Dracula, One Dark, Monokai, Gruvbox Light, Solarized, Catppuccin Latte). Bunun
  nedeni, orijinal seçili satır renklerinin üzerinde yolların ve uyarıların zor okunmasıydı.
- `Themes/Dark.xaml` ve `Themes/Light.xaml` silindi. İçlerindeki kopya ComboBox şablonu tek bir
  `Themes/Controls.xaml` dosyasına taşındı ve uygulama açılışında bir kez yükleniyor. Açılır listeye yükseklik
  sınırı eklendi; 21 tema ekranı boydan boya kaplamasın diye.
- `ThemeService` artık paletten fırçaları kodda üretiyor. Opaklık değeri, Windows 11 backdrop'unun üstündeki
  zemin katmanının saydamlığı olarak uygulanıyor. Varsayılan 90, eski sabit değerle (`E6` = %90) aynı.
- `settings.opacity` ayarı eklendi. Kaydederken 40-100 aralığına sıkıştırılıyor; kutuda ↑/↓ tuşları 5'er
  artırıp azaltıyor.
- README'deki ayar örneği ve `config.example.json` güncellendi.

### Nasıl doğrulandı
- `dotnet build`: uyarısız başarılı.
- `dotnet test`: 384 testin 384'ü geçti. Yeni `ThemeCatalogTests` şunları kontrol ediyor: System dışındaki her
  temanın tam bir paleti var; tüm renkler geçerli hex; ana metin hem zeminde hem seçili satırda en az 4.5,
  ikincil/vurgu/uyarı renkleri en az 3.0 kontrast oranında; yeni tema adı ve opaklık config'e yazılıp geri
  okunabiliyor; opaklığı olmayan eski config 90 ile açılıyor.
- Duman testi: geliştirme sürümü geçici bir config klasörüyle (`catppuccinMocha`, opaklık 70, farklı kısayol
  tuşu) başlatıldı, 6 saniye sonra hâlâ çalışıyordu ve hata günlüğü oluşmadı. Bu, yeni kaynak sözlüğünün ve
  `Controls.xaml` birleştirmesinin açılışta hata vermediğini gösteriyor.
- Görsel kontrol: kullanıcı 2026-10-14'te geliştirme sürümünü ayrı bir config kopyasıyla çalıştırıp temaları,
  açılır listeyi ve opaklığı denedi; "herşey çalışıyor" dedi.

### Değişen nesneler / dosyalar
| Dosya | Değişiklik |
|---|---|
| `src/Launcher.Core/Model/Settings.cs` | Yeni temalar, `Opacity` ayarı |
| `src/Launcher.Core/Theming/ThemeCatalog.cs` | Yeni: 20 palet ve ayar listesi |
| `src/Launcher.App/Services/ThemeService.cs` | Paletten fırça üretimi, opaklık |
| `src/Launcher.App/Themes/Controls.xaml` | Eski `Dark.xaml`'dan türetilen ortak ComboBox şablonu |
| `src/Launcher.App/Themes/Light.xaml` | Silindi |
| `src/Launcher.App/App.xaml`, `App.xaml.cs`, `Views/MainWindow.xaml.cs` | Yeni `Apply` imzası |
| `src/Launcher.App/Views/SettingsView.xaml(.cs)`, `ViewModels/SettingsViewModel.cs` | Tema listesi, opaklık alanı |
| `tests/Launcher.Core.Tests/ThemeCatalogTests.cs` | Yeni testler |
| `README.md`, `config.example.json` | Ayar dokümantasyonu |

### Yapılmayanlar ve açık kalanlar
- Canlı önizleme yok: tema, mevcut ayarlar düzeni gereği Enter ile kaydedince uygulanıyor.
- Windows 10'da opaklık görünür bir etki yapmıyor (backdrop yok). Ayarlar sayfasında bunu söyleyen bir not
  yok, yalnızca README'de yazıyor.
- Commit atılmadı; değişiklikler çalışma kopyasında duruyor.

### ⚠ Senden beklenenler (şimdi)
| # | Eylem | Neden | Aciliyet | İşi durduruyor mu? (Evet/Hayır) |
|---|---|---|---|---|
| 1 | Commit atılsın mı, söylemek | Commit'i ancak istendiğinde atıyorum | Düşük | Hayır |

### Sonraki oturum için not
Yeni tema eklemek için `Theme` enum'una bir üye ve `ThemeCatalog.Palettes` listesine bir palet eklemek
yeterli; `ThemeCatalogTests` eksik paleti ve okunmayan renkleri yakalar.

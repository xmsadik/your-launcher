# Yeni özellikler: önizleme, parametreli öğeler, sistem komutları, pencere geçişi, hesap makinesi, dosya araması, eklentiler ve Türkçe arayüz

**Durum:** DEVAM EDİYOR
**Tarih:** 2026-10-14 (son güncelleme: 2026-10-14)
**Etkilenen yer:** `src/Launcher.Core`, `src/Launcher.App`, `tests/Launcher.Core.Tests`, `README.md`, `launcher-spec.md`. SAP'a yazma yok.

## A. BRIEF (işe başlamadan önce)
### Özet
Kullanıcının istediği 11 özellik, birbirinin üstüne oturan beş fazda eklenecek. Önce aramaya "dinamik sonuç"
altyapısı kurulacak, çünkü hesap makinesi, sistem komutları, pencere geçişi, dosya araması ve eklentiler
config'teki öğelerden gelmeyen sonuçlar üretiyor. Her faz ayrı derlenip test edilecek ve ayrı commit olacak.

### Arka plan
- **Öğe (node):** config.json'daki ağacın bir elemanı. Türleri: klasör, uygulama (app), yol (path), komut
  (command), URL, ayırıcı.
- **Arama bugün nasıl çalışıyor:** `SearchEngine`, ağacı düzleştirilmiş bir dizinde (`FlatIndex`) bulanık
  eşleşmeyle (fuzzy) puanlıyor. Sonuçlar her zaman bir öğe; liste satırı (`ListItemViewModel`) zorunlu
  olarak bir `Node` taşıyor. Arama UI thread'inde, senkron çalışıyor.
- **Dinamik sonuç (provider result):** config'te karşılığı olmayan, arama anında üretilen satır. Örnek:
  "12*4 = 48" satırı ya da açık bir pencere. Bunun için `IResultProvider` adında bir arayüz ve sonuçları
  öğe sonuçlarıyla birleştiren bir katman eklenecek. Hızlı provider'lar (hesap makinesi, sistem komutları,
  pencereler) senkron, yavaş olanlar (dosya araması, eklentiler) arka planda çalışıp listeye sonradan
  eklenecek; eski aramaya ait geç gelen sonuçlar atılacak (mevcut "generation" sayacı ile).
- **Spesifikasyon sınırı:** `launcher-spec.md` §2 "Hedef Olmayanlar" bölümü hesap makinesi, web araması,
  dosya sistemi indeksleme ve eklenti sistemini kapsam dışı sayıyor. Kullanıcı 2026-10-14'te bunların
  eklenmesini istedi; bölüm buna göre güncellenecek. Eklenti **mağazası** kapsam dışı kalmaya devam ediyor.
- **Store sürümü:** Store'da 0.1.1 yayında. Bu iş yeni bir sürüm yayımlamayı kapsamıyor.

### Kapsam / Kapsam dışı
Kapsam (önceki mesajdaki numaralarla):

| # | Özellik | Kısa tanım |
|---|---|---|
| 1 | Temada canlı önizleme | Ayarlarda tema/opaklık değişince panel anında boyanır; Esc eski temaya döner |
| 2 | Parametreli öğeler | URL/komut içinde `{query}`; "jira ABC-123" → şablonla açılır (URL'de kodlanarak) |
| 3 | Hazır sistem komutları | Kilitle, uyku, hazırda beklet, oturumu kapat, yeniden başlat, kapat, geri dönüşüm kutusunu boşalt; tehlikeliler onaylı |
| 4 | Öğeye özel kısayol tuşu | Öğe, paneli açmadan kendi global kısayoluyla başlar; editörde alan |
| 5 | Windows vurgu rengi | Ayar açıkken temanın vurgu rengi yerine Windows'unki; Windows'ta değişince canlı güncellenir |
| 6 | Pencere geçişi | Açık pencereler aramada listelenir, Enter o pencereye geçer |
| 7 | Hesap makinesi | Matematik ifadesi yazınca sonuç satırı; Enter panoya kopyalar; hex/bin desteği |
| 8 | Başlat menüsünden içe aktarma | Masaüstü uygulamaları (.lnk) ve Store uygulamaları seçilerek bir klasöre eklenir; tek seferlik |
| 9 | Türkçe arayüz | Tüm arayüz metinleri kaynak dosyalarına; dil ayarı (Windows'u izle / English / Türkçe) |
| 10 | Dosya araması | Aramaya dosya/klasör sonuçları (arka uç: karar 1) |
| 11 | Eklenti sistemi | Kullanıcının kendi eklentileri (model: karar 2) |

Kapsam dışı:
- Eklenti mağazası, eklentilerin otomatik indirilmesi/güncellenmesi.
- Store'a yeni sürüm gönderimi ve Store sayfasının Türkçeleştirilmesi.
- Bulut senkronizasyonu.

### Plan
Her fazın sonunda: derleme, tüm testler, bağımsız kod incelemesi, commit. Uygulama (kodlama) Sonnet alt
ajanlarına, plan eleştirisi ve diff incelemesi ayrı bir ajana verilir; son doğruluk kararı bende.

- [x] **Faz 0 — Altyapı:** `IResultProvider` arayüzü, sonuç birleştirme ve sıralama kuralları, senkron ve
  arka plan provider desteği, liste satırının öğe dışı sonucu gösterebilmesi. `launcher-spec.md` §2 güncellemesi.
- [x] **Faz 1 — Hızlı kazançlar:** #1 canlı önizleme, #5 Windows vurgu rengi, #3 sistem komutları,
  #7 hesap makinesi (Core'da kendi ifade ayrıştırıcısı; `eval` yok).
- [x] **Faz 2 — Öğe özellikleri:** #2 parametreli öğeler, #4 öğeye özel kısayol tuşu (çakışma ve kayıt
  hatası ana kısayoldaki gibi kullanıcıya gösterilir).
- [x] **Faz 3 — Windows entegrasyonu:** #6 pencere geçişi, #8 Başlat menüsünden içe aktarma (bookmark
  içe aktarmayla aynı düzen: tekrar içe aktarınca aynı klasörü günceller).
- [ ] **Faz 4 — Büyük işler:** #10 dosya araması, #11 eklenti sistemi.
- [ ] **Faz 5 — Türkçe arayüz:** #9. En sona konuldu çünkü önceki fazların ekleyeceği yeni metinler de
  çevrilecek; erken yapılırsa her fazda iki dilde metin tutmak gerekir.
- [ ] Faz sonu: README güncellemesi, rapor.

### Tasarım kuralları (plan eleştirisinden sonra, 2026-10-14)
Plan bağımsız bir ajana eleştirtildi; aşağıdaki kurallar o eleştiriden çıktı ve tüm fazlar için bağlayıcı.
1. **Öğe dışı satırlar:** `ListItemViewModel.Node` boş olabilir hale gelir; öğe dışı satır bir
   `ProviderResult` (başlık, alt satır, ikon, çalıştırma eylemi) taşır. Satırın `CanEdit`, `CanLaunch`,
   `CanShowInFolder` özellikleri olur. F2, Del, Ctrl+I, Ctrl+X/V, Ctrl+D, Ctrl+Enter, sağ tık menüsü, çift tık
   ve sürükleme bu özelliklere bakar. Neden: geçici sahte öğe türleri kopyalama/taşıma yoluyla config'e
   sızabilir ve config okuyucusu bilinmeyen türde hata verir.
2. **Kullanım istatistiği:** Provider sonuçlarının kalıcı kimliği olur (ör. `sys:lock`). Kullanım temizliği
   (`UsageService.Prune`) iki nokta içeren kimlikleri silmez. Neden: yoksa sistem komutları sık kullanıma göre
   hiç öne çıkamaz.
3. **Sıralama:** Sabit katman sırası, puanlar karıştırılmaz: hesap makinesi sonucu en üstte, sonra öğeler,
   sonra sistem komutları, sonra pencereler, en altta arka plan sonuçları (dosyalar, eklentiler). Arka plan
   sonuçları listenin sonuna eklenir, sırayı değiştirmez ve seçimi kaydırmaz. Gecikme (debounce) yalnızca arka
   plan provider'larında olur; öğe sonuçları bugünkü gibi anında gelir.
4. **Metinler:** Faz 0'da tek bir metin erişim noktası (`Strings`) kurulur ve sonraki fazlar yeni metinleri
   oraya yazar. Faz 5 yalnızca Türkçe çevirileri ekler ve mevcut sabit metinleri taşır. Release yapılandırmasındaki
   `SatelliteResourceLanguages=en` satırı `en;tr` yapılır; yoksa Türkçe dosyalar paketten sessizce düşer.
5. **Öğeye özel kısayollar:** `HotkeyService` birden çok kimlik yönetir. Config yeniden yüklenince, içe
   aktarmada ve silmede kayıtlar yeniden eşitlenir; Ctrl+D ile çoğaltılan öğenin kısayolu kopyalanmaz.
6. **`{query}` yerleştirme:** Ortam değişkenleri açıldıktan sonra yapılır (sorgudaki `%PATH%` açılmasın).
   URL'de `Uri.EscapeDataString`, cmd'de tırnak/özel karakter kaçışı, pwsh'te mevcut `-EncodedCommand` yolu.
   Arama "anahtar kelime + boşluk + metin" biçimini ayrıca tanır.
7. **Pencere geçişi:** Hedef pencere, panel gizlenmeden önce öne getirilir (öndeki pencere biz olduğumuz için
   Windows izin verir); simge durumundaki pencere geri yüklenir; görünmez (cloaked) pencereler listelenmez.
8. **Store paketi:** Yeni bir paket yetkisi (capability) eklenmez. Store uygulamaları `shell:AppsFolder`
   üzerinden listelenir ve açılır; bu yetki gerektirmiyor.
9. **Dosya araması:** Windows Search her zaman var; Everything çalışıyorsa (çalışma anında tespit) o kullanılır.
   En fazla 20 sonuç.
10. **Pano:** Panoya kopyalama başka uygulama panoyu tutuyorsa hata verebilir; kısa yeniden denemeyle sarılır.

### Riskler ve varsayımlar
- **Bellek ve açılış hızı:** Spesifikasyon hedefi kısayoldan sonra 100 ms altında görünmek. Pencere listesi ve
  dosya araması panel açılırken değil, yazarken çalışacak; dosya araması kısa bir gecikmeyle (debounce)
  tetiklenecek.
- **Store paketi kısıtları:** Paketli uygulamada Başlat menüsü ve pencere listesi okunabiliyor, ama bazı
  Win32 çağrıları (ör. başka pencereyi öne getirme) Windows'un "ön plan kilidi" kuralına takılabilir. Panel o
  anda öndeki pencere olduğu için genelde izin var; olmadığı durumda pencere görev çubuğunda yanıp söner.
- **Eklentiler kullanıcı kodu çalıştırır.** Uygulama yalnızca kullanıcının kendi koyduğu eklenti klasörünü
  okur, hiçbir şey indirmez. Eklenti hatası veya zaman aşımı uygulamayı çökertmemeli (ayrı süreç ise doğal).
- **Sistem komutları:** Yeniden başlat/kapat/oturum kapat her zaman onay ister, varsayılan olarak açık.
- **Testler:** Core'daki saf mantık (ifade ayrıştırıcı, `{query}` yerleştirme ve kodlama, sonuç birleştirme,
  eklenti çıktısı ayrıştırma) birim testle doğrulanır. Win32'ye dayanan kısımlar (pencere listesi, kısayollar,
  dosya araması) yalnızca derleme ve kısa duman testiyle doğrulanabilir; asıl kontrol kullanıcının elle
  testinde.

### ⚠ Senden beklenenler (başlamadan önce)
| # | Karar / eylem | Neden gerekli | İşi durduruyor mu? (Evet/Hayır) | Cevap |
|---|---|---|---|---|
| 1 | Dosya aramasının arka ucu | Windows Search her makinede var ama yavaş; Everything çok hızlı ama ayrıca kurulu olmalı | Evet (Faz 4) | İkisi birden: Everything çalışıyorsa o, yoksa Windows Search |
| 2 | Eklenti modeli | Mimariyi belirliyor; sonradan değiştirmek pahalı | Evet (Faz 4) | Ayrı süreç, JSON (stdin/stdout) |
| 3 | Pencere ve dosya sonuçlarının aramada nasıl görüneceği | Karışık mı, önekle mi; günlük kullanım hissini belirliyor | Hayır (önerilen varsayılanla başlanır) | Pencereler karışık (öğelerin altında), dosyalar `f ` önekiyle |
| 4 | Türkçe arayüzde dil seçimi | Varsayılan davranış | Hayır (önerilen varsayılanla başlanır) | Windows dilini izle, ayardan English/Türkçe sabitlenebilir |

### Kararlar
- 2026-10-14: Kullanıcı önerilen 11 özelliğin tamamının eklenmesini istedi; temalar commit'i (`671c7f5`) önce atıldı.
- 2026-10-14: Faz sırası: altyapı → hızlı kazançlar → öğe özellikleri → Windows entegrasyonu → büyük işler →
  Türkçe arayüz. Türkçe arayüzün en sona konma nedeni: diğer fazların metinleri de çevrilecek.

- 2026-10-14: Kullanıcı dört kararın hepsinde önerilen seçeneği seçti (tablodaki Cevap sütunu).

## B. RAPOR (iş bittikten sonra)

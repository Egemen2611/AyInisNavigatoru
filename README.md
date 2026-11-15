[README.md](https://github.com/user-attachments/files/34875602/README.md)
# 🌕 Ay İniş Navigatörü / Lunar Landing Navigator

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)
![Platform](https://img.shields.io/badge/platform-Windows-0078D6?logo=windows)
![Language](https://img.shields.io/badge/C%23-WinForms-239120?logo=csharp)
![NASA Space Apps](https://img.shields.io/badge/NASA%20Space%20Apps-Eski%C5%9Fehir-0B3D91)

**NASA Space Apps Challenge – Eskişehir** için geliştirilmiş, Ay yüzeyinde seçilen bir noktanın inişe ne kadar uygun olduğunu **NASA Lunar Reconnaissance Orbiter (LRO)** uydusunun gerçek ölçümleriyle **0–100** arasında puanlayan bir Windows masaüstü uygulaması.

> 🇬🇧 *A Windows desktop app that scores how suitable any point on the Moon is for landing (0–100), using real measurements from NASA's Lunar Reconnaissance Orbiter (LOLA altimeter + Diviner radiometer). Fully bilingual: Turkish / English.*

---

## İçindekiler

- [Özellikler](#-özellikler)
- [Hızlı başlangıç](#-hızlı-başlangıç)
- [Kaynaktan derleme](#-kaynaktan-derleme)
- [Nasıl kullanılır?](#-nasıl-kullanılır)
- [Kullanılan veriler](#-kullanılan-veriler)
- [Puanlama yöntemi](#-puanlama-yöntemi)
- [Görev profilleri](#-görev-profilleri)
- [Proje yapısı](#-proje-yapısı)
- [Sınırlamalar](#-sınırlamalar)
- [Kaynaklar](#-kaynaklar)

---

## ✨ Özellikler

- **🌍 3B Ay küresi** — Fareyle döndürün, istediğiniz noktaya tıklayın; o nokta anında analiz edilir.
- **📊 6 kriterli puanlama** — Eğim, krater/taş yoğunluğu, güneş ışığı, sıcaklık, Dünya ile iletişim ve arazi yapısı.
- **🎯 5 görev profili** — Dengeli, kısa robotik (CLPS), insanlı (Artemis), kalıcı üs, gezgin (rover). Her profilin kendi ağırlıkları ve kritik sınırları var.
- **🌡 Küresel ısı haritası** — Ay'ın tamamı 1440 × 720 = **1.036.800 hücreye** (0.25°) bölünüp puanlanır; 7 farklı katman gösterilebilir.
- **🛬 Tarihî inişler** — Luna, Surveyor, Apollo 11–17, Chang'e, Chandrayaan-3, SLIM, IM-1, Blue Ghost dahil **28 başarılı yumuşak iniş** küre üzerinde işaretli. Her raporda en yakın gerçek iniş noktası yazar.
- **☀ Gerçek güneş** — Ay şu an nasıl aydınlanıyorsa öyle görünür; gece-gündüz çizgisi gerçektir.
- **🕒 İniş penceresi önerisi** — Güneşin 7°–20° yükseklikte olduğu bir sonraki sabahı hesaplar (Apollo inişleri ~10°).
- **⚖ Nokta karşılaştırma** — Analiz edilen tüm noktalar tek tabloda, sıralanabilir.
- **📄 Tarayıcı raporu** — Ayrıntılı HTML rapor; "Yazdır / PDF olarak kaydet" düğmesiyle. Her analiz ayrıca `raporlar/` klasörüne JSON olarak kaydedilir.
- **🌐 İki dil** — Tek tuşla Türkçe ↔ English. Seçim `dil.txt` dosyasında hatırlanır.
- **⚡ Akıllı veri indirme** — Gigabaytlık NASA dosyalarının tamamı indirilmez; HTTP *Range* istekleriyle yalnızca gereken satırlar okunur ve `veri_onbellek/` klasöründe saklanır.

---

## 🚀 Hızlı başlangıç

Derlemeyle uğraşmadan çalıştırmak için:

1. [**.NET 10 Desktop Runtime**](https://dotnet.microsoft.com/download/dotnet/10.0)'ı kurun (Windows x64).
2. [Releases](../../releases) sayfasından `AyInisNavigatoru.exe` dosyasını indirin.
3. Çift tıklayıp çalıştırın. İnternet bağlantısı gerekir (veriler NASA PDS sunucularından canlı çekilir).

> Bağımlılıklar [Costura.Fody](https://github.com/Fody/Costura) ile exe'nin içine gömülüdür; ek DLL gerekmez.

---

## 🛠 Kaynaktan derleme

### Gereksinimler

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- (İsteğe bağlı) **Visual Studio 2026** — *".NET desktop development"* iş yüküyle

### Visual Studio ile

1. Depoyu klonlayın:
   ```bash
   git clone https://github.com/<kullanici-adi>/AyInisNavigatoru.git
   ```
2. `AyInisNavigatoru.slnx` (veya `AyInisNavigatoru.csproj`) dosyasını açın.
3. **F5** ile çalıştırın.

### Komut satırıyla

```bash
cd AyInisNavigatoru
dotnet run
```

Yayınlanabilir tek exe üretmek için:

```bash
dotnet publish -c Release -r win-x64 --self-contained false
```

---

## 📖 Nasıl kullanılır?

1. Üst çubuktan bir **görev profili** seçin.
2. Ay'ı fareyle döndürün ve bir noktaya **tıklayın** (ya da "Git" listesinden hazır bir nokta seçin). Sağdaki panelde rapor oluşur.
3. **🌡 Isı haritası** ile Ay'ın tamamının uygunluk haritasını açın; katman menüsünden kriter değiştirin.
4. Birkaç nokta analiz ettikten sonra **⚖ Karşılaştır** ve **📄 Rapor** ile sonuçları inceleyin.
5. **⚙ Ayarlar**'dan iniş elipsi yarıçapını değiştirebilirsiniz (varsayılan 2000 m).

Takıldığınızda uygulamadaki **❓ Nasıl kullanılır?** düğmesine basın.

```
┌───────────────────────────────────────────────────────────────────────────────┐
│ Görev:[▼] Git:[▼] │ 🌡 Isı haritası [katman ▼] │ 🛬 Tarihî inişler ☀ Gerçek güneş │ ⚖ 📄 ⚙ ❓ ⟲ 🌐 │
├───────────────────────────────────────────────────────────────────────────────┤
│ Açıklama şeridi                                                                 │
├──────────────────────────────────────────────────┬────────────────────────────┤
│                 3B AY KÜRESİ                       │          RAPOR             │
├──────────────────────────────────────────────────┴────────────────────────────┤
│ İmleç konumu (+ ısı haritası değerleri) · ilerleme · durum · 📜 Günlük              │
└───────────────────────────────────────────────────────────────────────────────┘
```

---

## 🛰 Kullanılan veriler

| Veri | Alet | Çözünürlük | Kullanım |
|---|---|---|---|
| `LDEM_128` | LRO **LOLA** lazer altimetresi | 128 piksel/derece (~237 m) | Tekli analiz: eğim, krater, pürüzlülük |
| `LDEM_64` | LRO **LOLA** | 64 piksel/derece (~474 m) | 30 km'lik ufuk profili |
| `LDEM_16`, `LDEM_4` | LRO **LOLA** | 16 / 4 piksel/derece | Küresel ısı haritası ve küre dokusu |
| `dgdr_ra_avg_cyl_128` / `_032` | LRO **Diviner** radyometresi | 128 / 32 piksel/derece | Kaya yoğunluğu |
| `dgdr_st_avg_cyl_032` | LRO **Diviner** | 32 piksel/derece | Gece regolit sıcaklığı |

- LOLA: [imbrium.mit.edu](https://imbrium.mit.edu/DATA/LOLA_GDR/CYLINDRICAL/IMG/) (LOLA PDS düğümü)
- Diviner: [PDS Geosciences Node](https://pds-geosciences.wustl.edu/lro/lro-l-dlre-4-rdr-v1/lrodlr_1001/data/gdr_l3/cylindrical/img/)
- Tarihî inişler: NASA NSSDC, [*Table of Anthropogenic Impacts and Spacecraft on the Moon*](https://nssdc.gsfc.nasa.gov/planetary/lunar/lunar_artifact_impacts.html)

Dosya biçimi (satır/sütun sayısı, ölçek, projeksiyon) her seferinde dosyanın kendi PDS etiketinden (`.LBL`) okunur. Veri adresleri değişirse `Config.cs` dosyasından güncellenebilir; bir kaynak açılamazsa program o kriteri atlar ve uyarı verir.

---

## 🧮 Puanlama yöntemi

Varsayılan ("Dengeli") profildeki ağırlıklar:

| Kriter | Ağırlık | Hesaplama | Eşikler |
|---|---|---|---|
| **Yüzey eğimi** | %25 | LOLA DEM üzerinde Horn (1981) yöntemi; iniş elipsi içinde ortalama, P90 ve >15° alan oranı | Apollo LM ≤12°, Artemis HLS ≤8°. **>15° = uygun değil** |
| **Krater ve taş** | %20 | Diviner kaya yoğunluğu (Bandfield vd. 2011) + DEM'de halka profiliyle krater tespiti (d/D ≥ 0.05, Pike 1977) | Ay ortalaması ≈ %0.4. **>%10 = uygun değil** |
| **Güneş ışığı** | %15 | Güneş konumu (eksen eğikliği 1.54°) + 36 yönde 30 km ufuk profili → gölgelenme ve gün ışığı süresi | Öğlen yüksekliği, gün doğumu gecikmesi |
| **Sıcaklık** | %10 | Gündüz: εσT⁴ = (1−A(i))·S·cos i (Hayne vd. 2017). Gece: Diviner ölçümü | Ekvator öğlen ≈ 390 K, gece ≈ 95 K |
| **Dünya ile iletişim** | %15 | Alt-Dünya noktasına açısal uzaklık + librasyon (±7.9° boylam, ±6.7° enlem) + arazi ufku | Dünya ufkun altına inerse **uygun değil** |
| **Arazi yapısı** | %15 | Eğik düzlem çıkarıldıktan sonra RMS pürüzlülük, 5 km rölyef, mare/yayla sınıflaması | Düz mare yüzeyleri en iyi puanı alır |

**Karar:** ≥80 Çok uygun · 65–80 Uygun · 50–65 Şartlı uygun · <50 Uygun değil.
Kritik sınırlardan biri aşılırsa (eğim, kaya, Dünya görünmüyor) puandan bağımsız olarak **"UYGUN DEĞİL"** denir.

> Küresel ısı haritası tekli analizle aynı puan eğrilerini kullanır ama ~2 km ölçekte bir **ön eleme** haritasıdır. Kesin karar için noktaya tıklayıp 237 m çözünürlüklü analiz yapın.

---

## 🎯 Görev profilleri

| Profil | Eğim | Krater/taş | Güneş | Sıcaklık | İletişim | Arazi | Maks. eğim | Maks. kaya |
|---|---|---|---|---|---|---|---|---|
| Dengeli (standart) | 25 | 20 | 15 | 10 | 15 | 15 | 15° | %10 |
| Kısa robotik (CLPS) | 30 | 30 | 15 | 5 | 15 | 5 | 12° | %8 |
| İnsanlı (Artemis) | 30 | 25 | 10 | 10 | 15 | 10 | 10° | %5 |
| Kalıcı Ay üssü | 15 | 15 | 25 | 15 | 20 | 10 | 15° | %10 |
| Gezgin (rover) | 20 | 25 | 15 | 5 | 15 | 20 | 15° | %8 |

Yeni profil eklemek için `Analysis/MissionProfile.cs` dosyasına bakın.

---

## 📁 Proje yapısı

```
AyInisNavigatoru/
├── Program.cs                  # Giriş noktası
├── Config.cs                   # Veri adresleri, analiz ayarları, fiziksel sabitler
├── LandingNavigator.cs         # Veri indirme + analiz akışını yöneten ana sınıf
├── Localization.cs             # Türkçe/İngilizce metin desteği: L.T("tr", "en")
├── Models.cs                   # Sonuç veri modelleri
├── Analysis/
│   ├── TerrainAnalyzer.cs      # Eğim, pürüzlülük, krater tespiti
│   ├── SolarAnalyzer.cs        # Güneş konumu, ufuk, iniş penceresi
│   ├── ThermalAndCommAnalyzer.cs # Sıcaklık ve Dünya görünürlüğü
│   ├── SuitabilityScorer.cs    # Kriter puanları ve genel karar
│   ├── GlobalSuitabilityMap.cs # 1 milyon hücrelik küresel ısı haritası
│   ├── MissionProfile.cs       # Görev profilleri
│   └── LandingCatalog.cs       # 28 tarihî iniş noktası
├── Data/
│   ├── PdsLabel.cs             # PDS .LBL etiket ayrıştırıcı
│   └── PdsImageSource.cs       # HTTP Range ile kısmi okuma + disk önbelleği
└── UI/
    ├── MainForm.cs             # Ana pencere
    ├── MoonGlobeControl.cs     # 3B Ay küresi
    ├── MoonMapControl.cs       # 2B harita
    └── HtmlReport.cs           # Tarayıcı raporu
```

Çalışma sırasında exe'nin yanında şu klasör/dosyalar oluşur (depoya eklenmez):

- `veri_onbellek/` — indirilen NASA veri parçaları
- `raporlar/` — her analizin JSON çıktısı
- `dil.txt` — seçili dil

---

## ⚠️ Sınırlamalar

- Eğim 237 m çözünürlükte hesaplanır; iniş aracı ölçeğinde (1–10 m) eğimler daha yüksek olabilir. Daha ayrıntılı analiz için LROC NAC stereo DEM'leri (2–5 m) gerekir.
- Krater dedektörü ~1 km'den büyük kraterleri bulur; küçük kraterler ve kayalar için Diviner kaya yoğunluğu kullanılır.
- Güneş/Dünya tarihleri basitleştirilmiş bir astronomi modeline dayanır (±12 saat). Kesin değerler için NASA JPL SPICE çekirdekleri gerekir.
- Kutup bölgeleri (>80°) için polar stereografik LOLA ürünleri daha uygundur.

---

## 📚 Kaynaklar

- Smith, D.E. vd. (2010). The Lunar Orbiter Laser Altimeter investigation on LRO. *Space Sci. Rev.* 150.
- Bandfield, J.L. vd. (2011). Lunar surface rock abundance and regolith fines temperatures derived from LRO Diviner Radiometer data. *JGR* 116, E00H02.
- Hayne, P.O. vd. (2017). Global regolith thermophysical properties of the Moon from the Diviner Lunar Radiometer Experiment. *JGR Planets* 122.
- Williams, J.-P. vd. (2017). The global surface temperatures of the Moon as measured by the Diviner Lunar Radiometer Experiment. *Icarus* 283.
- Pike, R.J. (1977). Size-dependence in the shape of fresh impact craters on the Moon.
- Horn, B.K.P. (1981). Hill shading and the reflectance map. *Proc. IEEE* 69.
- Kopp, G. & Lean, J.L. (2011). A new, lower value of total solar irradiance. *GRL* 38.
- Meeus, J. *Astronomical Algorithms.*

---

## 🙏 Teşekkür

Bu proje **NASA Space Apps Challenge – Eskişehir** kapsamında geliştirilmiştir. Tüm bilimsel veriler NASA Planetary Data System (PDS) arşivlerinden alınmıştır. Bu uygulama NASA tarafından onaylanmış veya desteklenmiş değildir.

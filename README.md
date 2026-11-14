# 🌕 Ay İniş Navigatörü 3.0

**NASA Space Apps Challenge – Eskişehir**

Ay'ın yakın yüzünde seçilen bir noktanın inişe ne kadar uygun olduğunu, **NASA Lunar Reconnaissance Orbiter (LRO)** uydusunun gerçek ölçümlerine dayanarak 0–100 arasında puanlar.

---

## Visual Studio'da çalıştırma

1. Gerekenler: **Visual Studio 2022** (17.8 veya üstü) ve **.NET 8 SDK**. Visual Studio Installer'da **".NET desktop development"** iş yükü kurulu olmalı.
2. ZIP dosyasını bir klasöre çıkarın.
3. Visual Studio'da **File → Open → Project/Solution** yolunu izleyip `AyInisNavigatoru.csproj` dosyasını seçin.
4. **F5** ile çalıştırın. Bir pencere açılır.

## Ekran düzeni (sürüm 3.0)

```
┌───────────────────────────────────────────────────────────────────────────────┐
│ Görev:[▼] Git:[▼] │ 🌡 Isı haritası [katman ▼] │ 🛬 Tarihî inişler ☀ Gerçek güneş │ ⚖ 📄 ⚙ ❓ ⟲ │
├───────────────────────────────────────────────────────────────────────────────┤
│ Açıklama şeridi                                                                 │
├──────────────────────────────────────────────────┬────────────────────────────┤
│                 3B AY KÜRESİ                       │          RAPOR             │
├──────────────────────────────────────────────────┴────────────────────────────┤
│ İmleç konumu (+ ısı haritası değerleri) · ilerleme · durum · 📜 Günlük              │
└───────────────────────────────────────────────────────────────────────────────┘
```

## Yeni özellikler (3.0)

### 🌡 Küresel ısı haritası: Ay'ın her yeri uygunluğuna göre renklenir
- Düğmeye basınca Ay yüzeyi **1440 × 720 = 1.036.800 hücreye** (0.25°, ≈7.6 km) bölünür ve her hücre puanlanır.
- Tekli analizle **aynı puan eğrileri** (`ScoreCurves`) ve **aynı görev profili ağırlıkları** kullanılır.
- Katmanlar: Genel uygunluk · Yüzey eğimi · Krater ve taş · Güneş ışığı · Sıcaklık · Dünya ile iletişim · Arazi yapısı
- Veriler: LOLA LDEM_16 (eğim, pürüzlülük, rölyef, krater duvarları) + Diviner küresel kaya haritası (32 piksel/derece, ilk seferde ~90 MB, parça parça iner ve diskte saklanır).
- İmleç bir hücrenin üzerindeyken alt çubukta o hücrenin puanı, ortalama eğimi, kaya yoğunluğu ve Dünya'nın en düşük yüksekliği yazar.
- Görev profili değişince harita ~1 saniyede yeniden puanlanır.
- **Önemli:** küresel harita ~2 km ölçeğinde bir **ön eleme** haritasıdır. Kesin karar için noktaya tıklayıp 237 m çözünürlüklü tekli analiz yapın.

### 🛬 Tarihî iniş noktaları
- Ay'a yapılmış **28 başarılı yumuşak iniş** küre üzerinde gösterilir: Luna, Surveyor, Apollo 11–17, Chang'e 3–6, Chandrayaan-3, SLIM, IM-1, Blue Ghost 1, IM-2.
- Koordinatlar: **NASA NSSDC**, *Table of Anthropogenic Impacts and Spacecraft on the Moon*.
- "Git" listesinden birini seçince Ay oraya döner ve analiz yapılır.
- Her raporda **en yakın gerçek iniş noktası** ve uzaklığı yazar.
- Doğrulama örneği: kataloğa göre Apollo 12, Surveyor 3'ün ~160 m yanına inmiştir. Gerçekte de astronotlar Surveyor 3'e yürümüştür.

### ⚖ Nokta karşılaştırma
Analiz edilen tüm noktalar tek tabloda: genel puan, karar, 6 kriter ve en yakın gerçek iniş. Sütunlara göre sıralanabilir, çift tıkla o noktaya gidilir.

### 📄 Tarayıcıda rapor
Seçili noktanın ayrıntılı raporu, tüm noktaların karşılaştırması, küresel harita özeti ve yöntem/kaynak listesiyle HTML sayfası açılır. "Yazdır / PDF olarak kaydet" düğmesi vardır.

### Kaldırılanlar
"En iyi / en kötü nokta" bölge taraması kaldırıldı; yerini Ay'ın tamamını kapsayan küresel ısı haritası aldı.

---

## Kullanılan gerçek veriler

| Veri | Alet | Çözünürlük | Kaynak |
|---|---|---|---|
| Yükseklik modeli `LDEM_128` | LRO **LOLA** lazer altimetresi | 128 piksel/derece (~237 m) | imbrium.mit.edu (LOLA PDS düğümü) |
| Yükseklik modeli `LDEM_64` (ufuk için) | LRO **LOLA** | 64 piksel/derece (~474 m) | imbrium.mit.edu |
| Kaya yoğunluğu `dgdr_ra_avg_cyl_128` | LRO **Diviner** radyometresi | 128 piksel/derece | PDS Geosciences Node |
| Gece regolit sıcaklığı `dgdr_st_avg_cyl_032` | LRO **Diviner** | 32 piksel/derece | PDS Geosciences Node |

Program 2 GB'lık dosyaları **indirmez**. HTTP *Range* istekleriyle yalnızca seçilen noktanın çevresindeki satırları okur. Dosya biçimi (satır/sütun sayısı, ölçek, projeksiyon) her seferinde dosyanın kendi PDS etiketinden (`.LBL`) okunur.

---

## Kriterler ve bilimsel yöntem

| Kriter | Ağırlık | Nasıl hesaplanıyor | Eşikler |
|---|---|---|---|
| **Yüzey eğimi** | %25 | LOLA DEM üzerinde Horn (1981) yöntemi; iniş elipsi içinde ortalama, %90'lık dilim (P90) ve >15° alan oranı | Apollo LM ≤12°, Artemis HLS ≤8°. **>15° = uygun değil** |
| **Krater ve taş yoğunluğu** | %20 | Diviner kaya yoğunluğu (≥1 m kayaların kapladığı alan, Bandfield vd. 2011) + LOLA DEM'de halka profiliyle krater tespiti (derinlik/çap ≥ 0.05, Pike 1977) | Ay ortalaması ≈ %0.4 kaya. **>%10 = uygun değil** |
| **Güneş ışığı** | %15 | Güneş konumu (Ay ekseni eğikliği 1.54°) + LOLA'dan 36 yönde 30 km ufuk profili → gölgelenme, gün ışığı süresi | Öğlen yüksekliği ve gün doğumu gecikmesi |
| **Sıcaklık** | %10 | Gündüz: ışınım dengesi εσT⁴ = (1−A(i))·S·cos i (Hayne vd. 2017 albedo modeli). Gece: Diviner ölçümü | Ekvator öğlen ≈ 390 K, gece ≈ 95 K |
| **Dünya ile iletişim** | %15 | Alt-Dünya noktasına açısal uzaklık + librasyon (±7.9° boylam, ±6.7° enlem) + arazi ufku | Dünya ufkun altına inerse **uygun değil** |
| **Yükseklik ve arazi yapısı** | %15 | Eğik düzlem çıkarıldıktan sonra kalan pürüzlülük (RMS), 5 km'lik rölyef, mare/yayla sınıflaması | Düz mare yüzeyleri en iyi puanı alır |

**Genel karar:** ≥80 Çok uygun · 65–80 Uygun · 50–65 Şartlı uygun · <50 Uygun değil.
Kritik sınırlardan biri aşılırsa (eğim >15°, kaya >%10, Dünya görünmüyor) puandan bağımsız olarak "UYGUN DEĞİL" denir.

**İniş zamanlaması:** Program, güneşin 7°–20° yükseklikte olduğu bir sonraki sabahı önerir. Apollo inişleri de güneş ~10° yüksekteyken yapıldı, çünkü alçak güneşte kayalar ve kraterler uzun gölge verir ve kolayca görülür.

---

## Sınırlamalar (jüriye dürüstçe söyleyin)

- Eğim, 237 m çözünürlükte hesaplanıyor. Gerçek iniş aracı ölçeğinde (1–10 m) eğimler daha yüksek olabilir. Daha ayrıntılı analiz için LROC NAC stereo DEM'leri (2–5 m) kullanılmalıdır.
- Krater dedektörü ~1 km'den büyük kraterleri bulur. Küçük kraterler ve kayalar için Diviner kaya yoğunluğu kullanılıyor.
- Güneş/Dünya tarihleri basitleştirilmiş astronomi modeline dayanır (±12 saat). Kesin değerler için NASA JPL SPICE çekirdekleri gerekir.
- Kutup bölgeleri (>80°) için polar stereografik LOLA ürünleri daha uygundur.

## Veri adresi değişirse

PDS arşivleri zaman zaman yeniden düzenlenir. Bir veri kaynağı açılamazsa program o kriteri atlar ve uyarı verir. Adresler `Config.cs` dosyasının başındadır.

## Kaynaklar

- Smith, D.E. vd. (2010). The Lunar Orbiter Laser Altimeter investigation on LRO. *Space Sci. Rev.* 150.
- Bandfield, J.L. vd. (2011). Lunar surface rock abundance and regolith fines temperatures derived from LRO Diviner Radiometer data. *JGR* 116, E00H02.
- Hayne, P.O. vd. (2017). Global regolith thermophysical properties of the Moon from the Diviner Lunar Radiometer Experiment. *JGR Planets* 122.
- Williams, J.-P. vd. (2017). The global surface temperatures of the Moon as measured by the Diviner Lunar Radiometer Experiment. *Icarus* 283.
- Pike, R.J. (1977). Size-dependence in the shape of fresh impact craters on the Moon.
- Horn, B.K.P. (1981). Hill shading and the reflectance map. *Proc. IEEE* 69.

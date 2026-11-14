namespace AyInisNavigatoru.Analysis;

/// <summary>
/// Altı kriteri 0-100 arasında puanlar ve ağırlıklı ortalamayı alır.
/// Eşik değerleri gerçek görev gereksinimlerine dayanır:
///  • Eğim: Apollo Ay Modülü ≤12°, NASA Artemis HLS ≤8° (iniş noktası), CLPS iniş araçları genelde ≤10-15°.
///  • Kaya: Diviner kaya yoğunluğu (≥1 m kayaların kapladığı alan oranı). Ay ortalaması ~%0.4 (Bandfield vd. 2011).
///  • Güneş: Enerji için öğlen güneş yüksekliği ve araziden kaynaklı gölgelenme.
///  • Sıcaklık: Elektronik ve batarya için aşırı sıcak/soğuktan uzak olmak.
///  • İletişim: Dünya'nın ufkun üzerinde kalması (doğrudan haberleşme).
///  • Arazi: Pürüzlülük ve yerel yükseklik farkı (rölyef).
/// </summary>
public static class SuitabilityScorer
{
    /// <summary>Parçalı doğrusal enterpolasyon: (x, puan) noktaları x'e göre artan sırada.</summary>
    private static double Piecewise(double x, params (double X, double Y)[] pts)
    {
        if (x <= pts[0].X) return pts[0].Y;
        for (int i = 1; i < pts.Length; i++)
            if (x <= pts[i].X)
            {
                double t = (x - pts[i - 1].X) / (pts[i].X - pts[i - 1].X);
                return pts[i - 1].Y + t * (pts[i].Y - pts[i - 1].Y);
            }
        return pts[^1].Y;
    }

    private static double Clamp(double v) => Math.Clamp(v, 0, 100);

    public static void Score(SiteEvaluation ev) => Score(ev, MissionProfile.Standard);

    public static void Score(SiteEvaluation ev, MissionProfile profile)
    {
        var t = ev.Terrain;
        ev.Criteria.Clear();
        ev.HardLimitViolations.Clear();
        ev.ProfileName = profile.Name;

        // 1) YÜZEY EĞİMİ (%25)
        var slope = new CriterionScore { Name = "Yüzey eğimi", Weight = profile.SlopeWeight };
        if (t != null && !double.IsNaN(t.SlopeAtSiteDeg))
        {
            double sSite = ScoreCurves.Slope(t.SlopeAtSiteDeg);
            double sP90 = ScoreCurves.Slope(t.SlopeP90Deg);
            double sFrac = ScoreCurves.SteepFraction(t.PercentOver15Deg);
            slope.Score = Clamp(0.45 * sSite + 0.35 * sP90 + 0.20 * sFrac);
            slope.Summary = $"Nokta: {t.SlopeAtSiteDeg:F1}°, elips içi P90: {t.SlopeP90Deg:F1}°";
            slope.Details.Add($"İniş elipsi (r = {t.EllipseRadiusM / 1000:F1} km) ortalama eğim: {t.SlopeMeanDeg:F1}°, en yüksek: {t.SlopeMaxDeg:F1}°");
            slope.Details.Add($">10° eğimli alan: %{t.PercentOver10Deg:F1}  |  >15° eğimli alan: %{t.PercentOver15Deg:F1}");
            slope.Details.Add($"Eğim yönü (bakı): {t.AspectAtSiteDeg:F0}° ({Compass(t.AspectAtSiteDeg)})");
            slope.Details.Add($"Not: {t.PixelSizeM:F0} m çözünürlükte hesaplandı; iniş aracı ölçeğindeki (1-10 m) eğimler daha yüksek olabilir.");
            if (t.SlopeAtSiteDeg > profile.MaxSlopeDeg)
                ev.HardLimitViolations.Add($"Nokta eğimi {t.SlopeAtSiteDeg:F1}° > {profile.MaxSlopeDeg:F0}° (iniş aracı devrilme riski)");
        }
        else slope.Summary = "Veri alınamadı";
        ev.Criteria.Add(slope);

        // 2) KRATER VE TAŞ YOĞUNLUĞU (%20)
        var hazard = new CriterionScore { Name = "Krater ve taş yoğunluğu", Weight = profile.HazardWeight };
        double? rockScore = null, craterScore = null;
        if (ev.Rocks != null)
        {
            var r = ev.Rocks;
            rockScore = ScoreCurves.Rock(Math.Max(r.RockAbundanceAtSite, r.RockAbundanceMean));
            hazard.Details.Add($"Diviner kaya yoğunluğu (≥1 m kayaların kapladığı alan): nokta %{r.RockAbundanceAtSite * 100:F2}, " +
                               $"ortalama %{r.RockAbundanceMean * 100:F2}, en yüksek %{r.RockAbundanceMax * 100:F2}");
            hazard.Details.Add("Karşılaştırma: Ay ortalaması ≈ %0.4; genç kraterlerin çevresi %5-30'a çıkabilir.");
            if (r.RockAbundanceAtSite > profile.MaxRockAbundance)
                ev.HardLimitViolations.Add($"Kaya yoğunluğu %{r.RockAbundanceAtSite * 100:F1} > %{profile.MaxRockAbundance * 100:F0} (iniş ayağı kayaya çarpabilir)");
        }
        if (t != null)
        {
            double density = t.CraterDensityPer100Km2;
            craterScore = ScoreCurves.CraterDensity(density);
            if (t.SiteInsideCrater) craterScore -= 25;
            if (!double.IsNaN(t.NearestCraterRimDistanceM) && t.NearestCraterRimDistanceM < 500) craterScore -= 15;
            craterScore = Clamp(craterScore.Value);
            hazard.Details.Add($"LOLA verisinde tespit edilen krater (Ç ≥ ~{4 * t.PixelSizeM / 1000:F1} km): {t.Craters.Count} adet, " +
                               $"{t.CraterSearchRadiusM / 1000:F0} km yarıçapta → {density:F1} krater / 100 km²");
            if (t.Craters.Count > 0)
            {
                var c = t.Craters[0];
                hazard.Details.Add($"En yakın krater: çap {c.DiameterM / 1000:F2} km, derinlik {c.DepthM:F0} m, " +
                                   $"d/D = {c.DepthToDiameter:F2}, merkeze uzaklık {c.DistanceFromSiteM / 1000:F2} km");
            }
            if (t.SiteInsideCrater) hazard.Details.Add("⚠ Seçilen nokta bir kraterin içinde.");
            // Taze (genç) kraterler: dik iç duvarlar ve bol kaya. d/D ≥ 0.15 taze kabul edilir (Pike 1977).
            var host = t.Craters.FirstOrDefault(k => k.DistanceFromSiteM < k.DiameterM / 2);
            if (host != null && host.DepthToDiameter >= 0.15)
                ev.HardLimitViolations.Add($"Nokta taze bir kraterin içinde (Ç {host.DiameterM / 1000:F1} km, d/D {host.DepthToDiameter:F2}): dik duvarlar ve kaya alanı");
        }
        if (rockScore.HasValue && craterScore.HasValue) hazard.Score = 0.6 * rockScore.Value + 0.4 * craterScore.Value;
        else hazard.Score = rockScore ?? craterScore;
        hazard.Summary = hazard.Score.HasValue
            ? $"Kaya: {(rockScore.HasValue ? $"{rockScore:F0}" : "veri yok")} puan, krater: {(craterScore.HasValue ? $"{craterScore:F0}" : "veri yok")} puan"
            : "Veri alınamadı";
        ev.Criteria.Add(hazard);

        // 3) GÜNEŞ IŞIĞI (%15)
        var sun = new CriterionScore { Name = "Güneş ışığı", Weight = profile.SunWeight };
        if (ev.Sun != null)
        {
            var s = ev.Sun;
            double elevScore = ScoreCurves.SunElevation(s.NoonElevationDeg);
            double ratio = s.FlatTerrainFraction > 0 ? s.IlluminatedFraction / s.FlatTerrainFraction : 0;
            double illumScore = ScoreCurves.Illumination(ratio);
            sun.Score = Clamp(0.5 * elevScore + 0.5 * illumScore);
            sun.Summary = $"Öğlen güneş yüksekliği {s.NoonElevationDeg:F1}°, gün ışığı {s.DaylightEarthDays:F1} Dünya günü";
            sun.Details.Add($"Aydınlık süre oranı: %{s.IlluminatedFraction * 100:F1} (düz arazide %{s.FlatTerrainFraction * 100:F1})");
            sun.Details.Add($"Araziden dolayı gün doğumu {s.SunriseDelayHours:F1} saat gecikiyor, gün batımı {s.SunsetAdvanceHours:F1} saat erken");
            sun.Details.Add($"Güneş paneli verimi (öğlen, düz panel): ~%{Math.Sin(s.NoonElevationDeg * Math.PI / 180) * 100:F0} (dik açıya göre)");
        }
        else sun.Summary = "Hesaplanamadı";
        ev.Criteria.Add(sun);

        // 4) SICAKLIK (%10)
        var thermal = new CriterionScore { Name = "Sıcaklık", Weight = profile.ThermalWeight };
        if (ev.Thermal != null)
        {
            var th = ev.Thermal;
            double hotScore = ScoreCurves.Hot(th.NoonTemperatureK);
            double coldScore = ScoreCurves.Cold(th.NightTemperatureK);
            thermal.Score = Clamp(0.5 * hotScore + 0.5 * coldScore);
            thermal.Summary = $"Öğlen {th.NoonTemperatureK:F0} K ({th.NoonTemperatureK - 273.15:F0} °C), " +
                              $"gece {th.NightTemperatureK:F0} K ({th.NightTemperatureK - 273.15:F0} °C)";
            thermal.Details.Add($"İniş anında (güneş ~12°) yüzey sıcaklığı: {th.LandingWindowTemperatureK:F0} K ({th.LandingWindowTemperatureK - 273.15:F0} °C)");
            thermal.Details.Add($"Gece verisi kaynağı: {th.NightSource}");
            thermal.Details.Add("Gündüz değeri ışınım dengesi modeliyle hesaplandı (εσT⁴ = (1−A)·S·cos i); Diviner ölçümleriyle ±10 K uyumludur.");
        }
        else thermal.Summary = "Hesaplanamadı";
        ev.Criteria.Add(thermal);

        // 5) DÜNYA İLE İLETİŞİM (%15)
        var comm = new CriterionScore { Name = "Dünya ile iletişim", Weight = profile.CommWeight };
        if (ev.Communication != null)
        {
            var c = ev.Communication;
            comm.Score = c.EarthNeverVisible ? 0 :
                Clamp(ScoreCurves.CommMargin(c.WorstCaseMarginDeg));
            comm.Summary = $"Dünya yüksekliği ort. {c.EarthElevationMeanDeg:F1}° (librasyonla {c.EarthElevationMinDeg:F1}°…{c.EarthElevationMaxDeg:F1}°)";
            comm.Details.Add($"Dünya'nın yönü (azimut): {c.EarthAzimuthDeg:F0}° ({Compass(c.EarthAzimuthDeg)}); o yöndeki arazi ufku: {c.TerrainHorizonTowardEarthDeg:F1}°");
            comm.Details.Add($"En kötü durumda ufkun üzerindeki pay: {c.WorstCaseMarginDeg:F1}°");
            comm.Details.Add($"Tek yön sinyal gecikmesi: {c.OneWayLightTimeS:F2} s (gidiş-dönüş {2 * c.OneWayLightTimeS:F2} s)");
            if (c.EarthNeverVisible)
            {
                comm.Details.Add("⚠ Dünya bu noktadan hiç görünmez: röle uydusu gerekir (ör. Chang'e 4 / Queqiao).");
                ev.HardLimitViolations.Add("Dünya ufkun altında: doğrudan iletişim imkânsız");
            }
            else if (!c.EarthAlwaysVisible)
                comm.Details.Add("⚠ Librasyon nedeniyle Dünya zaman zaman ufkun altına iniyor: iletişim kesintileri olur.");
        }
        else comm.Summary = "Hesaplanamadı";
        ev.Criteria.Add(comm);

        // 6) YÜKSEKLİK VE ARAZİ YAPISI (%15)
        var terrain = new CriterionScore { Name = "Yükseklik ve arazi yapısı", Weight = profile.TerrainWeight };
        if (t != null)
        {
            double roughScore = ScoreCurves.Roughness(t.RoughnessRmsM);
            double reliefScore = ScoreCurves.Relief(t.LocalReliefM);
            terrain.Score = Clamp(0.55 * roughScore + 0.45 * reliefScore);
            terrain.Summary = $"Yükseklik {t.ElevationM:F0} m, pürüzlülük {t.RoughnessRmsM:F1} m RMS, rölyef {t.LocalReliefM:F0} m";
            terrain.Details.Add($"Arazi tipi (tahmin): {t.TerrainType}");
            terrain.Details.Add($"Yükseklik, Ay referans küresine (R = 1737.4 km) göredir.");
            terrain.Details.Add($"5 km yarıçapta en yüksek ile en alçak nokta arasındaki fark: {t.LocalReliefM:F0} m");
        }
        else terrain.Summary = "Veri alınamadı";
        ev.Criteria.Add(terrain);

        // ---------------- Genel puan ----------------
        double wSum = ev.Criteria.Where(c => c.Score.HasValue).Sum(c => c.Weight);
        ev.OverallScore = wSum > 0
            ? ev.Criteria.Where(c => c.Score.HasValue).Sum(c => c.Score!.Value * c.Weight) / wSum
            : 0;

        if (ev.HardLimitViolations.Count > 0)
            ev.Verdict = "UYGUN DEĞİL (kritik sınır aşıldı)";
        else if (ev.OverallScore >= 80) ev.Verdict = "ÇOK UYGUN";
        else if (ev.OverallScore >= 65) ev.Verdict = "UYGUN";
        else if (ev.OverallScore >= 50) ev.Verdict = "ŞARTLI UYGUN (dikkatli iniş gerekir)";
        else ev.Verdict = "UYGUN DEĞİL";

        int missing = ev.Criteria.Count(c => !c.Score.HasValue);
        ev.Warnings.RemoveAll(w => w.Contains("kriter için veri alınamadı"));
        if (missing > 0)
            ev.Warnings.Add($"{missing} kriter için veri alınamadı; genel puan kalan kriterlerin ağırlıklarına göre hesaplandı.");
    }

    public static string Compass(double az)
    {
        string[] dirs = { "K", "KKD", "KD", "DKD", "D", "DGD", "GD", "GGD", "G", "GGB", "GB", "BGB", "B", "BKB", "KB", "KKB" };
        return dirs[(int)Math.Round(((az % 360) + 360) % 360 / 22.5) % 16];
    }
}

/// <summary>
/// Tüm puan eğrileri tek yerde: tekli analiz, karşılaştırma ve küresel harita AYNI eğrileri kullanır.
/// Her eğri, ölçülen değeri 0-100 puana çeviren parçalı doğrusal bir fonksiyondur.
/// </summary>
public static class ScoreCurves
{
    public static double Piecewise(double x, params (double X, double Y)[] pts)
    {
        if (double.IsNaN(x)) return double.NaN;
        if (x <= pts[0].X) return pts[0].Y;
        for (int i = 1; i < pts.Length; i++)
            if (x <= pts[i].X)
            {
                double t = (x - pts[i - 1].X) / (pts[i].X - pts[i - 1].X);
                return pts[i - 1].Y + t * (pts[i].Y - pts[i - 1].Y);
            }
        return pts[^1].Y;
    }

    /// <summary>Eğim (°): Artemis HLS 8°, Apollo LM 12°, 15°+ devrilme riski</summary>
    public static double Slope(double deg) =>
        Piecewise(deg, (0, 100), (5, 95), (8, 85), (10, 70), (12, 55), (15, 30), (20, 10), (25, 0));

    /// <summary>15°'den dik alan yüzdesi</summary>
    public static double SteepFraction(double pct) => Piecewise(pct, (0, 100), (5, 70), (20, 30), (50, 0));

    /// <summary>Diviner kaya yoğunluğu (oran). Ay ortalaması ≈ 0.004 (Bandfield vd. 2011)</summary>
    public static double Rock(double ra) =>
        Piecewise(ra, (0, 100), (0.005, 95), (0.01, 80), (0.02, 60), (0.05, 30), (0.10, 0));

    /// <summary>Krater yoğunluğu (Ç ≥ ~1 km krater / 100 km²)</summary>
    public static double CraterDensity(double per100Km2) =>
        Piecewise(per100Km2, (0, 100), (2, 85), (5, 65), (10, 40), (20, 15), (40, 0));

    public static double SunElevation(double noonElevDeg) =>
        Piecewise(noonElevDeg, (5, 0), (10, 25), (30, 65), (50, 90), (60, 100));

    public static double Illumination(double ratioToFlat) =>
        Piecewise(ratioToFlat, (0.6, 0), (0.8, 40), (0.9, 70), (0.97, 90), (1.0, 100));

    public static double Hot(double noonK) => Piecewise(noonK, (250, 100), (330, 85), (370, 65), (395, 45), (420, 20));
    public static double Cold(double nightK) => Piecewise(nightK, (60, 10), (80, 35), (95, 60), (110, 80), (130, 100));

    /// <summary>Dünya'nın en kötü durumda ufkun üzerindeki payı (°)</summary>
    public static double CommMargin(double marginDeg) => Piecewise(marginDeg, (-5, 0), (0, 20), (10, 60), (20, 80), (35, 100));

    public static double Roughness(double rmsM) => Piecewise(rmsM, (0, 100), (5, 95), (15, 75), (30, 50), (60, 20), (100, 0));
    public static double Relief(double reliefM) => Piecewise(reliefM, (0, 100), (150, 90), (400, 70), (800, 45), (1500, 20), (3000, 0));
}

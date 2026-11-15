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
    private static double Clamp(double v) => Math.Clamp(v, 0, 100);

    public static void Score(SiteEvaluation ev) => Score(ev, MissionProfile.Standard);

    /// <summary>
    /// Puanları ve tüm açıklama metinlerini ŞU ANKİ DİLDE üretir.
    /// Dil değiştiğinde aynı değerlendirme yeniden puanlanarak metinler diğer dile çevrilir.
    /// </summary>
    public static void Score(SiteEvaluation ev, MissionProfile profile)
    {
        var t = ev.Terrain;
        ev.Criteria.Clear();
        ev.HardLimitViolations.Clear();
        ev.ProfileName = profile.Name;
        string noData = L.T("Veri alınamadı", "No data available");
        string notComputed = L.T("Hesaplanamadı", "Could not be computed");

        // 1) YÜZEY EĞİMİ
        var slope = new CriterionScore { Name = L.T("Yüzey eğimi", "Surface slope"), Weight = profile.SlopeWeight };
        if (t != null && !double.IsNaN(t.SlopeAtSiteDeg))
        {
            double sSite = ScoreCurves.Slope(t.SlopeAtSiteDeg);
            double sP90 = ScoreCurves.Slope(t.SlopeP90Deg);
            double sFrac = ScoreCurves.SteepFraction(t.PercentOver15Deg);
            slope.Score = Clamp(0.45 * sSite + 0.35 * sP90 + 0.20 * sFrac);
            slope.Summary = L.T($"Nokta: {t.SlopeAtSiteDeg:F1}°, elips içi P90: {t.SlopeP90Deg:F1}°",
                                $"Site: {t.SlopeAtSiteDeg:F1}°, ellipse P90: {t.SlopeP90Deg:F1}°");
            slope.Details.Add(L.T($"İniş elipsi (r = {t.EllipseRadiusM / 1000:F1} km) ortalama eğim: {t.SlopeMeanDeg:F1}°, en yüksek: {t.SlopeMaxDeg:F1}°",
                                  $"Landing ellipse (r = {t.EllipseRadiusM / 1000:F1} km) mean slope: {t.SlopeMeanDeg:F1}°, maximum: {t.SlopeMaxDeg:F1}°"));
            slope.Details.Add(L.T($">10° eğimli alan: %{t.PercentOver10Deg:F1}  |  >15° eğimli alan: %{t.PercentOver15Deg:F1}",
                                  $"Area steeper than 10°: {t.PercentOver10Deg:F1}%  |  steeper than 15°: {t.PercentOver15Deg:F1}%"));
            slope.Details.Add(L.T($"Eğim yönü (bakı): {t.AspectAtSiteDeg:F0}° ({Compass(t.AspectAtSiteDeg)})",
                                  $"Slope direction (aspect): {t.AspectAtSiteDeg:F0}° ({Compass(t.AspectAtSiteDeg)})"));
            slope.Details.Add(L.T($"Not: {t.PixelSizeM:F0} m çözünürlükte hesaplandı; iniş aracı ölçeğindeki (1-10 m) eğimler daha yüksek olabilir.",
                                  $"Note: computed at {t.PixelSizeM:F0} m resolution; slopes at lander scale (1-10 m) may be higher."));
            if (t.SlopeAtSiteDeg > profile.MaxSlopeDeg)
                ev.HardLimitViolations.Add(L.T($"Nokta eğimi {t.SlopeAtSiteDeg:F1}° > {profile.MaxSlopeDeg:F0}° (iniş aracı devrilme riski)",
                                               $"Site slope {t.SlopeAtSiteDeg:F1}° > {profile.MaxSlopeDeg:F0}° (risk of lander tipping over)"));
        }
        else slope.Summary = noData;
        ev.Criteria.Add(slope);

        // 2) KRATER VE TAŞ YOĞUNLUĞU (yalnızca puanı düşürür)
        var hazard = new CriterionScore { Name = L.T("Krater ve taş yoğunluğu", "Crater and rock density"), Weight = profile.HazardWeight };
        double? rockScore = null, craterScore = null;
        if (ev.Rocks != null)
        {
            var r = ev.Rocks;
            rockScore = ScoreCurves.Rock(Math.Max(r.RockAbundanceAtSite, r.RockAbundanceMean));
            hazard.Details.Add(L.T($"Diviner kaya yoğunluğu (≥1 m kayaların kapladığı alan): nokta %{r.RockAbundanceAtSite * 100:F2}, " +
                                   $"ortalama %{r.RockAbundanceMean * 100:F2}, en yüksek %{r.RockAbundanceMax * 100:F2}",
                                   $"Diviner rock abundance (area covered by rocks ≥1 m): site {r.RockAbundanceAtSite * 100:F2}%, " +
                                   $"mean {r.RockAbundanceMean * 100:F2}%, maximum {r.RockAbundanceMax * 100:F2}%"));
            hazard.Details.Add(L.T("Karşılaştırma: Ay ortalaması ≈ %0.4; genç kraterlerin çevresi %5-30'a çıkabilir.",
                                   "For comparison: lunar average ≈ 0.4%; surroundings of young craters can reach 5-30%."));
            if (r.RockAbundanceAtSite > profile.MaxRockAbundance)
                ev.HardLimitViolations.Add(L.T($"Kaya yoğunluğu %{r.RockAbundanceAtSite * 100:F1} > %{profile.MaxRockAbundance * 100:F0} (iniş ayağı kayaya çarpabilir)",
                                               $"Rock abundance {r.RockAbundanceAtSite * 100:F1}% > {profile.MaxRockAbundance * 100:F0}% (landing legs may hit rocks)"));
        }
        if (t != null)
        {
            double density = t.CraterDensityPer100Km2;
            craterScore = ScoreCurves.CraterDensity(density);
            if (t.SiteInsideCrater) craterScore -= 25;
            if (!double.IsNaN(t.NearestCraterRimDistanceM) && t.NearestCraterRimDistanceM < 500) craterScore -= 15;
            craterScore = Clamp(craterScore.Value);
            hazard.Details.Add(L.T($"LOLA verisinde tespit edilen krater (Ç ≥ ~{4 * t.PixelSizeM / 1000:F1} km): {t.Craters.Count} adet, " +
                                   $"{t.CraterSearchRadiusM / 1000:F0} km yarıçapta → {density:F1} krater / 100 km²",
                                   $"Craters detected in LOLA data (D ≥ ~{4 * t.PixelSizeM / 1000:F1} km): {t.Craters.Count} " +
                                   $"within {t.CraterSearchRadiusM / 1000:F0} km → {density:F1} craters / 100 km²"));
            if (t.Craters.Count > 0)
            {
                var c = t.Craters[0];
                hazard.Details.Add(L.T($"En yakın krater: çap {c.DiameterM / 1000:F2} km, derinlik {c.DepthM:F0} m, " +
                                       $"d/D = {c.DepthToDiameter:F2}, merkeze uzaklık {c.DistanceFromSiteM / 1000:F2} km",
                                       $"Nearest crater: diameter {c.DiameterM / 1000:F2} km, depth {c.DepthM:F0} m, " +
                                       $"d/D = {c.DepthToDiameter:F2}, distance to center {c.DistanceFromSiteM / 1000:F2} km"));
            }
            if (t.SiteInsideCrater) hazard.Details.Add(L.T("⚠ Seçilen nokta bir kraterin içinde.", "⚠ The selected site is inside a crater."));
            // Taze (genç) kraterler: dik iç duvarlar ve bol kaya. d/D ≥ 0.15 taze kabul edilir (Pike 1977).
            var host = t.Craters.FirstOrDefault(k => k.DistanceFromSiteM < k.DiameterM / 2);
            if (host != null && host.DepthToDiameter >= 0.15)
                ev.HardLimitViolations.Add(L.T($"Nokta taze bir kraterin içinde (Ç {host.DiameterM / 1000:F1} km, d/D {host.DepthToDiameter:F2}): dik duvarlar ve kaya alanı",
                                               $"Site is inside a fresh crater (D {host.DiameterM / 1000:F1} km, d/D {host.DepthToDiameter:F2}): steep walls and boulder fields"));
        }
        if (rockScore.HasValue && craterScore.HasValue) hazard.Score = 0.6 * rockScore.Value + 0.4 * craterScore.Value;
        else hazard.Score = rockScore ?? craterScore;
        string na = L.T("veri yok", "no data");
        string rockTxt = rockScore.HasValue ? $"{rockScore:F0}" : na;
        string craterTxt = craterScore.HasValue ? $"{craterScore:F0}" : na;
        hazard.Summary = hazard.Score.HasValue
            ? L.T($"Kaya: {rockTxt} puan, krater: {craterTxt} puan", $"Rocks: {rockTxt} pts, craters: {craterTxt} pts")
            : noData;
        ev.Criteria.Add(hazard);

        // 3) GÜNEŞ IŞIĞI
        var sun = new CriterionScore { Name = L.T("Güneş ışığı", "Sunlight"), Weight = profile.SunWeight };
        if (ev.Sun != null)
        {
            var s = ev.Sun;
            double elevScore = ScoreCurves.SunElevation(s.NoonElevationDeg);
            double ratio = s.FlatTerrainFraction > 0 ? s.IlluminatedFraction / s.FlatTerrainFraction : 0;
            double illumScore = ScoreCurves.Illumination(ratio);
            sun.Score = Clamp(0.5 * elevScore + 0.5 * illumScore);
            sun.Summary = L.T($"Öğlen güneş yüksekliği {s.NoonElevationDeg:F1}°, gün ışığı {s.DaylightEarthDays:F1} Dünya günü",
                              $"Noon Sun elevation {s.NoonElevationDeg:F1}°, daylight {s.DaylightEarthDays:F1} Earth days");
            sun.Details.Add(L.T($"Aydınlık süre oranı: %{s.IlluminatedFraction * 100:F1} (düz arazide %{s.FlatTerrainFraction * 100:F1})",
                                $"Fraction of time in sunlight: {s.IlluminatedFraction * 100:F1}% (flat terrain: {s.FlatTerrainFraction * 100:F1}%)"));
            sun.Details.Add(L.T($"Araziden dolayı gün doğumu {s.SunriseDelayHours:F1} saat gecikiyor, gün batımı {s.SunsetAdvanceHours:F1} saat erken",
                                $"Due to terrain, sunrise is delayed by {s.SunriseDelayHours:F1} h and sunset comes {s.SunsetAdvanceHours:F1} h early"));
            sun.Details.Add(L.T($"Güneş paneli verimi (öğlen, düz panel): ~%{Math.Sin(s.NoonElevationDeg * Math.PI / 180) * 100:F0} (dik açıya göre)",
                                $"Solar panel output (noon, flat panel): ~{Math.Sin(s.NoonElevationDeg * Math.PI / 180) * 100:F0}% of perpendicular"));
        }
        else sun.Summary = notComputed;
        ev.Criteria.Add(sun);

        // 4) SICAKLIK
        var thermal = new CriterionScore { Name = L.T("Sıcaklık", "Temperature"), Weight = profile.ThermalWeight };
        if (ev.Thermal != null)
        {
            var th = ev.Thermal;
            double hotScore = ScoreCurves.Hot(th.NoonTemperatureK);
            double coldScore = ScoreCurves.Cold(th.NightTemperatureK);
            thermal.Score = Clamp(0.5 * hotScore + 0.5 * coldScore);
            thermal.Summary = L.T($"Öğlen {th.NoonTemperatureK:F0} K ({th.NoonTemperatureK - 273.15:F0} °C), gece {th.NightTemperatureK:F0} K ({th.NightTemperatureK - 273.15:F0} °C)",
                                  $"Noon {th.NoonTemperatureK:F0} K ({th.NoonTemperatureK - 273.15:F0} °C), night {th.NightTemperatureK:F0} K ({th.NightTemperatureK - 273.15:F0} °C)");
            thermal.Details.Add(L.T($"İniş anında (güneş ~12°) yüzey sıcaklığı: {th.LandingWindowTemperatureK:F0} K ({th.LandingWindowTemperatureK - 273.15:F0} °C)",
                                    $"Surface temperature at landing time (Sun ~12°): {th.LandingWindowTemperatureK:F0} K ({th.LandingWindowTemperatureK - 273.15:F0} °C)"));
            string nightSrc = th.NightIsMeasured
                ? L.T("LRO Diviner gece regolit sıcaklığı (ölçüm)", "LRO Diviner nighttime regolith temperature (measured)")
                : L.T("Model (Diviner verisi alınamadı)", "Model (Diviner data unavailable)");
            thermal.Details.Add(L.T($"Gece verisi kaynağı: {nightSrc}", $"Night data source: {nightSrc}"));
            thermal.Details.Add(L.T("Gündüz değeri ışınım dengesi modeliyle hesaplandı (εσT⁴ = (1−A)·S·cos i); Diviner ölçümleriyle ±10 K uyumludur.",
                                    "Daytime value from the radiative-equilibrium model (εσT⁴ = (1−A)·S·cos i); agrees with Diviner within ±10 K."));
        }
        else thermal.Summary = notComputed;
        ev.Criteria.Add(thermal);

        // 5) DÜNYA İLE İLETİŞİM
        var comm = new CriterionScore { Name = L.T("Dünya ile iletişim", "Communication with Earth"), Weight = profile.CommWeight };
        if (ev.Communication != null)
        {
            var c = ev.Communication;
            comm.Score = c.EarthNeverVisible ? 0 : Clamp(ScoreCurves.CommMargin(c.WorstCaseMarginDeg));
            comm.Summary = L.T($"Dünya yüksekliği ort. {c.EarthElevationMeanDeg:F1}° (librasyonla {c.EarthElevationMinDeg:F1}°…{c.EarthElevationMaxDeg:F1}°)",
                               $"Earth elevation mean {c.EarthElevationMeanDeg:F1}° (with libration {c.EarthElevationMinDeg:F1}°…{c.EarthElevationMaxDeg:F1}°)");
            comm.Details.Add(L.T($"Dünya'nın yönü (azimut): {c.EarthAzimuthDeg:F0}° ({Compass(c.EarthAzimuthDeg)}); o yöndeki arazi ufku: {c.TerrainHorizonTowardEarthDeg:F1}°",
                                 $"Direction of Earth (azimuth): {c.EarthAzimuthDeg:F0}° ({Compass(c.EarthAzimuthDeg)}); terrain horizon in that direction: {c.TerrainHorizonTowardEarthDeg:F1}°"));
            comm.Details.Add(L.T($"En kötü durumda ufkun üzerindeki pay: {c.WorstCaseMarginDeg:F1}°",
                                 $"Worst-case margin above the horizon: {c.WorstCaseMarginDeg:F1}°"));
            comm.Details.Add(L.T($"Tek yön sinyal gecikmesi: {c.OneWayLightTimeS:F2} s (gidiş-dönüş {2 * c.OneWayLightTimeS:F2} s)",
                                 $"One-way signal delay: {c.OneWayLightTimeS:F2} s (round trip {2 * c.OneWayLightTimeS:F2} s)"));
            if (c.EarthNeverVisible)
            {
                comm.Details.Add(L.T("⚠ Dünya bu noktadan hiç görünmez: röle uydusu gerekir (ör. Chang'e 4 / Queqiao).",
                                     "⚠ Earth is never visible from this site: a relay satellite is required (e.g. Chang'e 4 / Queqiao)."));
                ev.HardLimitViolations.Add(L.T("Dünya ufkun altında: doğrudan iletişim imkânsız",
                                               "Earth is below the horizon: direct communication is impossible"));
            }
            else if (!c.EarthAlwaysVisible)
                comm.Details.Add(L.T("⚠ Librasyon nedeniyle Dünya zaman zaman ufkun altına iniyor: iletişim kesintileri olur.",
                                     "⚠ Due to libration, Earth periodically drops below the horizon: communication outages will occur."));
        }
        else comm.Summary = notComputed;
        ev.Criteria.Add(comm);

        // 6) YÜKSEKLİK VE ARAZİ YAPISI
        var terrain = new CriterionScore { Name = L.T("Yükseklik ve arazi yapısı", "Elevation and terrain"), Weight = profile.TerrainWeight };
        if (t != null)
        {
            double roughScore = ScoreCurves.Roughness(t.RoughnessRmsM);
            double reliefScore = ScoreCurves.Relief(t.LocalReliefM);
            terrain.Score = Clamp(0.55 * roughScore + 0.45 * reliefScore);
            terrain.Summary = L.T($"Yükseklik {t.ElevationM:F0} m, pürüzlülük {t.RoughnessRmsM:F1} m RMS, rölyef {t.LocalReliefM:F0} m",
                                  $"Elevation {t.ElevationM:F0} m, roughness {t.RoughnessRmsM:F1} m RMS, relief {t.LocalReliefM:F0} m");
            string type = TerrainAnalyzer.ClassifyTerrain(t.ElevationM, t.RoughnessRmsM, t.LocalReliefM);
            t.TerrainType = type;
            terrain.Details.Add(L.T($"Arazi tipi (tahmin): {type}", $"Terrain type (estimate): {type}"));
            terrain.Details.Add(L.T("Yükseklik, Ay referans küresine (R = 1737.4 km) göredir.",
                                    "Elevation is relative to the lunar reference sphere (R = 1737.4 km)."));
            terrain.Details.Add(L.T($"5 km yarıçapta en yüksek ile en alçak nokta arasındaki fark: {t.LocalReliefM:F0} m",
                                    $"Difference between highest and lowest point within 5 km: {t.LocalReliefM:F0} m"));
        }
        else terrain.Summary = noData;
        ev.Criteria.Add(terrain);

        // ---------------- Genel puan ----------------
        double wSum = ev.Criteria.Where(c => c.Score.HasValue).Sum(c => c.Weight);
        ev.OverallScore = wSum > 0
            ? ev.Criteria.Where(c => c.Score.HasValue).Sum(c => c.Score!.Value * c.Weight) / wSum
            : 0;

        ev.Verdict = VerdictText(ev.OverallScore, ev.HardLimitViolations.Count > 0);

        int missing = ev.Criteria.Count(c => !c.Score.HasValue);
        ev.Warnings.Clear();
        foreach (var w in ev.WarningTexts) ev.Warnings.Add(L.T(w[0], w[1]));
        if (missing > 0)
            ev.Warnings.Add(L.T($"{missing} kriter için veri alınamadı; genel puan kalan kriterlerin ağırlıklarına göre hesaplandı.",
                                $"{missing} criteria had no data; the overall score was computed from the remaining criteria weights."));
    }

    public static string VerdictText(double score, bool hardFail)
    {
        if (hardFail) return L.T("UYGUN DEĞİL (kritik sınır aşıldı)", "NOT SUITABLE (critical limit exceeded)");
        if (score >= 80) return L.T("ÇOK UYGUN", "HIGHLY SUITABLE");
        if (score >= 65) return L.T("UYGUN", "SUITABLE");
        if (score >= 50) return L.T("ŞARTLI UYGUN (dikkatli iniş gerekir)", "CONDITIONALLY SUITABLE (careful landing required)");
        return L.T("UYGUN DEĞİL", "NOT SUITABLE");
    }

    public static string Compass(double az)
    {
        string[] tr = { "K", "KKD", "KD", "DKD", "D", "DGD", "GD", "GGD", "G", "GGB", "GB", "BGB", "B", "BKB", "KB", "KKB" };
        string[] en = { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };
        int i = (int)Math.Round(((az % 360) + 360) % 360 / 22.5) % 16;
        return L.En ? en[i] : tr[i];
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

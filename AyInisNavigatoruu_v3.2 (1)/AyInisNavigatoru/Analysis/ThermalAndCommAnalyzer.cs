namespace AyInisNavigatoru.Analysis;

/// <summary>
/// Gündüz sıcaklığı: Ay regolitinin düşük ısıl ataleti nedeniyle gündüz yüzeyi
/// anlık ışınım dengesine çok yakındır:  εσT⁴ = (1 − A(i))·S·cos(i)
/// Albedo'nun geliş açısına bağımlılığı Keihm (1984) / Hayne vd. (2017) modelinden:
///   A(i) = A0 + a·(i/45°)³ + b·(i/90°)⁸ ,  A0 = 0.12, a = 0.06, b = 0.25
/// Gece sıcaklığı: LRO Diviner ölçümlerinden (gece regolit sıcaklığı haritası).
/// </summary>
public static class ThermalAnalyzer
{
    public static double Albedo(double incidenceDeg)
    {
        const double a = 0.06, b = 0.25;
        return Config.BondAlbedo + a * Math.Pow(incidenceDeg / 45.0, 3) + b * Math.Pow(incidenceDeg / 90.0, 8);
    }

    public static double RadiativeEquilibriumK(double cosIncidence)
    {
        if (cosIncidence <= 0) return double.NaN;
        double i = Math.Acos(Math.Clamp(cosIncidence, 0, 1)) * 180 / Math.PI;
        double absorbed = (1 - Albedo(i)) * Config.SolarConstant * cosIncidence;
        return Math.Pow(absorbed / (Config.Emissivity * Config.StefanBoltzmann), 0.25);
    }

    /// <summary>
    /// Diviner verisi alınamazsa kullanılan yedek model: Diviner gözlemlerine göre ekvatorda
    /// gece sonu (gün doğumu öncesi) sıcaklık ≈ 95 K, gece ortalaması ≈ 100-110 K'dir
    /// (Vasavada vd. 2012; Williams vd. 2017). Enleme göre kabaca ölçeklenir.
    /// </summary>
    public static double NightModelK(double latDeg) =>
        105.0 * Math.Pow(Math.Max(0.05, Math.Cos(latDeg * Math.PI / 180)), 0.25);

    public static ThermalResult Analyze(LandingSite site, TerrainResult? terrain, double? divinerNightK)
    {
        double slope = terrain?.SlopeAtSiteDeg ?? 0, aspect = terrain?.AspectAtSiteDeg ?? 0;
        if (double.IsNaN(slope)) { slope = 0; aspect = 0; }

        var (eNoon, azNoon) = SolarAnalyzer.SunPosition(site.LatitudeDeg, 0);
        double noonK = RadiativeEquilibriumK(SolarAnalyzer.CosIncidence(eNoon, azNoon, slope, aspect));

        // İniş penceresinin ortası: güneş ~12° yükseklikte, sabah (doğudan)
        double hLanding = -180;
        for (double h = -180; h <= 0; h += 0.25)
            if (SolarAnalyzer.SunPosition(site.LatitudeDeg, h).ElevationDeg >= 12) { hLanding = h; break; }
        var (eL, azL) = SolarAnalyzer.SunPosition(site.LatitudeDeg, hLanding);
        double landingK = RadiativeEquilibriumK(SolarAnalyzer.CosIncidence(eL, azL, slope, aspect));

        bool hasDiviner = divinerNightK is > 40 and < 300;
        return new ThermalResult
        {
            NoonTemperatureK = noonK,
            LandingWindowTemperatureK = landingK,
            NightTemperatureK = hasDiviner ? divinerNightK!.Value : NightModelK(site.LatitudeDeg),
            NightIsMeasured = hasDiviner,
            NightSource = hasDiviner ? "LRO Diviner" : "Model"
        };
    }
}

/// <summary>
/// Dünya ile doğrudan iletişim (DTE). Ay, Dünya'ya kütleçekimsel kilitli olduğundan
/// Dünya gökyüzünde neredeyse sabit durur; konumu yalnızca librasyonla
/// (boylamda ±7.9°, enlemde ±6.7°) salınır. Alt-Dünya noktası ortalamada (0°, 0°)'dır.
/// </summary>
public static class CommunicationAnalyzer
{
    private static double D2R(double d) => d * Math.PI / 180;
    private static double R2D(double r) => r * 180 / Math.PI;

    private static double AngularDistanceDeg(double lat1, double lon1, double lat2, double lon2)
    {
        double c = Math.Sin(D2R(lat1)) * Math.Sin(D2R(lat2)) +
                   Math.Cos(D2R(lat1)) * Math.Cos(D2R(lat2)) * Math.Cos(D2R(lon2 - lon1));
        return R2D(Math.Acos(Math.Clamp(c, -1, 1)));
    }

    private static double BearingDeg(double lat1, double lon1, double lat2, double lon2)
    {
        double dl = D2R(lon2 - lon1);
        double y = Math.Sin(dl) * Math.Cos(D2R(lat2));
        double x = Math.Cos(D2R(lat1)) * Math.Sin(D2R(lat2)) - Math.Sin(D2R(lat1)) * Math.Cos(D2R(lat2)) * Math.Cos(dl);
        return (R2D(Math.Atan2(y, x)) + 360) % 360;
    }

    public static CommResult Analyze(LandingSite site, HorizonProfile horizon)
    {
        double lat = site.LatitudeDeg, lon = site.LongitudeEastDeg;
        double meanEl = 90 - AngularDistanceDeg(lat, lon, 0, 0);
        double minMargin = double.MaxValue, minEl = double.MaxValue, maxEl = double.MinValue;

        // Librasyon kutusunu tara (en kötü durum)
        for (int i = -4; i <= 4; i++)
        for (int j = -4; j <= 4; j++)
        {
            double subLat = Config.LibrationLatDeg * i / 4.0;
            double subLon = Config.LibrationLonDeg * j / 4.0;
            double el = 90 - AngularDistanceDeg(lat, lon, subLat, subLon);
            double az = BearingDeg(lat, lon, subLat, subLon);
            double margin = el - horizon.At(az);
            minEl = Math.Min(minEl, el);
            maxEl = Math.Max(maxEl, el);
            minMargin = Math.Min(minMargin, margin);
        }

        double earthAz = BearingDeg(lat, lon, 0, 0);
        return new CommResult
        {
            EarthElevationMeanDeg = meanEl,
            EarthElevationMinDeg = minEl,
            EarthElevationMaxDeg = maxEl,
            EarthAzimuthDeg = earthAz,
            TerrainHorizonTowardEarthDeg = horizon.At(earthAz),
            WorstCaseMarginDeg = minMargin,
            OneWayLightTimeS = Config.EarthMoonDistanceKm / Config.SpeedOfLightKmS,
            EarthAlwaysVisible = minMargin > 0,
            EarthNeverVisible = maxEl - horizon.At(earthAz) < 0
        };
    }
}

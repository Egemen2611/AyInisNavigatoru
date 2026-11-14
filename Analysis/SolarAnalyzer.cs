namespace AyInisNavigatoru.Analysis;

/// <summary>
/// Güneş geometrisi. Ay'ın dönme ekseni ekliptiğe yalnızca ~1.54° eğik olduğu için
/// güneşin Ay'daki deklinasyonu ±1.54° aralığında kalır; burada δ = 0 kabul edilir.
/// Bir Ay güneş günü (sinodik ay) 29.53 Dünya günüdür → 1° saat açısı ≈ 1.97 saat.
/// </summary>
public static class SolarAnalyzer
{
    public const double SunAngularRadiusDeg = 0.27;
    public static double HoursPerHourAngleDegree => Config.SynodicMonthDays * 24.0 / 360.0;

    public static double Deg2Rad(double d) => d * Math.PI / 180.0;
    public static double Rad2Deg(double r) => r * 180.0 / Math.PI;
    public static double Norm180(double a) => ((a + 180) % 360 + 360) % 360 - 180;

    /// <summary>Saat açısı H (öğlen = 0, sabah negatif) için güneş yükselti ve azimutu.</summary>
    public static (double ElevationDeg, double AzimuthDeg) SunPosition(double latDeg, double hourAngleDeg, double declinationDeg = 0)
    {
        double phi = Deg2Rad(latDeg), h = Deg2Rad(hourAngleDeg), dec = Deg2Rad(declinationDeg);
        double sinE = Math.Cos(phi) * Math.Cos(h) * Math.Cos(dec) + Math.Sin(phi) * Math.Sin(dec);
        double e = Math.Asin(Math.Clamp(sinE, -1, 1));
        double az = Math.Atan2(-Math.Cos(dec) * Math.Sin(h),
                               Math.Sin(dec) * Math.Cos(phi) - Math.Cos(dec) * Math.Cos(h) * Math.Sin(phi));
        return (Rad2Deg(e), (Rad2Deg(az) + 360) % 360);
    }

    /// <summary>
    /// Güneş-altı noktanın (subsolar point) yaklaşık boylamı.
    /// Yeni ayda ≈180° (uzak yüz), dolunayda ≈0° (yakın yüzün merkezi).
    /// Librasyon nedeniyle hata payı birkaç derece / ~±0.5 gündür.
    /// </summary>
    public static double SubsolarLongitude(DateTime utc)
    {
        double days = (utc - Config.ReferenceNewMoonUtc).TotalDays;
        double age = ((days % Config.SynodicMonthDays) + Config.SynodicMonthDays) % Config.SynodicMonthDays;
        return Norm180(180.0 - 360.0 * age / Config.SynodicMonthDays);
    }

    public static double CurrentHourAngle(LandingSite site, DateTime utc) =>
        Norm180(site.LongitudeEastDeg - SubsolarLongitude(utc));

    public static SunResult Analyze(LandingSite site, HorizonProfile horizon, DateTime nowUtc,
                                    double windowMinElevDeg = 7, double windowMaxElevDeg = 20)
    {
        var res = new SunResult();
        const double step = 0.25;
        int lit = 0, flatLit = 0, total = 0;
        double? firstLitH = null, lastLitH = null;
        double? windowStartH = null, windowEndH = null;

        for (double h = -180; h < 180; h += step)
        {
            var (e, az) = SunPosition(site.LatitudeDeg, h);
            bool flat = e + SunAngularRadiusDeg > 0;
            bool visible = e + SunAngularRadiusDeg > horizon.At(az);
            total++;
            if (flat) flatLit++;
            if (visible)
            {
                lit++;
                firstLitH ??= h;
                lastLitH = h;
                // Sabah iniş penceresi: Apollo inişleri güneş 5°-15° yüksekteyken yapıldı;
                // alçak güneş kraterlerin ve kayaların uzun gölge vermesini sağlar.
                if (h < 0 && e >= windowMinElevDeg && e <= windowMaxElevDeg)
                {
                    windowStartH ??= h;
                    windowEndH = h;
                }
            }
        }

        res.NoonElevationDeg = SunPosition(site.LatitudeDeg, 0).ElevationDeg;
        res.IlluminatedFraction = (double)lit / total;
        res.FlatTerrainFraction = (double)flatLit / total;
        res.DaylightEarthDays = res.IlluminatedFraction * Config.SynodicMonthDays;

        // Düz arazide gün doğumu/batımı: H = -90 / +90 (δ = 0 için)
        if (firstLitH.HasValue) res.SunriseDelayHours = Math.Max(0, (firstLitH.Value + 90) * HoursPerHourAngleDegree);
        if (lastLitH.HasValue) res.SunsetAdvanceHours = Math.Max(0, (90 - lastLitH.Value) * HoursPerHourAngleDegree);

        double hNow = CurrentHourAngle(site, nowUtc);
        var (eNow, azNow) = SunPosition(site.LatitudeDeg, hNow);
        res.CurrentSunElevationDeg = eNow;
        res.CurrentlyDaylight = eNow + SunAngularRadiusDeg > horizon.At(azNow);

        if (windowStartH.HasValue && windowEndH.HasValue)
        {
            res.LandingWindowFound = true;
            res.NextLandingWindowStartUtc = nowUtc.AddHours(HoursUntil(hNow, windowStartH.Value));
            res.NextLandingWindowEndUtc = res.NextLandingWindowStartUtc.AddHours(
                (windowEndH.Value - windowStartH.Value) * HoursPerHourAngleDegree);
            // Şu an pencerenin içindeysek, bir sonraki pencereye değil şimdikine işaret et
            if (hNow >= windowStartH.Value && hNow <= windowEndH.Value)
            {
                res.NextLandingWindowStartUtc = nowUtc;
                res.NextLandingWindowEndUtc = nowUtc.AddHours((windowEndH.Value - hNow) * HoursPerHourAngleDegree);
            }
        }
        return res;
    }

    private static double HoursUntil(double hNow, double hTarget)
    {
        double dH = ((hTarget - hNow) % 360 + 360) % 360;
        return dH * HoursPerHourAngleDegree;
    }

    /// <summary>Eğimli bir yüzeyde güneş geliş açısının kosinüsü.</summary>
    public static double CosIncidence(double sunElevDeg, double sunAzDeg, double slopeDeg, double aspectDeg)
    {
        double e = Deg2Rad(sunElevDeg), s = Deg2Rad(slopeDeg);
        double cosI = Math.Sin(e) * Math.Cos(s) + Math.Cos(e) * Math.Sin(s) * Math.Cos(Deg2Rad(sunAzDeg - aspectDeg));
        return Math.Max(0, cosI);
    }
}

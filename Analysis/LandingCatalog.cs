namespace AyInisNavigatoru.Analysis;

public sealed record HistoricLanding(string Name, string Country, DateTime Date, double LatDeg, double Lon360, bool Crewed)
{
    public double LonDeg => SolarAnalyzer.Norm180(Lon360);
    public LandingSite ToSite() => new($"{Name} ({Date:yyyy})", LatDeg, LonDeg);
}

/// <summary>
/// Ay'a yapılmış başarılı yumuşak inişler.
/// Koordinatlar: NASA NSSDC "Table of Anthropogenic Impacts and Spacecraft on the Moon"
/// (https://nssdc.gsfc.nasa.gov/planetary/lunar/lunar_artifact_impacts.html), doğu boylamı 0-360°.
/// Apollo için iniş modülü (LM) iniş katının LRO kamerasıyla ölçülmüş konumu kullanılmıştır.
/// </summary>
public static class LandingCatalog
{
    public static readonly HistoricLanding[] All =
    {
        new("Luna 9", "SSCB", new DateTime(1966, 2, 3), 7.13, 295.63, false),
        new("Surveyor 1", "ABD", new DateTime(1966, 6, 2), -2.4745, 316.6602, false),
        new("Luna 13", "SSCB", new DateTime(1966, 12, 24), 18.87, 297.95, false),
        new("Surveyor 3", "ABD", new DateTime(1967, 4, 20), -3.0162, 336.5820, false),
        new("Surveyor 5", "ABD", new DateTime(1967, 9, 11), 1.4551, 23.1943, false),
        new("Surveyor 6", "ABD", new DateTime(1967, 11, 10), 0.4742, 358.5725, false),
        new("Surveyor 7", "ABD", new DateTime(1968, 1, 10), -40.9812, 348.4873, false),
        new("Apollo 11", "ABD", new DateTime(1969, 7, 20), 0.67416, 23.47314, true),
        new("Apollo 12", "ABD", new DateTime(1969, 11, 19), -3.0128, 336.5781, true),
        new("Luna 16", "SSCB", new DateTime(1970, 9, 20), -0.5137, 56.3638, false),
        new("Luna 17 / Lunokhod 1", "SSCB", new DateTime(1970, 11, 17), 38.23764, 324.99837, false),
        new("Apollo 14", "ABD", new DateTime(1971, 2, 5), -3.64589, 342.52806, true),
        new("Apollo 15", "ABD", new DateTime(1971, 7, 30), 26.13239, 3.63330, true),
        new("Luna 20", "SSCB", new DateTime(1972, 2, 21), 3.7863, 56.6242, false),
        new("Apollo 16", "ABD", new DateTime(1972, 4, 21), -8.9734, 15.5011, true),
        new("Apollo 17", "ABD", new DateTime(1972, 12, 11), 20.1911, 30.7723, true),
        new("Luna 21 / Lunokhod 2", "SSCB", new DateTime(1973, 1, 15), 25.9994, 30.4076, false),
        new("Luna 23", "SSCB", new DateTime(1974, 11, 6), 12.6667, 62.1511, false),
        new("Luna 24", "SSCB", new DateTime(1976, 8, 18), 12.7142, 62.2129, false),
        new("Chang'e 3", "Çin", new DateTime(2013, 12, 14), 44.1214, 340.4883, false),
        new("Chang'e 4 (uzak yüz)", "Çin", new DateTime(2019, 1, 3), -45.4561, 177.5885, false),
        new("Chang'e 5", "Çin", new DateTime(2020, 12, 1), 43.0576, 308.0839, false),
        new("Chandrayaan-3", "Hindistan", new DateTime(2023, 8, 23), -69.3741, 32.32, false),
        new("SLIM", "Japonya", new DateTime(2024, 1, 19), -13.3160, 25.2510, false),
        new("IM-1 Odysseus", "ABD (özel)", new DateTime(2024, 2, 22), -80.13, 1.44, false),
        new("Chang'e 6 (uzak yüz)", "Çin", new DateTime(2024, 6, 1), -41.6385, 206.0148, false),
        new("Blue Ghost 1", "ABD (özel)", new DateTime(2025, 3, 2), 18.562, 61.810, false),
        new("IM-2 Athena", "ABD (özel)", new DateTime(2025, 3, 6), -84.7906, 29.1957, false),
    };

    /// <summary>İki nokta arasındaki büyük daire (yüzey) uzaklığı, km</summary>
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        double p1 = lat1 * Math.PI / 180, p2 = lat2 * Math.PI / 180, dl = (lon2 - lon1) * Math.PI / 180;
        double a = Math.Pow(Math.Sin((p2 - p1) / 2), 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Pow(Math.Sin(dl / 2), 2);
        return 2 * Config.MoonRadiusM / 1000 * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    public static (HistoricLanding Landing, double DistanceKm) Nearest(LandingSite site) =>
        All.Select(l => (l, DistanceKm(site.LatitudeDeg, site.LongitudeEastDeg, l.LatDeg, l.LonDeg)))
           .OrderBy(x => x.Item2).First();
}

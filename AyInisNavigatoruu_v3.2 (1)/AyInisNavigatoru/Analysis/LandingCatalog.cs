namespace AyInisNavigatoru.Analysis;

public sealed record HistoricLanding(string BaseName, string CountryCode, DateTime Date, double LatDeg, double Lon360, bool Crewed, bool FarSide = false)
{
    public double LonDeg => SolarAnalyzer.Norm180(Lon360);
    public string Name => FarSide ? BaseName + L.T(" (uzak yüz)", " (far side)") : BaseName;
    public string Country => CountryCode switch
    {
        "USSR" => L.T("SSCB", "USSR"),
        "USA" => L.T("ABD", "USA"),
        "USA-C" => L.T("ABD (özel)", "USA (commercial)"),
        "CHN" => L.T("Çin", "China"),
        "IND" => L.T("Hindistan", "India"),
        "JPN" => L.T("Japonya", "Japan"),
        _ => CountryCode
    };
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
        new("Luna 9", "USSR", new DateTime(1966, 2, 3), 7.13, 295.63, false),
        new("Surveyor 1", "USA", new DateTime(1966, 6, 2), -2.4745, 316.6602, false),
        new("Luna 13", "USSR", new DateTime(1966, 12, 24), 18.87, 297.95, false),
        new("Surveyor 3", "USA", new DateTime(1967, 4, 20), -3.0162, 336.5820, false),
        new("Surveyor 5", "USA", new DateTime(1967, 9, 11), 1.4551, 23.1943, false),
        new("Surveyor 6", "USA", new DateTime(1967, 11, 10), 0.4742, 358.5725, false),
        new("Surveyor 7", "USA", new DateTime(1968, 1, 10), -40.9812, 348.4873, false),
        new("Apollo 11", "USA", new DateTime(1969, 7, 20), 0.67416, 23.47314, true),
        new("Apollo 12", "USA", new DateTime(1969, 11, 19), -3.0128, 336.5781, true),
        new("Luna 16", "USSR", new DateTime(1970, 9, 20), -0.5137, 56.3638, false),
        new("Luna 17 / Lunokhod 1", "USSR", new DateTime(1970, 11, 17), 38.23764, 324.99837, false),
        new("Apollo 14", "USA", new DateTime(1971, 2, 5), -3.64589, 342.52806, true),
        new("Apollo 15", "USA", new DateTime(1971, 7, 30), 26.13239, 3.63330, true),
        new("Luna 20", "USSR", new DateTime(1972, 2, 21), 3.7863, 56.6242, false),
        new("Apollo 16", "USA", new DateTime(1972, 4, 21), -8.9734, 15.5011, true),
        new("Apollo 17", "USA", new DateTime(1972, 12, 11), 20.1911, 30.7723, true),
        new("Luna 21 / Lunokhod 2", "USSR", new DateTime(1973, 1, 15), 25.9994, 30.4076, false),
        new("Luna 23", "USSR", new DateTime(1974, 11, 6), 12.6667, 62.1511, false),
        new("Luna 24", "USSR", new DateTime(1976, 8, 18), 12.7142, 62.2129, false),
        new("Chang'e 3", "CHN", new DateTime(2013, 12, 14), 44.1214, 340.4883, false),
        new("Chang'e 4", "CHN", new DateTime(2019, 1, 3), -45.4561, 177.5885, false, true),
        new("Chang'e 5", "CHN", new DateTime(2020, 12, 1), 43.0576, 308.0839, false),
        new("Chandrayaan-3", "IND", new DateTime(2023, 8, 23), -69.3741, 32.32, false),
        new("SLIM", "JPN", new DateTime(2024, 1, 19), -13.3160, 25.2510, false),
        new("IM-1 Odysseus", "USA-C", new DateTime(2024, 2, 22), -80.13, 1.44, false),
        new("Chang'e 6", "CHN", new DateTime(2024, 6, 1), -41.6385, 206.0148, false, true),
        new("Blue Ghost 1", "USA-C", new DateTime(2025, 3, 2), 18.562, 61.810, false),
        new("IM-2 Athena", "USA-C", new DateTime(2025, 3, 6), -84.7906, 29.1957, false),
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

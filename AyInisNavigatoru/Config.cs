namespace AyInisNavigatoru;

/// <summary>
/// Veri kaynakları ve bilimsel sabitler.
/// Tüm veriler NASA Lunar Reconnaissance Orbiter (LRO) görevinin
/// Planetary Data System (PDS) arşivlerinden canlı olarak çekilir.
/// </summary>
public static class Config
{
    // ---------------- LRO / LOLA (Lunar Orbiter Laser Altimeter) ----------------
    // Resmi LOLA PDS veri düğümü (MIT / NASA GSFC).
    // LDEM_128 : 128 piksel/derece (~237 m/piksel) global yükseklik modeli, 16-bit
    // LDEM_64  :  64 piksel/derece (~474 m/piksel) global yükseklik modeli, 16-bit
    public const string LolaBaseUrl = "https://imbrium.mit.edu/DATA/LOLA_GDR/CYLINDRICAL/IMG/";
    public const string LolaDemHighRes = "LDEM_128";
    public const string LolaDemLowRes = "LDEM_64";

    // ---------------- LRO / DIVINER (Lunar Radiometer Experiment) ---------------
    // PDS Geosciences Node, Diviner GDR Level 3 ürünleri.
    // dgdr_ra_avg_cyl_128 : Kaya yoğunluğu (Rock Abundance, Bandfield vd. 2011), 128 ppd
    // dgdr_st_avg_cyl_032 : Gece regolit (toprak) sıcaklığı, 32 ppd
    public const string DivinerBaseUrl =
        "https://pds-geosciences.wustl.edu/lro/lro-l-dlre-4-rdr-v1/lrodlr_1001/data/gdr_l3/cylindrical/img/";
    public const string DivinerRockAbundance = "dgdr_ra_avg_cyl_128_img";
    public const string DivinerSoilTemperature = "dgdr_st_avg_cyl_032_img";
    // dgdr_ra_avg_cyl_032 : Kaya yoğunluğu, 32 ppd — küresel uygunluk haritası için (tamamı ~90 MB, bir kez indirilir)
    public const string DivinerRockAbundanceGlobal = "dgdr_ra_avg_cyl_032_img";

    // ---------------- Analiz ayarları ----------------
    public const double DefaultLandingEllipseRadiusM = 2000;  // İniş elipsi yarıçapı (m)
    public const double CraterSearchRadiusM = 5000;           // Krater arama yarıçapı (m)
    public const double HorizonMaxDistanceM = 30000;          // Ufuk taraması mesafesi (m)
    public const double LanderMastHeightM = 2.0;              // Gözlemci/anten yüksekliği (m)

    public const int HighResBlockHalfRows = 40;  // LDEM_128 için ±satır (~9.5 km)
    public const int LowResBlockHalfRows = 70;   // LDEM_64 için ±satır (~33 km)
    public const int DivinerBlockHalfRows = 10;

    public const string CacheDirectory = "veri_onbellek";
    public const string ReportDirectory = "raporlar";

    // ---------------- Fiziksel sabitler ----------------
    public const double MoonRadiusM = 1737400.0;          // LOLA referans yarıçapı
    public const double SolarConstant = 1361.0;           // W/m² (1 AU, Kopp & Lean 2011)
    public const double StefanBoltzmann = 5.670374419e-8; // W/m²K⁴
    public const double BondAlbedo = 0.12;                // A0 (Hayne vd. 2017)
    public const double Emissivity = 0.95;                // Kızılötesi yayınım (Hayne vd. 2017)
    public const double SynodicMonthDays = 29.530588853;  // Ay'ın güneş günü
    public const double EarthMoonDistanceKm = 384400;
    public const double SpeedOfLightKmS = 299792.458;

    // Ay'ın optik librasyon genlikleri (derece)
    public const double LibrationLonDeg = 7.9;
    public const double LibrationLatDeg = 6.7;

    // Referans yeni ay: 6 Ocak 2000, 18:14 UTC (Meeus, Astronomical Algorithms)
    public static readonly DateTime ReferenceNewMoonUtc = new(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
}

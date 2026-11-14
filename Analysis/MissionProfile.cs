namespace AyInisNavigatoru.Analysis;

/// <summary>
/// Görev profili: altı kriterin ağırlıkları ve göreve özel kritik sınırlar.
/// Ağırlıkların toplamı 100'dür. Eşikler gerçek görev gereksinimlerine dayanır.
/// </summary>
public sealed class MissionProfile
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";

    public double SlopeWeight { get; init; }
    public double HazardWeight { get; init; }     // krater ve taş yoğunluğu
    public double SunWeight { get; init; }
    public double ThermalWeight { get; init; }
    public double CommWeight { get; init; }
    public double TerrainWeight { get; init; }

    /// <summary>Bu eğimin üzerindeki noktalar kesin olarak reddedilir (derece).</summary>
    public double MaxSlopeDeg { get; init; } = 15;
    /// <summary>Bu kaya yoğunluğunun üzerindeki noktalar kesin olarak reddedilir (oran, 0-1).</summary>
    public double MaxRockAbundance { get; init; } = 0.10;

    public string WeightSummary =>
        $"Eğim %{SlopeWeight:F0} · Krater/taş %{HazardWeight:F0} · Güneş %{SunWeight:F0} · " +
        $"Sıcaklık %{ThermalWeight:F0} · İletişim %{CommWeight:F0} · Arazi %{TerrainWeight:F0}  |  " +
        $"Kritik sınır: eğim > {MaxSlopeDeg:F0}°, kaya > %{MaxRockAbundance * 100:F0}";

    public override string ToString() => Name;

    // ------------------------------------------------------------------
    public static readonly MissionProfile Standard = new()
    {
        Name = "Dengeli (standart)",
        Description = "Tüm kriterleri dengeli değerlendirir; ilk sürümdeki puanlamanın aynısıdır. " +
                      "Görev türü belli değilse bunu seçin.",
        SlopeWeight = 25, HazardWeight = 20, SunWeight = 15, ThermalWeight = 10, CommWeight = 15, TerrainWeight = 15,
        MaxSlopeDeg = 15, MaxRockAbundance = 0.10
    };

    public static readonly MissionProfile RoboticShort = new()
    {
        Name = "Kısa robotik görev (CLPS tipi)",
        Description = "Tek bir Ay gündüzü (~14 Dünya günü) çalışan insansız iniş aracı. Gece dayanımı beklenmediği için " +
                      "sıcaklık önemsizdir; asıl risk iniş anıdır, bu yüzden eğim ile krater ve taşlara en yüksek ağırlık verilir. " +
                      "Örnek: Intuitive Machines IM-1, Firefly Blue Ghost.",
        SlopeWeight = 30, HazardWeight = 30, SunWeight = 15, ThermalWeight = 5, CommWeight = 15, TerrainWeight = 5,
        MaxSlopeDeg = 12, MaxRockAbundance = 0.08
    };

    public static readonly MissionProfile Crewed = new()
    {
        Name = "İnsanlı iniş (Artemis tipi)",
        Description = "Astronot güvenliği her şeyin önündedir. NASA'nın Artemis iniş aracı gereksinimine uygun olarak " +
                      "8°'nin üzerindeki eğimler büyük ceza alır, 10° üzeri kesin olarak reddedilir. Krater ve kayalar " +
                      "ile Dünya iletişimi de kritik önemdedir.",
        SlopeWeight = 30, HazardWeight = 25, SunWeight = 10, ThermalWeight = 10, CommWeight = 15, TerrainWeight = 10,
        MaxSlopeDeg = 10, MaxRockAbundance = 0.05
    };

    public static readonly MissionProfile Base = new()
    {
        Name = "Kalıcı Ay üssü",
        Description = "Yıllarca kullanılacak bir üs. İniş tek seferlik değil, sürekli enerji (güneş), Dünya ile kesintisiz " +
                      "haberleşme ve yaşanabilir sıcaklıklar öne çıkar. Yapılaşma için krater ve taşlar yine önemlidir.",
        SlopeWeight = 15, HazardWeight = 15, SunWeight = 25, ThermalWeight = 15, CommWeight = 20, TerrainWeight = 10,
        MaxSlopeDeg = 15, MaxRockAbundance = 0.10
    };

    public static readonly MissionProfile Rover = new()
    {
        Name = "Gezgin (rover) görevi",
        Description = "İnişten sonra kilometrelerce yol alacak bir gezgin. İniş noktasının yanında çevrenin de geniş, " +
                      "düz ve az kayalık olması gerekir; bu yüzden arazi yapısı (pürüzlülük, rölyef) ile krater ve " +
                      "taşlar yüksek ağırlık alır. Örnek: Yutu-2, VIPER.",
        SlopeWeight = 20, HazardWeight = 25, SunWeight = 15, ThermalWeight = 5, CommWeight = 15, TerrainWeight = 20,
        MaxSlopeDeg = 15, MaxRockAbundance = 0.08
    };

    public static readonly MissionProfile[] All = { Standard, RoboticShort, Crewed, Base, Rover };
}

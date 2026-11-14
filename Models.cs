namespace AyInisNavigatoru;

public sealed record LandingSite(string Name, double LatitudeDeg, double LongitudeEastDeg)
{
    /// <summary>0-360 aralığında doğu boylamı</summary>
    public double Lon360 => ((LongitudeEastDeg % 360) + 360) % 360;
}

public sealed record Crater(double LatDeg, double LonDeg, double DiameterM, double DepthM, double DistanceFromSiteM)
{
    public double DepthToDiameter => DepthM / DiameterM;
}

public sealed class TerrainResult
{
    public string DemProduct { get; set; } = "";
    public double PixelSizeM { get; set; }
    public double ElevationM { get; set; }
    public double SlopeAtSiteDeg { get; set; }
    public double AspectAtSiteDeg { get; set; }
    public double SlopeMeanDeg { get; set; }
    public double SlopeP90Deg { get; set; }
    public double SlopeMaxDeg { get; set; }
    public double PercentOver10Deg { get; set; }
    public double PercentOver15Deg { get; set; }
    public double RoughnessRmsM { get; set; }
    public double LocalReliefM { get; set; }
    public string TerrainType { get; set; } = "";
    public double EllipseRadiusM { get; set; }

    public double CraterSearchRadiusM { get; set; }
    public List<Crater> Craters { get; set; } = new();
    public double CraterDensityPer100Km2 { get; set; }
    public bool SiteInsideCrater { get; set; }
    public double NearestCraterRimDistanceM { get; set; } = double.NaN;
}

public sealed class HorizonProfile
{
    public string DemProduct { get; set; } = "";
    /// <summary>Kuzeyden saat yönünde 0..350, 10° adımlı ufuk yükselti açıları (derece)</summary>
    public double[] ElevationDeg { get; set; } = new double[36];
    public bool IsFlatAssumption { get; set; }

    public double At(double azimuthDeg)
    {
        double a = ((azimuthDeg % 360) + 360) % 360 / 10.0;
        int i0 = (int)Math.Floor(a) % 36;
        int i1 = (i0 + 1) % 36;
        double f = a - Math.Floor(a);
        return ElevationDeg[i0] * (1 - f) + ElevationDeg[i1] * f;
    }
}

public sealed class SunResult
{
    public double NoonElevationDeg { get; set; }
    public double IlluminatedFraction { get; set; }
    public double FlatTerrainFraction { get; set; }
    public double DaylightEarthDays { get; set; }
    public double SunriseDelayHours { get; set; }
    public double SunsetAdvanceHours { get; set; }
    public double CurrentSunElevationDeg { get; set; }
    public bool CurrentlyDaylight { get; set; }
    public DateTime NextLandingWindowStartUtc { get; set; }
    public DateTime NextLandingWindowEndUtc { get; set; }
    public bool LandingWindowFound { get; set; }
}

public sealed class ThermalResult
{
    public double NoonTemperatureK { get; set; }
    public double LandingWindowTemperatureK { get; set; }
    public double NightTemperatureK { get; set; }
    public string NightSource { get; set; } = "";
}

public sealed class RockResult
{
    public double RockAbundanceAtSite { get; set; }
    public double RockAbundanceMean { get; set; }
    public double RockAbundanceMax { get; set; }
}

public sealed class CommResult
{
    public double EarthElevationMeanDeg { get; set; }
    public double EarthElevationMinDeg { get; set; }
    public double EarthElevationMaxDeg { get; set; }
    public double EarthAzimuthDeg { get; set; }
    public double TerrainHorizonTowardEarthDeg { get; set; }
    public double WorstCaseMarginDeg { get; set; }
    public double OneWayLightTimeS { get; set; }
    public bool EarthAlwaysVisible { get; set; }
    public bool EarthNeverVisible { get; set; }
}

public sealed class CriterionScore
{
    public string Name { get; set; } = "";
    public double Weight { get; set; }
    public double? Score { get; set; }   // null = veri yok
    public string Summary { get; set; } = "";
    public List<string> Details { get; set; } = new();
}

public sealed class SiteEvaluation
{
    public LandingSite Site { get; set; } = new("", 0, 0);
    public string ProfileName { get; set; } = "";
    public DateTime EvaluatedAtUtc { get; set; }
    public TerrainResult? Terrain { get; set; }
    public HorizonProfile? Horizon { get; set; }
    public SunResult? Sun { get; set; }
    public ThermalResult? Thermal { get; set; }
    public RockResult? Rocks { get; set; }
    public CommResult? Communication { get; set; }
    public List<CriterionScore> Criteria { get; set; } = new();
    public double OverallScore { get; set; }
    public string Verdict { get; set; } = "";
    public List<string> HardLimitViolations { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> DataSources { get; set; } = new();
}

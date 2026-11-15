using AyInisNavigatoru.Data;

namespace AyInisNavigatoru.Analysis;

/// <summary>
/// AY'IN TAMAMI İÇİN UYGUNLUK HARİTASI (0.25° ≈ 7.6 km'lik hücreler, 1440 × 720 hücre).
///
/// Her hücre, tekli analizdeki AYNI puan eğrileriyle (ScoreCurves) ve AYNI görev profili
/// ağırlıklarıyla puanlanır. Kullanılan veriler:
///   • Eğim, pürüzlülük, rölyef, krater duvarları : LRO LOLA LDEM_16 (16 piksel/derece ≈ 1.9 km)
///   • Kaya yoğunluğu                            : LRO Diviner GDR L3 kaya haritası (32 piksel/derece)
///   • Güneş, sıcaklık, Dünya iletişimi           : fiziksel modeller (enlem/boylama göre)
///
/// ÖNEMLİ: Küresel harita ~2 km ölçeğinde bir ÖN ELEME haritasıdır. Bu ölçekte eğimler
/// iniş aracı ölçeğindekinden düşük görünür. Kesin karar için noktaya tıklayıp
/// 237 m çözünürlüklü tekli analiz yapılmalıdır.
/// </summary>
public sealed class GlobalSuitabilityMap
{
    public const double Resolution = 4;   // hücre/derece
    public int Width { get; } = 1440;
    public int Height { get; } = 720;

    // Ham ölçümler (hücre başına)
    private readonly float[] _slopeMean, _slopeMax, _steep15Pct, _wall10Pct, _reliefM, _roughM, _commMinEl, _commMaxEl;
    private readonly float[] _rockMean, _rockMax;
    private readonly double[] _noonK, _nightK, _noonElev;   // satır (enlem) başına

    // Kriter puanları
    public float[] SlopeScore { get; }
    public float[] HazardScore { get; private set; }
    public float[] SunScore { get; }
    public float[] ThermalScore { get; }
    public float[] CommScore { get; }
    public float[] TerrainScore { get; }
    public float[] Overall { get; private set; }
    public bool[] HardFail { get; private set; }

    public bool HasRocks { get; private set; }
    public string ProfileName { get; private set; } = "";
    public string DemProduct { get; }
    public double MeanOverall { get; private set; }
    public double PercentSuitable { get; private set; }   // puanı ≥ 65 ve kritik sınırı aşmayan yüzey oranı (alan ağırlıklı)

    private GlobalSuitabilityMap(int n, string demProduct)
    {
        DemProduct = demProduct;
        _slopeMean = new float[n]; _slopeMax = new float[n]; _steep15Pct = new float[n]; _wall10Pct = new float[n];
        _reliefM = new float[n]; _roughM = new float[n]; _commMinEl = new float[n]; _commMaxEl = new float[n];
        _rockMean = new float[n]; _rockMax = new float[n];
        Array.Fill(_rockMean, float.NaN); Array.Fill(_rockMax, float.NaN);
        _noonK = new double[Height]; _nightK = new double[Height]; _noonElev = new double[Height];
        SlopeScore = new float[n]; HazardScore = new float[n]; SunScore = new float[n]; ThermalScore = new float[n];
        CommScore = new float[n]; TerrainScore = new float[n]; Overall = new float[n]; HardFail = new bool[n];
    }

    // =====================================================================
    public static GlobalSuitabilityMap Build(float[] elevation, int srcW, int srcH, double srcRes, string product)
    {
        var m = new GlobalSuitabilityMap(1440 * 720, product);
        int k = Math.Max(1, (int)Math.Round(srcRes / Resolution));   // bir hücredeki kaynak piksel sayısı (kenar)
        double dy = Math.PI * Config.MoonRadiusM / (180.0 * srcRes);

        float H(int r, int c)
        {
            r = Math.Clamp(r, 0, srcH - 1);
            c = ((c % srcW) + srcW) % srcW;
            return elevation[r * srcW + c];
        }

        Parallel.For(0, m.Height, i =>
        {
            double latC = 90 - (i + 0.5) / Resolution;
            var pts = new (double x, double y, double z)[k * k];
            for (int j = 0; j < m.Width; j++)
            {
                int idx = i * m.Width + j;
                int r0 = i * k, c0 = j * k;
                double sum = 0, max = 0; int steep15 = 0, wall10 = 0, cnt = 0;
                double zmin = double.MaxValue, zmax = double.MinValue;
                double sx = 0, sy = 0, sz = 0, sxx = 0, syy = 0, sxy = 0, sxz = 0, syz = 0;

                for (int a = 0; a < k; a++)
                {
                    int r = r0 + a;
                    double lat = 90 - (r + 0.5) / srcRes;
                    double dx = Math.Max(50, dy * Math.Cos(lat * Math.PI / 180));
                    for (int b = 0; b < k; b++)
                    {
                        int c = c0 + b;
                        // Horn (1981) eğimi
                        double z1 = H(r - 1, c - 1), z2 = H(r - 1, c), z3 = H(r - 1, c + 1);
                        double z4 = H(r, c - 1), z5 = H(r, c), z6 = H(r, c + 1);
                        double z7 = H(r + 1, c - 1), z8 = H(r + 1, c), z9 = H(r + 1, c + 1);
                        double dzdx = ((z3 + 2 * z6 + z9) - (z1 + 2 * z4 + z7)) / (8 * dx);
                        double dzdy = ((z1 + 2 * z2 + z3) - (z7 + 2 * z8 + z9)) / (8 * dy);
                        double s = Math.Atan(Math.Sqrt(dzdx * dzdx + dzdy * dzdy)) * 180 / Math.PI;
                        sum += s; max = Math.Max(max, s); cnt++;
                        if (s > 15) steep15++;
                        if (s > 10) wall10++;

                        zmin = Math.Min(zmin, z5); zmax = Math.Max(zmax, z5);
                        double x = b * dx, y = -a * dy;
                        pts[a * k + b] = (x, y, z5);
                        sx += x; sy += y; sz += z5; sxx += x * x; syy += y * y; sxy += x * y; sxz += x * z5; syz += y * z5;
                    }
                }

                m._slopeMean[idx] = (float)(sum / cnt);
                m._slopeMax[idx] = (float)max;
                m._steep15Pct[idx] = 100f * steep15 / cnt;
                m._wall10Pct[idx] = 100f * wall10 / cnt;
                m._reliefM[idx] = (float)(zmax - zmin);
                m._roughM[idx] = (float)PlaneResidualRms(pts, cnt, sx, sy, sz, sxx, syy, sxy, sxz, syz);

                // Dünya iletişimi (librasyon kutusunun köşeleri ve kenar ortaları; arazi ufku yok sayılır)
                double lonC = (j + 0.5) / Resolution;
                double minEl = double.MaxValue, maxEl = double.MinValue;
                for (int u = -1; u <= 1; u++)
                for (int v = -1; v <= 1; v++)
                {
                    double el = 90 - AngularDistance(latC, lonC, u * Config.LibrationLatDeg, v * Config.LibrationLonDeg);
                    minEl = Math.Min(minEl, el); maxEl = Math.Max(maxEl, el);
                }
                m._commMinEl[idx] = (float)minEl;
                m._commMaxEl[idx] = (float)maxEl;
            }

            // Enleme bağlı değerler: düz yüzey, deklinasyon 0
            m._noonElev[i] = 90 - Math.Abs(latC);
            m._noonK[i] = ThermalAnalyzer.RadiativeEquilibriumK(Math.Cos(latC * Math.PI / 180));
            m._nightK[i] = ThermalAnalyzer.NightModelK(latC);
        });

        m.ComputeStaticScores();
        m.ComputeHazard();
        m.ApplyProfile(MissionProfile.Standard);
        return m;
    }

    private static double PlaneResidualRms((double x, double y, double z)[] p, int n, double sx, double sy, double sz,
                                           double sxx, double syy, double sxy, double sxz, double syz)
    {
        if (n < 4) return 0;
        double det = n * (sxx * syy - sxy * sxy) - sx * (sx * syy - sxy * sy) + sy * (sx * sxy - sxx * sy);
        if (Math.Abs(det) < 1e-9) return 0;
        double p0 = (sz * (sxx * syy - sxy * sxy) - sx * (sxz * syy - sxy * syz) + sy * (sxz * sxy - sxx * syz)) / det;
        double p1 = (n * (sxz * syy - syz * sxy) - sz * (sx * syy - sxy * sy) + sy * (sx * syz - sxz * sy)) / det;
        double p2 = (n * (sxx * syz - sxy * sxz) - sx * (sx * syz - sxz * sy) + sz * (sx * sxy - sxx * sy)) / det;
        double ss = 0;
        for (int i = 0; i < n; i++) { double r = p[i].z - (p0 + p1 * p[i].x + p2 * p[i].y); ss += r * r; }
        return Math.Sqrt(ss / n);
    }

    private static double AngularDistance(double lat1, double lon1, double lat2, double lon2)
    {
        double d = Math.PI / 180;
        double c = Math.Sin(lat1 * d) * Math.Sin(lat2 * d) + Math.Cos(lat1 * d) * Math.Cos(lat2 * d) * Math.Cos((lon2 - lon1) * d);
        return Math.Acos(Math.Clamp(c, -1, 1)) / d;
    }

    private void ComputeStaticScores()
    {
        Parallel.For(0, Height, i =>
        {
            double sun = 0.5 * ScoreCurves.SunElevation(_noonElev[i]) + 0.5 * ScoreCurves.Illumination(1.0);
            double thermal = 0.5 * ScoreCurves.Hot(_noonK[i]) + 0.5 * ScoreCurves.Cold(_nightK[i]);
            for (int j = 0; j < Width; j++)
            {
                int idx = i * Width + j;
                SlopeScore[idx] = (float)Math.Clamp(0.45 * ScoreCurves.Slope(_slopeMean[idx]) +
                                                    0.35 * ScoreCurves.Slope(_slopeMax[idx]) +
                                                    0.20 * ScoreCurves.SteepFraction(_steep15Pct[idx]), 0, 100);
                SunScore[idx] = (float)sun;
                ThermalScore[idx] = (float)thermal;
                CommScore[idx] = _commMaxEl[idx] < 0 ? 0f : (float)Math.Clamp(ScoreCurves.CommMargin(_commMinEl[idx]), 0, 100);
                TerrainScore[idx] = (float)Math.Clamp(0.55 * ScoreCurves.Roughness(_roughM[idx]) +
                                                      0.45 * ScoreCurves.Relief(_reliefM[idx]), 0, 100);
            }
        });
    }

    /// <summary>
    /// Krater ve taş tehlikesi: %60 Diviner kaya yoğunluğu + %40 krater duvarı oranı
    /// (hücrede 10°'den dik piksellerin oranı; ~2 km ölçeğinde dik yüzeyler büyük ölçüde krater duvarlarıdır).
    /// Kaya verisi yoksa yalnızca krater duvarı oranı kullanılır. Bu puan yalnızca DÜŞÜRÜCÜ etki yapar.
    /// </summary>
    private void ComputeHazard()
    {
        var hz = new float[Width * Height];
        var rockMean = _rockMean;   // kaya verisi henüz yoksa NaN
        Parallel.For(0, Height, i =>
        {
            for (int j = 0; j < Width; j++)
            {
                int idx = i * Width + j;
                double wall = ScoreCurves.Piecewise(_wall10Pct[idx], (0, 100), (5, 80), (15, 50), (30, 20), (50, 0));
                float ra = rockMean[idx];
                hz[idx] = float.IsNaN(ra)
                    ? (float)wall
                    : (float)Math.Clamp(0.6 * ScoreCurves.Rock(Math.Max(ra, _rockMax[idx] * 0.5f)) + 0.4 * wall, 0, 100);
            }
        });
        HazardScore = hz;
    }

    private float _rockUnit = float.NaN;

    /// <summary>
    /// Diviner kaya haritasının bir satır bloğunu hücrelere aktarır (ortalama ve en yüksek değer).
    /// Bellek tasarrufu için harita parça parça okunur; her parça 8'in katı satır içermelidir.
    /// </summary>
    public void AccumulateRocks(RasterBlock ra)
    {
        var src = ra.Source;
        if (float.IsNaN(_rockUnit))
        {
            // Birim kontrolü: bazı ürünlerde yüzde olarak saklanır (oran en fazla 1 olabilir)
            float rawMax = 0;
            foreach (var v in ra.RawData) if (!float.IsNaN(v) && v > rawMax) rawMax = v;
            if (rawMax > 0) _rockUnit = rawMax > 1.5f ? 0.01f : 1f;
        }
        float unit = float.IsNaN(_rockUnit) ? 1f : _rockUnit;
        int perCell = Math.Max(1, (int)Math.Round(src.Resolution / Resolution));

        Parallel.For(0, Height, i =>
        {
            double latTop = 90 - i / Resolution, latBot = 90 - (i + 1) / Resolution;
            int ra0 = (int)Math.Ceiling(src.ToPixel(latTop, 0).Row);
            int ra1 = (int)Math.Floor(src.ToPixel(latBot, 0).Row);
            if (ra0 < ra.FirstRow || ra1 >= ra.FirstRow + ra.RowCount) return;   // bu parçada değil
            for (int j = 0; j < Width; j++)
            {
                int ca0 = (int)Math.Ceiling(src.ToPixel(0, j / Resolution).Col);
                double s = 0, m = 0; int n = 0;
                for (int r = ra0; r <= ra1; r++)
                for (int c = ca0; c < ca0 + perCell; c++)
                {
                    float v = ra.Get(r, c);
                    if (float.IsNaN(v) || v < 0) continue;
                    v *= unit;
                    s += v; m = Math.Max(m, v); n++;
                }
                if (n > 0) { _rockMean[i * Width + j] = (float)(s / n); _rockMax[i * Width + j] = (float)m; }
            }
        });
    }

    /// <summary>Kaya verisi tamamlandığında krater ve taş puanını yeniden hesaplar.</summary>
    public void FinishRocks()
    {
        HasRocks = true;
        ComputeHazard();
    }

    /// <summary>Görev profilinin ağırlıkları ve kritik sınırlarıyla genel puanı hesaplar (hızlı, ~1 sn).</summary>
    public void ApplyProfile(MissionProfile p)
    {
        int n = Width * Height;
        var overall = new float[n];
        var fail = new bool[n];
        var hazard = HazardScore;
        double[] rowSum = new double[Height], rowArea = new double[Height], rowOk = new double[Height];

        Parallel.For(0, Height, i =>
        {
            double area = Math.Cos((90 - (i + 0.5) / Resolution) * Math.PI / 180);
            for (int j = 0; j < Width; j++)
            {
                int idx = i * Width + j;
                double w = 0, s = 0;
                void Add(double score, double weight) { if (!double.IsNaN(score)) { s += score * weight; w += weight; } }
                Add(SlopeScore[idx], p.SlopeWeight);
                Add(hazard[idx], p.HazardWeight);
                Add(SunScore[idx], p.SunWeight);
                Add(ThermalScore[idx], p.ThermalWeight);
                Add(CommScore[idx], p.CommWeight);
                Add(TerrainScore[idx], p.TerrainWeight);
                double o = w > 0 ? s / w : double.NaN;
                overall[idx] = (float)o;

                bool f = _slopeMean[idx] > p.MaxSlopeDeg
                         || (!float.IsNaN(_rockMean[idx]) && _rockMean[idx] > p.MaxRockAbundance)
                         || _commMaxEl[idx] < 0;
                fail[idx] = f;

                if (!double.IsNaN(o)) { rowSum[i] += o * area; rowArea[i] += area; }
                if (!f && o >= 65) rowOk[i] += area;
            }
        });

        Overall = overall;
        HardFail = fail;
        ProfileName = p.Name;
        MeanOverall = rowSum.Sum() / rowArea.Sum();
        PercentSuitable = 100 * rowOk.Sum() / rowArea.Sum();
    }

    // =====================================================================
    public float[]? Layer(HeatLayerKind kind) => kind switch
    {
        HeatLayerKind.Overall => Overall,
        HeatLayerKind.Slope => SlopeScore,
        HeatLayerKind.Hazard => HazardScore,
        HeatLayerKind.Sun => SunScore,
        HeatLayerKind.Thermal => ThermalScore,
        HeatLayerKind.Comm => CommScore,
        HeatLayerKind.Terrain => TerrainScore,
        _ => null
    };

    /// <summary>Çift doğrusal örnekleme (boylamda sarmalı).</summary>
    public static float Sample(float[] grid, int w, int h, double lat, double lon)
    {
        double row = (90 - lat) * Resolution - 0.5;
        double col = (((lon % 360) + 360) % 360) * Resolution - 0.5;
        int r0 = (int)Math.Floor(row), c0 = (int)Math.Floor(col);
        double fr = row - r0, fc = col - c0;
        int ra = Math.Clamp(r0, 0, h - 1), rb = Math.Clamp(r0 + 1, 0, h - 1);
        int ca = ((c0 % w) + w) % w, cb = (ca + 1) % w;
        float v00 = grid[ra * w + ca], v01 = grid[ra * w + cb], v10 = grid[rb * w + ca], v11 = grid[rb * w + cb];
        if (float.IsNaN(v00 + v01 + v10 + v11))
            return grid[Math.Clamp((int)Math.Round(row), 0, h - 1) * w + ((int)Math.Round(col) % w + w) % w];
        return (float)((v00 * (1 - fc) + v01 * fc) * (1 - fr) + (v10 * (1 - fc) + v11 * fc) * fr);
    }

    public bool FailAt(double lat, double lon)
    {
        int r = Math.Clamp((int)((90 - lat) * Resolution), 0, Height - 1);
        int c = ((int)Math.Floor((((lon % 360) + 360) % 360) * Resolution) % Width + Width) % Width;
        return HardFail[r * Width + c];
    }

    /// <summary>Fare imlecinin altındaki hücrenin kısa açıklaması (durum çubuğu için).</summary>
    public string Describe(double lat, double lon)
    {
        int r = Math.Clamp((int)((90 - lat) * Resolution), 0, Height - 1);
        int c = ((int)Math.Floor((((lon % 360) + 360) % 360) * Resolution) % Width + Width) % Width;
        int i = r * Width + c;
        string rock = float.IsNaN(_rockMean[i]) ? L.T("veri yok", "no data") : L.T($"%{_rockMean[i] * 100:F2}", $"{_rockMean[i] * 100:F2}%");
        string crit = HardFail[i] ? L.T(" (KRİTİK)", " (CRITICAL)") : "";
        return L.T($"Küresel puan {Overall[i]:F0}{crit} · eğim ort. {_slopeMean[i]:F1}° · kaya {rock} · Dünya en düşük {_commMinEl[i]:F0}°",
                   $"Global score {Overall[i]:F0}{crit} · mean slope {_slopeMean[i]:F1}° · rocks {rock} · Earth lowest {_commMinEl[i]:F0}°");
    }
}

public enum HeatLayerKind { Off, Overall, Slope, Hazard, Sun, Thermal, Comm, Terrain }

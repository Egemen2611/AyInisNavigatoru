using AyInisNavigatoru.Data;

namespace AyInisNavigatoru.Analysis;

/// <summary>
/// LOLA sayısal yükseklik modelinden (DEM) eğim, pürüzlülük, rölyef,
/// krater tespiti ve ufuk profili hesaplar.
/// </summary>
public static class TerrainAnalyzer
{
    private const double R = Config.MoonRadiusM;

    /// <summary>LOLA değerleri yarıçap (1737400 + h) olarak gelir; yüksekliğe çevirir.</summary>
    private static double Height(RasterBlock b, int row, int col)
    {
        double v = b.Get(row, col);
        return v > 1.0e6 ? v - R : v;
    }

    private static double HeightBilinear(RasterBlock b, double row, double col)
    {
        double v = b.Bilinear(row, col);
        return v > 1.0e6 ? v - R : v;
    }

    /// <summary>Horn (1981) yöntemiyle eğim ve bakı (aspect) hesabı.</summary>
    public static (double SlopeDeg, double AspectDeg) SlopeAt(RasterBlock b, int row, int col)
    {
        double dy = b.Source.PixelSizeM;
        double lat = b.Source.RowToLatitude(row);
        double dx = dy * Math.Cos(lat * Math.PI / 180.0);
        if (dx < 1) dx = 1;

        double a = Height(b, row - 1, col - 1), bb = Height(b, row - 1, col), c = Height(b, row - 1, col + 1);
        double d = Height(b, row, col - 1), f = Height(b, row, col + 1);
        double g = Height(b, row + 1, col - 1), h = Height(b, row + 1, col), i = Height(b, row + 1, col + 1);
        if (double.IsNaN(a + bb + c + d + f + g + h + i)) return (double.NaN, double.NaN);

        double dzdx = ((c + 2 * f + i) - (a + 2 * d + g)) / (8 * dx);   // doğuya doğru
        double dzdy = ((a + 2 * bb + c) - (g + 2 * h + i)) / (8 * dy);  // kuzeye doğru

        double slope = Math.Atan(Math.Sqrt(dzdx * dzdx + dzdy * dzdy)) * 180.0 / Math.PI;
        double aspect = Math.Atan2(-dzdx, -dzdy) * 180.0 / Math.PI;   // yokuş aşağı yön, kuzeyden saat yönü
        aspect = (aspect + 360) % 360;
        return (slope, aspect);
    }

    /// <param name="knownCraters">Bölge taramasında önceden bulunmuş kraterler (her nokta için yeniden aranmaz).</param>
    public static TerrainResult Analyze(RasterBlock dem, LandingSite site, double ellipseRadiusM,
                                        IReadOnlyList<Crater>? knownCraters = null)
    {
        var src = dem.Source;
        var (rc, cc) = src.ToPixel(site.LatitudeDeg, site.Lon360);
        int ri = (int)Math.Round(rc), ci = (int)Math.Round(cc);

        double dy = src.PixelSizeM;
        double dx = dy * Math.Cos(site.LatitudeDeg * Math.PI / 180.0);
        if (dx < 1) dx = 1;

        var result = new TerrainResult
        {
            DemProduct = src.Name,
            PixelSizeM = dy,
            EllipseRadiusM = ellipseRadiusM,
            ElevationM = HeightBilinear(dem, rc, cc),
            CraterSearchRadiusM = Config.CraterSearchRadiusM
        };

        (result.SlopeAtSiteDeg, result.AspectAtSiteDeg) = SlopeAt(dem, ri, ci);

        // ---------------- İniş elipsi içinde eğim istatistikleri + düzlem uydurma ----------------
        int nRows = (int)Math.Ceiling(ellipseRadiusM / dy) + 1;
        int nCols = (int)Math.Ceiling(ellipseRadiusM / dx) + 1;
        var slopes = new List<double>();
        // Düzlem z = p0 + p1*x + p2*y için normal denklemler
        double sxx = 0, sxy = 0, syy = 0, sx = 0, sy = 0, n = 0, sz = 0, sxz = 0, syz = 0;
        var points = new List<(double x, double y, double z)>();

        for (int r = ri - nRows; r <= ri + nRows; r++)
        for (int c = ci - nCols; c <= ci + nCols; c++)
        {
            double north = (rc - r) * dy, east = (c - cc) * dx;
            if (north * north + east * east > ellipseRadiusM * ellipseRadiusM) continue;

            var (s, _) = SlopeAt(dem, r, c);
            if (!double.IsNaN(s)) slopes.Add(s);

            double z = Height(dem, r, c);
            if (double.IsNaN(z)) continue;
            points.Add((east, north, z));
            sx += east; sy += north; sz += z; n++;
            sxx += east * east; syy += north * north; sxy += east * north;
            sxz += east * z; syz += north * z;
        }

        if (slopes.Count > 0)
        {
            slopes.Sort();
            result.SlopeMeanDeg = slopes.Average();
            result.SlopeMaxDeg = slopes[^1];
            result.SlopeP90Deg = slopes[(int)Math.Min(slopes.Count - 1, Math.Floor(0.9 * slopes.Count))];
            result.PercentOver10Deg = 100.0 * slopes.Count(s => s > 10) / slopes.Count;
            result.PercentOver15Deg = 100.0 * slopes.Count(s => s > 15) / slopes.Count;
        }

        // Pürüzlülük: eğik düzlem çıkarıldıktan sonra kalan yüksekliklerin RMS'i
        var plane = SolvePlane(n, sx, sy, sxx, sxy, syy, sz, sxz, syz);
        if (plane.HasValue && points.Count > 3)
        {
            var (p0, p1, p2) = plane.Value;
            double ss = points.Sum(p => Math.Pow(p.z - (p0 + p1 * p.x + p2 * p.y), 2));
            result.RoughnessRmsM = Math.Sqrt(ss / points.Count);
        }

        // ---------------- Yerel rölyef (5 km yarıçap) ----------------
        double reliefR = 5000;
        int rr = (int)Math.Ceiling(reliefR / dy), rcl = (int)Math.Ceiling(reliefR / dx);
        double min = double.MaxValue, max = double.MinValue;
        for (int r = ri - rr; r <= ri + rr; r++)
        for (int c = ci - rcl; c <= ci + rcl; c++)
        {
            double north = (rc - r) * dy, east = (c - cc) * dx;
            if (north * north + east * east > reliefR * reliefR) continue;
            double z = Height(dem, r, c);
            if (double.IsNaN(z)) continue;
            min = Math.Min(min, z); max = Math.Max(max, z);
        }
        result.LocalReliefM = max > min ? max - min : 0;

        result.TerrainType = ClassifyTerrain(result.ElevationM, result.RoughnessRmsM, result.LocalReliefM);

        // ---------------- Krater tespiti ----------------
        var craters = knownCraters ?? DetectCraters(dem, site.LatitudeDeg, site.Lon360, Config.CraterSearchRadiusM);
        ApplyCraterMetrics(craters, site, result);
        return result;
    }

    private static (double, double, double)? SolvePlane(double n, double sx, double sy, double sxx, double sxy,
                                                        double syy, double sz, double sxz, double syz)
    {
        // | n   sx  sy  | |p0|   | sz  |
        // | sx  sxx sxy | |p1| = | sxz |
        // | sy  sxy syy | |p2|   | syz |
        double det = n * (sxx * syy - sxy * sxy) - sx * (sx * syy - sxy * sy) + sy * (sx * sxy - sxx * sy);
        if (Math.Abs(det) < 1e-9) return null;
        double d0 = sz * (sxx * syy - sxy * sxy) - sx * (sxz * syy - sxy * syz) + sy * (sxz * sxy - sxx * syz);
        double d1 = n * (sxz * syy - syz * sxy) - sz * (sx * syy - sxy * sy) + sy * (sx * syz - sxz * sy);
        double d2 = n * (sxx * syz - sxy * sxz) - sx * (sx * syz - sxz * sy) + sz * (sx * sxy - sxx * sy);
        return (d0 / det, d1 / det, d2 / det);
    }

    public static string ClassifyTerrain(double elevation, double roughness, double relief)
    {
        // Ay denizleri (maria) genelde referans yüzeyin 1-3 km altında ve düzdür;
        // yüksek araziler (highlands) daha yüksek, eski ve kraterlidir.
        if (elevation < -1200 && roughness < 15 && relief < 600)
            return L.T("Mare (bazaltik lav düzlüğü) - düz, genç yüzey", "Mare (basaltic lava plain) - flat, young surface");
        if (elevation > 500 || relief > 1500)
            return L.T("Yüksek arazi (highlands) - eski, kraterli anortozit kabuk", "Highlands - old, cratered anorthositic crust");
        if (relief > 800)
            return L.T("Engebeli arazi / krater çevresi", "Rugged terrain / crater surroundings");
        return L.T("Geçiş bölgesi (mare kenarı veya düzlük)", "Transition zone (mare edge or plain)");
    }

    /// <summary>
    /// Basit krater dedektörü: yerel minimum etrafında halka (ring) profilleri çıkarır,
    /// kenar (rim) yarıçapını bulur ve derinlik/çap oranını Pike (1977) aralığıyla
    /// (taze kraterler ~0.2, yaşlı kraterler ~0.05-0.1) karşılaştırır.
    /// Merkez (centerLat, centerLon) etrafında searchRadiusM içindeki kraterleri döndürür.
    /// </summary>
    public static List<Crater> DetectCraters(RasterBlock dem, double centerLat, double centerLon360, double searchRadiusM)
    {
        const int ringSamples = 24;
        const int minRadiusPx = 2, maxRadiusPx = 10;
        var src = dem.Source;
        var (rc, cc) = src.ToPixel(centerLat, centerLon360);
        double dy = src.PixelSizeM;
        double dx = Math.Max(1, dy * Math.Cos(centerLat * Math.PI / 180.0));
        double searchR = searchRadiusM;
        int sr = (int)Math.Ceiling(searchR / dy), sc = (int)Math.Ceiling(searchR / dx);
        int ri = (int)Math.Round(rc), ci = (int)Math.Round(cc);

        var candidates = new List<(int r, int c, double radiusM, double depth, double score)>();

        for (int r = ri - sr; r <= ri + sr; r++)
        for (int c = ci - sc; c <= ci + sc; c++)
        {
            double north = (rc - r) * dy, east = (c - cc) * dx;
            if (north * north + east * east > searchR * searchR) continue;

            double z0 = Height(dem, r, c);
            if (double.IsNaN(z0)) continue;

            // 5x5 yerel minimum mu?
            bool isMin = true;
            for (int i = -2; i <= 2 && isMin; i++)
            for (int j = -2; j <= 2; j++)
            {
                if (i == 0 && j == 0) continue;
                double z = Height(dem, r + i, c + j);
                if (double.IsNaN(z) || z < z0) { isMin = false; break; }
            }
            if (!isMin) continue;

            // Halka profilleri (fiziksel daire: satır/sütun ölçekleri farklı)
            var profile = new double[maxRadiusPx + 3];
            bool valid = true;
            for (int k = minRadiusPx; k <= maxRadiusPx + 2 && valid; k++)
            {
                double radiusM = k * dy, sum = 0; int cnt = 0;
                for (int t = 0; t < ringSamples; t++)
                {
                    double th = 2 * Math.PI * t / ringSamples;
                    double z = HeightBilinear(dem, r - radiusM * Math.Cos(th) / dy, c + radiusM * Math.Sin(th) / dx);
                    if (double.IsNaN(z)) continue;
                    sum += z; cnt++;
                }
                if (cnt < ringSamples * 0.8) { valid = false; break; }
                profile[k] = sum / cnt - z0;
            }
            if (!valid) continue;

            // Kenar = profilin en yüksek olduğu yarıçap (dış tarafta tekrar düşmeli)
            int rim = minRadiusPx;
            for (int k = minRadiusPx; k <= maxRadiusPx; k++)
                if (profile[k] > profile[rim]) rim = k;
            if (rim >= maxRadiusPx) continue;
            if (profile[rim + 2] >= profile[rim]) continue;

            double depth = profile[rim];
            double diameter = 2 * rim * dy;
            double dD = depth / diameter;
            if (depth < 15 || dD < 0.05) continue;

            // Dairesellik: kenar halkasının çoğu merkezden belirgin yüksek olmalı
            int above = 0;
            for (int t = 0; t < ringSamples; t++)
            {
                double th = 2 * Math.PI * t / ringSamples;
                double z = HeightBilinear(dem, r - rim * Math.Cos(th), c + rim * dy * Math.Sin(th) / dx);
                if (!double.IsNaN(z) && z - z0 > 0.3 * depth) above++;
            }
            if (above < ringSamples * 0.8) continue;

            candidates.Add((r, c, rim * dy, depth, dD));
        }

        // Aynı krateri birden çok kez saymamak için baskılama (non-maximum suppression)
        var accepted = new List<(int r, int c, double radiusM, double depth)>();
        foreach (var cand in candidates.OrderByDescending(x => x.radiusM).ThenByDescending(x => x.score))
        {
            bool overlaps = accepted.Any(a =>
            {
                double dN = (a.r - cand.r) * dy, dE = (a.c - cand.c) * dx;
                return Math.Sqrt(dN * dN + dE * dE) < Math.Max(a.radiusM, cand.radiusM);
            });
            if (!overlaps) accepted.Add((cand.r, cand.c, cand.radiusM, cand.depth));
        }

        var list = new List<Crater>();
        foreach (var a in accepted)
        {
            double dN = (rc - a.r) * dy, dE = (a.c - cc) * dx;
            double lat = src.RowToLatitude(a.r);
            double lon = SolarAnalyzer.Norm180(src.ColToLongitude(a.c));
            list.Add(new Crater(lat, lon, 2 * a.radiusM, a.depth, Math.Sqrt(dN * dN + dE * dE)));
        }
        return list;
    }

    /// <summary>Bilinen krater listesinden, verilen noktaya göre yoğunluk, en yakın kenar ve "krater içinde mi" bilgisini çıkarır.</summary>
    public static void ApplyCraterMetrics(IReadOnlyList<Crater> craters, LandingSite site, TerrainResult result)
    {
        double searchR = Config.CraterSearchRadiusM;
        double cosLat = Math.Max(0.01, Math.Cos(site.LatitudeDeg * Math.PI / 180));
        double nearestRim = double.MaxValue;
        var near = new List<Crater>();

        foreach (var cr in craters)
        {
            double dN = (cr.LatDeg - site.LatitudeDeg) * Math.PI / 180 * R;
            double dE = SolarAnalyzer.Norm180(cr.LonDeg - site.LongitudeEastDeg) * Math.PI / 180 * R * cosLat;
            double dist = Math.Sqrt(dN * dN + dE * dE);
            double radius = cr.DiameterM / 2;

            if (dist <= searchR) near.Add(cr with { DistanceFromSiteM = dist });
            if (dist < radius) result.SiteInsideCrater = true;
            if (dist <= searchR + radius) nearestRim = Math.Min(nearestRim, Math.Abs(dist - radius));
        }

        near.Sort((x, y) => x.DistanceFromSiteM.CompareTo(y.DistanceFromSiteM));
        result.Craters = near;
        result.CraterSearchRadiusM = searchR;
        result.NearestCraterRimDistanceM = nearestRim < double.MaxValue ? nearestRim : double.NaN;
        double areaKm2 = Math.PI * Math.Pow(searchR / 1000.0, 2);
        result.CraterDensityPer100Km2 = near.Count / areaKm2 * 100.0;
    }

    /// <summary>
    /// Ufuk profili: her 10°'lik azimutta, Ay eğriliği hesaba katılarak
    /// arazinin oluşturduğu en büyük yükselti açısı.
    /// </summary>
    public static HorizonProfile ComputeHorizon(RasterBlock dem, LandingSite site)
    {
        var profile = new HorizonProfile { DemProduct = dem.Source.Name };
        var (rc, cc) = dem.Source.ToPixel(site.LatitudeDeg, site.Lon360);
        double h0 = HeightBilinear(dem, rc, cc) + Config.LanderMastHeightM;
        double step = dem.Source.PixelSizeM;
        double cosLat = Math.Max(0.01, Math.Cos(site.LatitudeDeg * Math.PI / 180));

        for (int k = 0; k < 36; k++)
        {
            double az = k * 10 * Math.PI / 180;
            double best = -90;
            for (double d = step; d <= Config.HorizonMaxDistanceM; d += step)
            {
                double lat = site.LatitudeDeg + d * Math.Cos(az) / R * 180 / Math.PI;
                double lon = site.Lon360 + d * Math.Sin(az) / (R * cosLat) * 180 / Math.PI;
                var (r, c) = dem.Source.ToPixel(lat, lon);
                double h = HeightBilinear(dem, r, c);
                if (double.IsNaN(h)) continue;
                double drop = d * d / (2 * R);      // Ay yüzeyinin eğriliği
                double ang = Math.Atan2(h - h0 - drop, d) * 180 / Math.PI;
                if (ang > best) best = ang;
            }
            profile.ElevationDeg[k] = best < -89 ? 0 : best;
        }
        return profile;
    }
}

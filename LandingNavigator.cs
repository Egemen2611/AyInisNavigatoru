using AyInisNavigatoru.Analysis;
using AyInisNavigatoru.Data;

namespace AyInisNavigatoru;

/// <summary>
/// Tüm veri kaynaklarını yönetir ve bir iniş noktasının değerlendirmesini yapar.
/// </summary>
public sealed class LandingNavigator : IDisposable
{
    private readonly HttpClient _http;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PdsImageSource?> _sources = new();

    /// <summary>İlerleme mesajları (arayüzde günlük penceresine yazılır).</summary>
    public event Action<string>? LogMessage;

    public LandingNavigator()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AyInisNavigatoru/1.0 (NASA Space Apps Challenge)");
    }

    private void Log(string msg) => LogMessage?.Invoke(msg);

    /// <summary>Harita çizimi için düşük çözünürlüklü global LOLA modelini tamamen okur.</summary>
    public async Task<RasterBlock?> LoadGlobalDemAsync(string product)
    {
        var src = await GetSourceAsync(Config.LolaBaseUrl, product);
        if (src == null) return null;
        double mb = src.Lines * src.LineBytes / 1024.0 / 1024.0;
        Log($"Harita için {product} indiriliyor (~{mb:F0} MB, yalnızca ilk seferde)...");
        try { return await src.ReadRowsAsync(0, src.Lines - 1); }
        catch (Exception ex) { Log($"UYARI: {product} haritası okunamadı → {ex.Message}"); return null; }
    }

    private async Task<PdsImageSource?> GetSourceAsync(string baseUrl, string product)
    {
        if (_sources.TryGetValue(product, out var cached)) return cached;
        try
        {
            Log($"{product} etiketi okunuyor ({new Uri(baseUrl).Host})...");
            var src = await PdsImageSource.OpenAsync(_http, baseUrl, product, Config.CacheDirectory);
            Log($"{product}: {src.Samples}×{src.Lines} piksel, {src.Resolution} piksel/derece, {src.SampleType}");
            _sources[product] = src;
            return src;
        }
        catch (Exception ex)
        {
            Log($"UYARI: {product} açılamadı → {ex.Message}");
            _sources[product] = null;
            return null;
        }
    }

    private async Task<RasterBlock?> ReadAroundAsync(PdsImageSource src, LandingSite site, int halfRows)
    {
        var (row, _) = src.ToPixel(site.LatitudeDeg, site.Lon360);
        int r = (int)Math.Round(row);
        double mb = (2 * halfRows + 1) * src.LineBytes / 1024.0 / 1024.0;
        Log($"{src.Name}: {2 * halfRows + 1} satır indiriliyor (~{mb:F1} MB, önbellekte varsa indirilmez)...");
        try { return await src.ReadRowsAsync(r - halfRows, r + halfRows); }
        catch (Exception ex)
        {
            Log($"UYARI: {src.Name} verisi okunamadı → {ex.Message}");
            return null;
        }
    }

    public Task<SiteEvaluation> EvaluateAsync(LandingSite site, double ellipseRadiusM) =>
        EvaluateAsync(site, ellipseRadiusM, MissionProfile.Standard);

    public async Task<SiteEvaluation> EvaluateAsync(LandingSite site, double ellipseRadiusM, MissionProfile profile)
    {
        var ev = new SiteEvaluation { Site = site, EvaluatedAtUtc = DateTime.UtcNow };

        // ---------- 1) LOLA yüksek çözünürlüklü DEM: eğim, pürüzlülük, kraterler ----------
        Log("▶ LRO LOLA yükseklik verisi alınıyor...");
        RasterBlock? demHigh = null;
        var lolaHigh = await GetSourceAsync(Config.LolaBaseUrl, Config.LolaDemHighRes);
        if (lolaHigh != null) demHigh = await ReadAroundAsync(lolaHigh, site, Config.HighResBlockHalfRows);

        var lolaLow = await GetSourceAsync(Config.LolaBaseUrl, Config.LolaDemLowRes);
        RasterBlock? demLow = lolaLow != null ? await ReadAroundAsync(lolaLow, site, Config.LowResBlockHalfRows) : null;

        var demForTerrain = demHigh ?? demLow;
        if (demForTerrain != null)
        {
            ev.Terrain = TerrainAnalyzer.Analyze(demForTerrain, site, ellipseRadiusM);
            ev.DataSources.Add($"LRO LOLA {demForTerrain.Source.Name} (Smith vd. 2010) — {demForTerrain.Source.ImageUrl}");
        }

        // ---------- 2) Ufuk profili (daha geniş alan → düşük çözünürlüklü DEM) ----------
        var demForHorizon = demLow ?? demHigh;
        if (demForHorizon != null)
        {
            Log("Ufuk profili hesaplanıyor (36 yön, 30 km)...");
            ev.Horizon = TerrainAnalyzer.ComputeHorizon(demForHorizon, site);
            if (demForHorizon != demForTerrain)
                ev.DataSources.Add($"LRO LOLA {demForHorizon.Source.Name} — {demForHorizon.Source.ImageUrl}");
        }
        else
        {
            ev.Horizon = new HorizonProfile { IsFlatAssumption = true, DemProduct = "yok" };
            ev.Warnings.Add("Ufuk profili için DEM alınamadı; arazi düz kabul edildi.");
        }

        // ---------- 3) Diviner kaya yoğunluğu ----------
        Log("▶ LRO Diviner kaya yoğunluğu verisi alınıyor...");
        var raSrc = await GetSourceAsync(Config.DivinerBaseUrl, Config.DivinerRockAbundance);
        if (raSrc != null)
        {
            var block = await ReadAroundAsync(raSrc, site, Config.DivinerBlockHalfRows);
            if (block != null)
            {
                ev.Rocks = ComputeRockStats(block, site, ellipseRadiusM);
                if (ev.Rocks != null)
                    ev.DataSources.Add($"LRO Diviner kaya yoğunluğu (Bandfield vd. 2011) — {raSrc.ImageUrl}");
                else
                    ev.Warnings.Add("Diviner kaya yoğunluğu bu nokta için boş (harita ±70-80° enlem ile sınırlı olabilir).");
            }
        }

        // ---------- 4) Diviner gece sıcaklığı ----------
        Log("▶ LRO Diviner gece sıcaklığı verisi alınıyor...");
        double? nightK = null;
        var stSrc = await GetSourceAsync(Config.DivinerBaseUrl, Config.DivinerSoilTemperature);
        if (stSrc != null)
        {
            var block = await ReadAroundAsync(stSrc, site, 2);
            if (block != null)
            {
                double v = block.AtLatLon(site.LatitudeDeg, site.Lon360);
                if (!double.IsNaN(v) && v > 40 && v < 300)
                {
                    nightK = v;
                    ev.DataSources.Add($"LRO Diviner gece regolit sıcaklığı — {stSrc.ImageUrl}");
                }
            }
        }

        // ---------- 5) Hesaplamalar ----------
        Log("▶ Güneş, sıcaklık ve iletişim analizi yapılıyor...");
        ev.Sun = SolarAnalyzer.Analyze(site, ev.Horizon!, ev.EvaluatedAtUtc);
        ev.Thermal = ThermalAnalyzer.Analyze(site, ev.Terrain, nightK);
        ev.Communication = CommunicationAnalyzer.Analyze(site, ev.Horizon!);

        if (Math.Abs(site.LatitudeDeg) > 80)
            ev.Warnings.Add("Kutup bölgesi (>80°): silindirik projeksiyon bozulur, kutup stereografik veriler daha doğru sonuç verir.");
        if (Math.Abs(site.LongitudeEastDeg) > 90 && Math.Abs(site.LongitudeEastDeg) < 270)
            ev.Warnings.Add("Seçilen nokta Ay'ın uzak yüzünde (Dünya'dan görünmeyen yüz).");

        SuitabilityScorer.Score(ev, profile);
        return ev;
    }

    // =====================================================================
    //  KÜRESEL KAYA HARİTASI (küresel uygunluk haritası için)
    // =====================================================================

    /// <summary>
    /// Diviner küresel kaya yoğunluğu haritasını (32 piksel/derece) parça parça indirip
    /// küresel uygunluk haritasına aktarır. Parçalar diskte saklanır; ikinci açılışta indirme olmaz.
    /// </summary>
    public async Task<bool> LoadGlobalRocksAsync(GlobalSuitabilityMap map, IProgress<double>? progress = null)
    {
        var src = await GetSourceAsync(Config.DivinerBaseUrl, Config.DivinerRockAbundanceGlobal);
        if (src == null) return false;
        const int chunkRows = 480;   // 8'in katı: her harita hücresi tek parçaya düşer
        Log($"Küresel kaya haritası indiriliyor (~{src.Lines * src.LineBytes / 1048576.0:F0} MB, yalnızca ilk seferde)...");
        try
        {
            for (int r = 0; r < src.Lines; r += chunkRows)
            {
                var block = await src.ReadRowsAsync(r, Math.Min(src.Lines - 1, r + chunkRows - 1));
                map.AccumulateRocks(block);
                progress?.Report(Math.Min(1.0, (double)(r + chunkRows) / src.Lines));
            }
            map.FinishRocks();
            Log("Küresel kaya haritası eklendi.");
            return true;
        }
        catch (Exception ex)
        {
            Log($"UYARI: küresel kaya haritası okunamadı → {ex.Message}");
            return false;
        }
    }

    private static RockResult? ComputeRockStats(RasterBlock block, LandingSite site, double radiusM)
    {
        var src = block.Source;
        var (rc, cc) = src.ToPixel(site.LatitudeDeg, site.Lon360);
        double dy = src.PixelSizeM;
        double dx = Math.Max(1, dy * Math.Cos(site.LatitudeDeg * Math.PI / 180));
        int nr = (int)Math.Ceiling(radiusM / dy) + 1, nc = (int)Math.Ceiling(radiusM / dx) + 1;
        int ri = (int)Math.Round(rc), ci = (int)Math.Round(cc);

        var values = new List<double>();
        for (int r = ri - nr; r <= ri + nr; r++)
        for (int c = ci - nc; c <= ci + nc; c++)
        {
            double north = (rc - r) * dy, east = (c - cc) * dx;
            if (north * north + east * east > radiusM * radiusM) continue;
            double v = block.Get(r, c);
            if (!double.IsNaN(v) && v >= 0) values.Add(v);
        }
        if (values.Count == 0) return null;

        double site0 = block.AtLatLon(site.LatitudeDeg, site.Lon360);
        if (double.IsNaN(site0) || site0 < 0) site0 = values.Average();

        // Bazı ürünlerde değer yüzde olarak saklanır; oran (0-1) birimine çevir
        double unit = values.Max() > 1.0 ? 0.01 : 1.0;
        return new RockResult
        {
            RockAbundanceAtSite = site0 * unit,
            RockAbundanceMean = values.Average() * unit,
            RockAbundanceMax = values.Max() * unit
        };
    }

    public void Dispose() => _http.Dispose();
}

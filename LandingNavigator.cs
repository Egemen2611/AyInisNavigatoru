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
        Log(L.T($"Harita için {product} indiriliyor (~{mb:F0} MB, yalnızca ilk seferde)...", $"Downloading {product} for the map (~{mb:F0} MB, first time only)..."));
        try { return await src.ReadRowsAsync(0, src.Lines - 1); }
        catch (Exception ex) { Log(L.T($"UYARI: {product} haritası okunamadı → {ex.Message}", $"WARNING: could not read {product} map → {ex.Message}")); return null; }
    }

    private async Task<PdsImageSource?> GetSourceAsync(string baseUrl, string product)
    {
        if (_sources.TryGetValue(product, out var cached)) return cached;
        try
        {
            Log(L.T($"{product} etiketi okunuyor ({new Uri(baseUrl).Host})...", $"Reading {product} label ({new Uri(baseUrl).Host})..."));
            var src = await PdsImageSource.OpenAsync(_http, baseUrl, product, Config.CacheDirectory);
            Log(L.T($"{product}: {src.Samples}×{src.Lines} piksel, {src.Resolution} piksel/derece, {src.SampleType}", $"{product}: {src.Samples}×{src.Lines} pixels, {src.Resolution} pixels/degree, {src.SampleType}"));
            _sources[product] = src;
            return src;
        }
        catch (Exception ex)
        {
            Log(L.T($"UYARI: {product} açılamadı → {ex.Message}", $"WARNING: could not open {product} → {ex.Message}"));
            _sources[product] = null;
            return null;
        }
    }

    private async Task<RasterBlock?> ReadAroundAsync(PdsImageSource src, LandingSite site, int halfRows)
    {
        var (row, _) = src.ToPixel(site.LatitudeDeg, site.Lon360);
        int r = (int)Math.Round(row);
        double mb = (2 * halfRows + 1) * src.LineBytes / 1024.0 / 1024.0;
        Log(L.T($"{src.Name}: {2 * halfRows + 1} satır indiriliyor (~{mb:F1} MB, önbellekte varsa indirilmez)...", $"{src.Name}: downloading {2 * halfRows + 1} rows (~{mb:F1} MB, skipped if cached)..."));
        try { return await src.ReadRowsAsync(r - halfRows, r + halfRows); }
        catch (Exception ex)
        {
            Log(L.T($"UYARI: {src.Name} verisi okunamadı → {ex.Message}", $"WARNING: could not read {src.Name} data → {ex.Message}"));
            return null;
        }
    }

    public Task<SiteEvaluation> EvaluateAsync(LandingSite site, double ellipseRadiusM) =>
        EvaluateAsync(site, ellipseRadiusM, MissionProfile.Standard);

    public async Task<SiteEvaluation> EvaluateAsync(LandingSite site, double ellipseRadiusM, MissionProfile profile)
    {
        var ev = new SiteEvaluation { Site = site, EvaluatedAtUtc = DateTime.UtcNow };

        // ---------- 1) LOLA yüksek çözünürlüklü DEM: eğim, pürüzlülük, kraterler ----------
        Log(L.T("▶ LRO LOLA yükseklik verisi alınıyor...", "▶ Fetching LRO LOLA elevation data..."));
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
            Log(L.T("Ufuk profili hesaplanıyor (36 yön, 30 km)...", "Computing horizon profile (36 directions, 30 km)..."));
            ev.Horizon = TerrainAnalyzer.ComputeHorizon(demForHorizon, site);
            if (demForHorizon != demForTerrain)
                ev.DataSources.Add($"LRO LOLA {demForHorizon.Source.Name} — {demForHorizon.Source.ImageUrl}");
        }
        else
        {
            ev.Horizon = new HorizonProfile { IsFlatAssumption = true, DemProduct = "yok" };
            ev.WarningTexts.Add(new[] { "Ufuk profili için DEM alınamadı; arazi düz kabul edildi.", "No DEM available for the horizon profile; terrain assumed flat." });
        }

        // ---------- 3) Diviner kaya yoğunluğu ----------
        Log(L.T("▶ LRO Diviner kaya yoğunluğu verisi alınıyor...", "▶ Fetching LRO Diviner rock abundance data..."));
        var raSrc = await GetSourceAsync(Config.DivinerBaseUrl, Config.DivinerRockAbundance);
        if (raSrc != null)
        {
            var block = await ReadAroundAsync(raSrc, site, Config.DivinerBlockHalfRows);
            if (block != null)
            {
                ev.Rocks = ComputeRockStats(block, site, ellipseRadiusM);
                if (ev.Rocks != null)
                    ev.DataSources.Add($"LRO Diviner rock abundance (Bandfield et al. 2011) — {raSrc.ImageUrl}");
                else
                    ev.WarningTexts.Add(new[] { "Diviner kaya yoğunluğu bu nokta için boş (harita ±70-80° enlem ile sınırlı olabilir).", "Diviner rock abundance is empty for this site (the map may be limited to ±70-80° latitude)." });
            }
        }

        // ---------- 4) Diviner gece sıcaklığı ----------
        Log(L.T("▶ LRO Diviner gece sıcaklığı verisi alınıyor...", "▶ Fetching LRO Diviner nighttime temperature data..."));
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
                    ev.DataSources.Add($"LRO Diviner nighttime regolith temperature — {stSrc.ImageUrl}");
                }
            }
        }

        // ---------- 5) Hesaplamalar ----------
        Log(L.T("▶ Güneş, sıcaklık ve iletişim analizi yapılıyor...", "▶ Analyzing sunlight, temperature and communication..."));
        ev.Sun = SolarAnalyzer.Analyze(site, ev.Horizon!, ev.EvaluatedAtUtc);
        ev.Thermal = ThermalAnalyzer.Analyze(site, ev.Terrain, nightK);
        ev.Communication = CommunicationAnalyzer.Analyze(site, ev.Horizon!);

        if (Math.Abs(site.LatitudeDeg) > 80)
            ev.WarningTexts.Add(new[] { "Kutup bölgesi (>80°): silindirik projeksiyon bozulur, kutup stereografik veriler daha doğru sonuç verir.", "Polar region (>80°): the cylindrical projection is distorted; polar stereographic data would be more accurate." });
        if (Math.Abs(site.LongitudeEastDeg) > 90 && Math.Abs(site.LongitudeEastDeg) < 270)
            ev.WarningTexts.Add(new[] { "Seçilen nokta Ay'ın uzak yüzünde (Dünya'dan görünmeyen yüz).", "The selected site is on the far side of the Moon (never visible from Earth)." });

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
        Log(L.T($"Küresel kaya haritası indiriliyor (~{src.Lines * src.LineBytes / 1048576.0:F0} MB, yalnızca ilk seferde)...", $"Downloading global rock map (~{src.Lines * src.LineBytes / 1048576.0:F0} MB, first time only)..."));
        try
        {
            for (int r = 0; r < src.Lines; r += chunkRows)
            {
                var block = await src.ReadRowsAsync(r, Math.Min(src.Lines - 1, r + chunkRows - 1));
                map.AccumulateRocks(block);
                progress?.Report(Math.Min(1.0, (double)(r + chunkRows) / src.Lines));
            }
            map.FinishRocks();
            Log(L.T("Küresel kaya haritası eklendi.", "Global rock map added."));
            return true;
        }
        catch (Exception ex)
        {
            Log(L.T($"UYARI: küresel kaya haritası okunamadı → {ex.Message}", $"WARNING: could not read global rock map → {ex.Message}"));
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

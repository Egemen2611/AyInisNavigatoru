using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AyInisNavigatoru.Analysis;

namespace AyInisNavigatoru.UI;

/// <summary>
/// Döndürülebilir 3B Ay küresi. Yüzey, LRO LOLA'nın gerçek yükseklik verisinden çizilir.
///  • Sol tuşla sürükle: Ay'ı döndür (yakın ve uzak yüzün tamamı görülebilir)
///  • Fare tekerleği: yakınlaştır / uzaklaştır
///  • Sol tık: noktayı seç
/// Aydınlatma, Ay yüzeyi için kullanılan Lommel–Seeliger yansıma yasasıyla hesaplanır.
/// Isı haritası açıkken Ay'ın TAMAMI küresel uygunluk puanına göre renklenir.
/// </summary>
public sealed class MoonGlobeControl : Control
{
    private MoonMapData? _map;
    private Bitmap? _bitmap;
    private bool _bitmapIsLowRes;

    // Görünüm: ekranın ortasına bakan noktanın enlem/boylamı (derece)
    private double _lat0 = 0, _lon0 = 0, _zoom = 1.0;
    private Point _mouseDown, _lastMouse;
    private bool _dragging;

    private bool _realSun;
    private double _subsolarLon;

    private GlobalSuitabilityMap? _global;
    private HeatLayerKind _layer = HeatLayerKind.Off;
    private bool _showLandings = true;

    private (double Lat, double Lon)? _pending;
    private readonly List<(double Lat, double Lon, double Score)> _markers = new();

    public event Action<double, double>? SiteClicked;
    public event Action<string>? HoverTextChanged;

    public MoonGlobeControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Color.FromArgb(4, 5, 12);
        Cursor = Cursors.Hand;
    }

    // ===================== Genel API =====================
    public void SetMap(MoonMapData map) { _map = map; Redraw(); }

    public void SetRealSunLighting(bool enabled, double subsolarLonDeg)
    {
        _realSun = enabled;
        _subsolarLon = subsolarLonDeg;
        Redraw();
    }

    /// <summary>Küresel uygunluk haritasını ve gösterilecek katmanı ayarlar (Off = gizli).</summary>
    public void SetGlobalHeatmap(GlobalSuitabilityMap? map, HeatLayerKind layer)
    {
        _global = map;
        _layer = layer;
        Redraw();
    }

    public HeatLayerKind Layer => _layer;

    public void SetShowLandings(bool show) { _showLandings = show; Invalidate(); }

    public void FlyTo(double lat, double lon, double? zoom = null)
    {
        _lat0 = Math.Clamp(lat, -89.5, 89.5);
        _lon0 = SolarAnalyzer.Norm180(lon);
        if (zoom.HasValue) _zoom = Math.Clamp(zoom.Value, 1, 80);
        Redraw();
    }

    public (double Lat, double Lon) ViewCenter => (_lat0, _lon0);

    public void ResetView() { _lat0 = 0; _lon0 = 0; _zoom = 1; Redraw(); }

    public void SetPending(double lat, double lon) { _pending = (lat, lon); Invalidate(); }
    public void ClearPending() { _pending = null; Invalidate(); }

    public void AddMarker(double lat, double lon, double score)
    {
        _markers.Add((lat, lon, score));
        Invalidate();
    }

    public void ClearMarkers() { _markers.Clear(); Invalidate(); }

    public void Redraw()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        Invalidate();
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Redraw(); }

    // ===================== Geometri =====================
    private double DiskRadiusPx => Math.Max(10, Math.Min(ClientSize.Width, ClientSize.Height) / 2.0 - 20);
    private double Scale => DiskRadiusPx * _zoom;

    private static double D2R(double d) => d * Math.PI / 180;

    /// <summary>Görünüm eksenleri (gövde koordinatlarında): C = bakış merkezi, E = ekranda sağ, N = ekranda yukarı.</summary>
    private (double[] C, double[] E, double[] N) Axes()
    {
        double p = D2R(_lat0), l = D2R(_lon0);
        var c = new[] { Math.Cos(p) * Math.Cos(l), Math.Cos(p) * Math.Sin(l), Math.Sin(p) };
        var e = new[] { -Math.Sin(l), Math.Cos(l), 0.0 };
        var n = new[] { -Math.Sin(p) * Math.Cos(l), -Math.Sin(p) * Math.Sin(l), Math.Cos(p) };
        return (c, e, n);
    }

    public (double Lat, double Lon)? ScreenToLatLon(double px, double py)
    {
        double x = (px - ClientSize.Width / 2.0) / Scale, y = -(py - ClientSize.Height / 2.0) / Scale;
        double rho2 = x * x + y * y;
        if (rho2 > 1) return null;
        double z = Math.Sqrt(1 - rho2);
        var (c, e, n) = Axes();
        double bx = z * c[0] + x * e[0] + y * n[0];
        double by = z * c[1] + x * e[1] + y * n[1];
        double bz = z * c[2] + x * e[2] + y * n[2];
        return (Math.Asin(Math.Clamp(bz, -1, 1)) * 180 / Math.PI, Math.Atan2(by, bx) * 180 / Math.PI);
    }

    private PointF? LatLonToScreen(double lat, double lon)
    {
        double la = D2R(lat), lo = D2R(lon);
        double bx = Math.Cos(la) * Math.Cos(lo), by = Math.Cos(la) * Math.Sin(lo), bz = Math.Sin(la);
        var (c, e, n) = Axes();
        double depth = bx * c[0] + by * c[1] + bz * c[2];
        if (depth < 0) return null;   // kürenin arka yüzü
        double x = bx * e[0] + by * e[1] + bz * e[2];
        double y = bx * n[0] + by * n[1] + bz * n[2];
        return new PointF((float)(ClientSize.Width / 2.0 + x * Scale), (float)(ClientSize.Height / 2.0 - y * Scale));
    }

    // ===================== Isı haritası renkleri =====================
    public static (int R, int G, int B) Ramp(double score)
    {
        // 0 = kırmızı, 50 = sarı, 100 = yeşil (RdYlGn paleti)
        score = Math.Clamp(score, 0, 100);
        (int, int, int) a, b; double t;
        if (score < 50) { a = (215, 48, 39); b = (254, 224, 139); t = score / 50; }
        else { a = (254, 224, 139); b = (26, 152, 80); t = (score - 50) / 50; }
        return ((int)(a.Item1 + (b.Item1 - a.Item1) * t), (int)(a.Item2 + (b.Item2 - a.Item2) * t), (int)(a.Item3 + (b.Item3 - a.Item3) * t));
    }

    public static string LayerName(HeatLayerKind l) => l switch
    {
        HeatLayerKind.Overall => "Genel uygunluk",
        HeatLayerKind.Slope => "Yüzey eğimi",
        HeatLayerKind.Hazard => "Krater ve taş",
        HeatLayerKind.Sun => "Güneş ışığı",
        HeatLayerKind.Thermal => "Sıcaklık",
        HeatLayerKind.Comm => "Dünya ile iletişim",
        HeatLayerKind.Terrain => "Arazi yapısı",
        _ => "Kapalı"
    };

    // ===================== Çizim =====================
    private void RenderBitmap(int step)
    {
        int w = Math.Max(1, ClientSize.Width), h = Math.Max(1, ClientSize.Height);
        _bitmap?.Dispose();
        _bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        _bitmapIsLowRes = step > 1;
        var pixels = new int[w * h];
        var map = _map;
        var (c, e, n) = Axes();
        double scale = Scale, cx = w / 2.0, cy = h / 2.0;

        bool realSun = _realSun;
        double sl = D2R(_subsolarLon);
        double[] sunBody = { Math.Cos(sl), Math.Sin(sl), 0 };
        double[] lightView = Normalize(-0.45, 0.50, 0.74);

        var global = _global;
        var layer = _layer;
        float[]? grid = global != null && layer != HeatLayerKind.Off ? global.Layer(layer) : null;
        bool showFail = layer == HeatLayerKind.Overall;
        int bg = BackColor.ToArgb();
        int star = Color.FromArgb(200, 200, 210).ToArgb();

        Parallel.For(0, (h + step - 1) / step, rowBlock =>
        {
            int py = rowBlock * step;
            for (int px = 0; px < w; px += step)
            {
                double x = (px + step / 2.0 - cx) / scale, y = -(py + step / 2.0 - cy) / scale;
                double rho2 = x * x + y * y;
                int color;
                if (rho2 > 1)
                {
                    unchecked
                    {
                        int hsh = (px * 73856093) ^ (py * 19349663);
                        color = (hsh & 0x7FF) == 7 ? star : bg;   // sabit yıldız alanı
                    }
                }
                else
                {
                    double z = Math.Sqrt(1 - rho2);
                    double bx = z * c[0] + x * e[0] + y * n[0];
                    double by = z * c[1] + x * e[1] + y * n[1];
                    double bz = z * c[2] + x * e[2] + y * n[2];
                    double lat = Math.Asin(Math.Clamp(bz, -1, 1)) * 180 / Math.PI;
                    double lon = Math.Atan2(by, bx) * 180 / Math.PI;

                    // Lommel–Seeliger: I ∝ μ0 / (μ0 + μ)   (μ0 = cos geliş, μ = cos yansıma = z)
                    double mu0 = realSun ? bx * sunBody[0] + by * sunBody[1] + bz * sunBody[2]
                                         : x * lightView[0] + y * lightView[1] + z * lightView[2];
                    double ls = mu0 > 0 ? 2 * mu0 / (mu0 + z) : 0;
                    double ambient = realSun ? 0.10 : 0.06;

                    double gray = 120, shade = 0.5;
                    if (map != null)
                    {
                        float elev = map.ElevationAt(lat, lon);
                        shade = map.ShadeAt(lat, lon);
                        gray = 70 + 135 * Math.Clamp((elev + 3500) / 8000.0, 0, 1);   // alçak mare koyu, yayla açık
                    }
                    double light = (ambient + (1 - ambient) * Math.Min(1.25, ls)) * (0.55 + 0.6 * shade);
                    double r = gray * light * 1.02, g = gray * light, b = gray * light * 0.95;

                    // Küresel ısı haritası: Ay'ın her yeri uygunluğuna göre renklenir
                    if (grid != null && global != null)
                    {
                        float v = GlobalSuitabilityMap.Sample(grid, global.Width, global.Height, lat, lon);
                        if (!float.IsNaN(v))
                        {
                            bool fail = showFail && global.FailAt(lat, lon);
                            var (hr, hg, hb) = fail ? (110, 0, 25) : Ramp(v);
                            const double alpha = 0.62;
                            double lum = 0.55 + 0.55 * Math.Clamp(light, 0, 1.2);   // rölyef görünür kalsın
                            r = r * (1 - alpha) + hr * alpha * lum;
                            g = g * (1 - alpha) + hg * alpha * lum;
                            b = b * (1 - alpha) + hb * alpha * lum;
                        }
                    }

                    color = unchecked((int)0xFF000000) | ((int)Math.Clamp(r, 0, 255) << 16) |
                            ((int)Math.Clamp(g, 0, 255) << 8) | (int)Math.Clamp(b, 0, 255);
                }

                for (int yy = py; yy < Math.Min(h, py + step); yy++)
                for (int xx = px; xx < Math.Min(w, px + step); xx++)
                    pixels[yy * w + xx] = color;
            }
        });

        var data = _bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (int row = 0; row < h; row++)
            Marshal.Copy(pixels, row * w, data.Scan0 + row * data.Stride, w);
        _bitmap.UnlockBits(data);
    }

    private static double[] Normalize(double x, double y, double z)
    {
        double l = Math.Sqrt(x * x + y * y + z * z);
        return new[] { x / l, y / l, z / l };
    }

    protected override void OnPaint(PaintEventArgs pe)
    {
        if (_bitmap == null || (_bitmapIsLowRes && !_dragging)) RenderBitmap(_dragging ? 3 : 1);
        var g = pe.Graphics;
        g.DrawImageUnscaled(_bitmap!, 0, 0);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        DrawGraticule(g);
        if (_showLandings) DrawLandings(g);
        DrawMarkers(g);
        DrawLegendAndInfo(g);
    }

    private void DrawGraticule(Graphics g)
    {
        using var pen = new Pen(Color.FromArgb(45, 120, 180, 255), 1);
        using var eq = new Pen(Color.FromArgb(90, 120, 200, 255), 1.3f);
        for (int lat = -60; lat <= 60; lat += 30)
            DrawPolyline(g, lat == 0 ? eq : pen, Enumerable.Range(0, 121).Select(k => (lat * 1.0, -180.0 + 3 * k)));
        for (int lon = -180; lon < 180; lon += 30)
            DrawPolyline(g, lon == 0 ? eq : pen, Enumerable.Range(0, 61).Select(k => (-90.0 + 3 * k, lon * 1.0)));
    }

    private void DrawPolyline(Graphics g, Pen pen, IEnumerable<(double Lat, double Lon)> pts)
    {
        var seg = new List<PointF>();
        foreach (var (la, lo) in pts)
        {
            var p = LatLonToScreen(la, lo);
            if (p == null) { if (seg.Count > 1) g.DrawLines(pen, seg.ToArray()); seg.Clear(); }
            else seg.Add(p.Value);
        }
        if (seg.Count > 1) g.DrawLines(pen, seg.ToArray());
    }

    /// <summary>Tarihî iniş noktaları: insanlı (Apollo) altın, robotik açık mavi üçgen.</summary>
    private void DrawLandings(Graphics g)
    {
        using var font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        using var crewed = new SolidBrush(Color.Gold);
        using var robotic = new SolidBrush(Color.FromArgb(120, 200, 255));
        bool labels = _zoom >= 1.6;
        foreach (var l in LandingCatalog.All)
        {
            var p0 = LatLonToScreen(l.LatDeg, l.LonDeg);
            if (p0 == null) continue;
            var p = p0.Value;
            var tri = new[] { new PointF(p.X, p.Y - 6), new PointF(p.X - 5, p.Y + 4), new PointF(p.X + 5, p.Y + 4) };
            g.FillPolygon(l.Crewed ? crewed : robotic, tri);
            g.DrawPolygon(Pens.Black, tri);
            if (labels)
            {
                g.DrawString(l.Name, font, Brushes.Black, p.X + 7, p.Y - 5);
                g.DrawString(l.Name, font, l.Crewed ? crewed : robotic, p.X + 6, p.Y - 6);
            }
        }
    }

    private void DrawMarkers(Graphics g)
    {
        using var font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        foreach (var m in _markers)
        {
            var p0 = LatLonToScreen(m.Lat, m.Lon);
            if (p0 == null) continue;
            var p = p0.Value;
            Color col = m.Score >= 80 ? Color.LimeGreen : m.Score >= 65 ? Color.YellowGreen
                      : m.Score >= 50 ? Color.Gold : Color.OrangeRed;
            using var brush = new SolidBrush(col);
            g.FillEllipse(brush, p.X - 6, p.Y - 6, 12, 12);
            g.DrawEllipse(Pens.Black, p.X - 6, p.Y - 6, 12, 12);
            string text = $"{m.Score:F0}";
            g.DrawString(text, font, Brushes.Black, p.X + 9, p.Y - 7);
            g.DrawString(text, font, brush, p.X + 8, p.Y - 8);
        }

        if (_pending != null)
        {
            var p0 = LatLonToScreen(_pending.Value.Lat, _pending.Value.Lon);
            if (p0 != null)
            {
                var p = p0.Value;
                using var pen = new Pen(Color.Cyan, 2);
                g.DrawEllipse(pen, p.X - 10, p.Y - 10, 20, 20);
                g.DrawLine(pen, p.X - 16, p.Y, p.X - 6, p.Y);
                g.DrawLine(pen, p.X + 6, p.Y, p.X + 16, p.Y);
                g.DrawLine(pen, p.X, p.Y - 16, p.X, p.Y - 6);
                g.DrawLine(pen, p.X, p.Y + 6, p.X, p.Y + 16);
            }
        }
    }

    private void DrawLegendAndInfo(Graphics g)
    {
        using var small = new Font("Segoe UI", 8.5f);
        using var bold = new Font("Segoe UI", 9f, FontStyle.Bold);

        string side = Math.Abs(SolarAnalyzer.Norm180(_lon0)) <= 90 ? "Yakın yüz (Dünya'ya bakan)" : "Uzak yüz (Dünya'dan görünmez)";
        g.DrawString($"Bakış merkezi: {_lat0:F1}°, {SolarAnalyzer.Norm180(_lon0):F1}°  ·  {side}  ·  Yakınlaştırma ×{_zoom:F1}",
                     bold, Brushes.White, 10, 8);
        g.DrawString(_realSun ? "Aydınlatma: gerçek güneş konumu (gece-gündüz çizgisi şu anki hâliyle)"
                              : "Aydınlatma: sabit ışık (yüzey şekillerini görmek için)",
                     small, Brushes.Silver, 10, 28);

        string info = _map == null
            ? "LOLA yükseklik haritası yükleniyor..."
            : $"Sürükle: döndür   ·   Tekerlek: yakınlaştır   ·   Tık: nokta seç   ·   Yüzey verisi: LRO LOLA {_map.Product}";
        g.DrawString(info, small, Brushes.LightGray, 10, ClientSize.Height - 22);

        int legendTop = ClientSize.Height - 30;
        if (_showLandings)
        {
            using var crewed = new SolidBrush(Color.Gold);
            using var robotic = new SolidBrush(Color.FromArgb(120, 200, 255));
            int x0 = ClientSize.Width - 250, y0 = 56;
            using var bgBrush = new SolidBrush(Color.FromArgb(150, 10, 12, 20));
            g.FillRectangle(bgBrush, x0 - 8, y0 - 6, 240, 40);
            g.FillPolygon(crewed, new[] { new PointF(x0 + 5, y0), new PointF(x0, y0 + 10), new PointF(x0 + 10, y0 + 10) });
            g.DrawString("İnsanlı iniş (Apollo)", small, Brushes.White, x0 + 16, y0 - 2);
            g.FillPolygon(robotic, new[] { new PointF(x0 + 5, y0 + 16), new PointF(x0, y0 + 26), new PointF(x0 + 10, y0 + 26) });
            g.DrawString("Robotik iniş (NASA NSSDC konumları)", small, Brushes.White, x0 + 16, y0 + 14);
        }

        if (_global != null && _layer != HeatLayerKind.Off)
        {
            int x0 = ClientSize.Width - 270, y0 = legendTop - 70, bw = 240, bh = 12;
            using var bgBrush = new SolidBrush(Color.FromArgb(180, 10, 12, 20));
            g.FillRectangle(bgBrush, x0 - 10, y0 - 26, bw + 20, 88);
            g.DrawString($"Isı haritası: {LayerName(_layer)}", bold, Brushes.White, x0, y0 - 22);
            for (int i = 0; i < bw; i++)
            {
                var (r, gg, b) = Ramp(100.0 * i / (bw - 1));
                using var pen = new Pen(Color.FromArgb(r, gg, b));
                g.DrawLine(pen, x0 + i, y0, x0 + i, y0 + bh);
            }
            g.DrawString("0 uygun değil", small, Brushes.White, x0, y0 + bh + 1);
            g.DrawString("100 çok uygun", small, Brushes.White, x0 + bw - 78, y0 + bh + 1);
            if (_layer == HeatLayerKind.Overall)
            {
                using var failBrush = new SolidBrush(Color.FromArgb(110, 0, 25));
                g.FillRectangle(failBrush, x0, y0 + bh + 20, 12, 10);
                g.DrawString("kritik sınır aşıldı (iniş yasak)", small, Brushes.White, x0 + 16, y0 + bh + 17);
            }
            g.DrawString($"Profil: {_global.ProfileName}{(_global.HasRocks ? "" : "  ·  kaya verisi yükleniyor")}",
                         small, Brushes.Silver, x0, y0 + bh + 34);
        }
    }

    // ===================== Fare =====================
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Focus(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _mouseDown = _lastMouse = e.Location;
        _dragging = false;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.Button == MouseButtons.Left)
        {
            if (!_dragging && Math.Abs(e.X - _mouseDown.X) + Math.Abs(e.Y - _mouseDown.Y) > 4) _dragging = true;
            if (_dragging)
            {
                double dx = e.X - _lastMouse.X, dy = e.Y - _lastMouse.Y;
                _lon0 = SolarAnalyzer.Norm180(_lon0 - dx / Scale * 180 / Math.PI);
                _lat0 = Math.Clamp(_lat0 + dy / Scale * 180 / Math.PI, -89.5, 89.5);
                _lastMouse = e.Location;
                _bitmap?.Dispose();
                _bitmap = null;
                Invalidate();
            }
        }

        var ll = ScreenToLatLon(e.X, e.Y);
        if (ll == null) { HoverTextChanged?.Invoke("Ay'ın dışında"); return; }
        double lat = ll.Value.Lat, lon = ll.Value.Lon;
        string elev = _map != null ? $"  |  Yükseklik ≈ {_map.ElevationAt(lat, lon):F0} m" : "";
        string face = Math.Abs(lon) <= 90 ? "yakın yüz" : "uzak yüz";
        string heat = _global != null && _layer != HeatLayerKind.Off ? "  |  " + _global.Describe(lat, lon) : "";
        HoverTextChanged?.Invoke($"Enlem {Math.Abs(lat):F3}° {(lat >= 0 ? "K" : "G")}   Boylam {Math.Abs(lon):F3}° {(lon >= 0 ? "D" : "B")}" +
                                 $"   ({face}){elev}{heat}");
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left)
        {
            if (!_dragging)
            {
                var ll = ScreenToLatLon(e.X, e.Y);
                if (ll != null) SiteClicked?.Invoke(ll.Value.Lat, ll.Value.Lon);
            }
            else
            {
                _dragging = false;
                Redraw();   // tam çözünürlükte yeniden çiz
            }
        }
        _dragging = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.35 : 1 / 1.35), 1.0, 80.0);
        Redraw();
    }
}

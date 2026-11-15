using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AyInisNavigatoru.UI;

/// <summary>Harita çizimi için hazırlanmış LOLA yükseklik ızgarası + gölgelendirme.</summary>
public sealed class MoonMapData
{
    public int Width { get; init; }
    public int Height { get; init; }
    public double Resolution { get; init; }   // piksel/derece
    public float[] Elevation { get; init; } = Array.Empty<float>();
    public float[] Shade { get; init; } = Array.Empty<float>();
    public string Product { get; init; } = "";

    /// <summary>LOLA global satır bloğundan (kuzeyden güneye, 0°→360° doğu) harita verisi üretir.</summary>
    public static MoonMapData FromLola(float[] radiusOrHeight, int width, int height, double resolution, string product)
    {
        var elev = new float[radiusOrHeight.Length];
        for (int i = 0; i < elev.Length; i++)
        {
            float v = radiusOrHeight[i];
            elev[i] = float.IsNaN(v) ? 0 : (v > 1.0e6f ? (float)(v - Config.MoonRadiusM) : v);
        }

        // Tepe gölgelendirmesi (hillshade): ışık kuzeybatıdan, 35° yükseklikten
        var shade = new float[elev.Length];
        double dy = Math.PI * Config.MoonRadiusM / (180.0 * resolution);
        const double exaggeration = 4.0;
        double zen = (90 - 35) * Math.PI / 180, lightAz = 315 * Math.PI / 180;
        for (int r = 0; r < height; r++)
        {
            double lat = 90 - (r + 0.5) / resolution;
            double dx = Math.Max(50, dy * Math.Cos(lat * Math.PI / 180));
            int rn = Math.Max(0, r - 1), rs = Math.Min(height - 1, r + 1);
            for (int c = 0; c < width; c++)
            {
                int cw = (c - 1 + width) % width, ce = (c + 1) % width;
                double dzdx = (elev[r * width + ce] - elev[r * width + cw]) / (2 * dx) * exaggeration;
                double dzdy = (elev[rn * width + c] - elev[rs * width + c]) / ((rs - rn) * dy) * exaggeration;
                double slope = Math.Atan(Math.Sqrt(dzdx * dzdx + dzdy * dzdy));
                double aspect = Math.Atan2(-dzdx, -dzdy);
                double hs = Math.Cos(zen) * Math.Cos(slope) + Math.Sin(zen) * Math.Sin(slope) * Math.Cos(lightAz - aspect);
                shade[r * width + c] = (float)Math.Clamp(hs, 0, 1);
            }
        }
        return new MoonMapData { Width = width, Height = height, Resolution = resolution, Elevation = elev, Shade = shade, Product = product };
    }

    private float Sample(float[] grid, double lat, double lon)
    {
        double row = (90 - lat) * Resolution - 0.5;
        double col = (((lon % 360) + 360) % 360) * Resolution - 0.5;
        int r0 = (int)Math.Floor(row), c0 = (int)Math.Floor(col);
        double fr = row - r0, fc = col - c0;
        r0 = Math.Clamp(r0, 0, Height - 1);
        int r1 = Math.Min(Height - 1, r0 + 1);
        int ca = ((c0 % Width) + Width) % Width, cb = (ca + 1) % Width;
        return (float)((grid[r0 * Width + ca] * (1 - fc) + grid[r0 * Width + cb] * fc) * (1 - fr) +
                       (grid[r1 * Width + ca] * (1 - fc) + grid[r1 * Width + cb] * fc) * fr);
    }

    public float ElevationAt(double lat, double lon) => Sample(Elevation, lat, lon);
    public float ShadeAt(double lat, double lon) => Sample(Shade, lat, lon);
}

/// <summary>
/// Ay'ın yakın yüzünü (Dünya'dan görüldüğü gibi, ortografik projeksiyon) çizen,
/// fareyle tıklanan noktanın enlem/boylamını veren harita kontrolü.
/// Fare tekerleği: yakınlaştır/uzaklaştır · Sol tuşla sürükle: kaydır · Sol tık: nokta seç
/// </summary>
public sealed class MoonMapControl : Control
{
    private MoonMapData? _map;
    private Bitmap? _bitmap;
    private double _zoom = 1.0;
    private double _viewX, _viewY;          // görünüm merkezi (birim disk koordinatı)
    private Point _mouseDown;
    private bool _dragging;
    private double _dragStartX, _dragStartY;
    private (double Lat, double Lon)? _pending;
    private readonly List<(double Lat, double Lon, double Score, string Name)> _markers = new();

    public event Action<double, double>? SiteClicked;
    public event Action<string>? HoverTextChanged;

    public MoonMapControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Color.FromArgb(8, 10, 20);
        Cursor = Cursors.Cross;
    }

    public void SetMap(MoonMapData map)
    {
        _map = map;
        InvalidateBitmap();
    }

    public void SetPending(double lat, double lon) { _pending = (lat, lon); Invalidate(); }
    public void ClearPending() { _pending = null; Invalidate(); }

    public void AddMarker(double lat, double lon, double score, string name)
    {
        _markers.Add((lat, lon, score, name));
        Invalidate();
    }

    public void ClearMarkers() { _markers.Clear(); Invalidate(); }

    public void ResetView() { _zoom = 1; _viewX = _viewY = 0; InvalidateBitmap(); }

    private void InvalidateBitmap()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        InvalidateBitmap();
    }

    // ---------------- Koordinat dönüşümleri ----------------
    private double DiskRadiusPx => Math.Max(10, Math.Min(ClientSize.Width, ClientSize.Height) / 2.0 - 15);

    private (double X, double Y) ScreenToDisk(double px, double py)
    {
        double s = DiskRadiusPx * _zoom;
        return (_viewX + (px - ClientSize.Width / 2.0) / s, _viewY - (py - ClientSize.Height / 2.0) / s);
    }

    private PointF DiskToScreen(double x, double y)
    {
        double s = DiskRadiusPx * _zoom;
        return new PointF((float)(ClientSize.Width / 2.0 + (x - _viewX) * s),
                          (float)(ClientSize.Height / 2.0 - (y - _viewY) * s));
    }

    /// <summary>Ters ortografik projeksiyon (merkez 0°K, 0°D). Disk dışında null döner.</summary>
    private static (double Lat, double Lon)? DiskToLatLon(double x, double y)
    {
        double rho2 = x * x + y * y;
        if (rho2 > 1) return null;
        double lat = Math.Asin(y) * 180 / Math.PI;
        double lon = Math.Atan2(x, Math.Sqrt(1 - rho2)) * 180 / Math.PI;
        return (lat, lon);
    }

    private static (double X, double Y)? LatLonToDisk(double lat, double lon)
    {
        double lonN = ((lon + 180) % 360 + 360) % 360 - 180;
        if (Math.Abs(lonN) > 90) return null;   // uzak yüz
        double la = lat * Math.PI / 180, lo = lonN * Math.PI / 180;
        return (Math.Cos(la) * Math.Sin(lo), Math.Sin(la));
    }

    public (double Lat, double Lon)? ScreenToLatLon(Point p)
    {
        var (x, y) = ScreenToDisk(p.X, p.Y);
        return DiskToLatLon(x, y);
    }

    // ---------------- Çizim ----------------
    private void RenderBitmap()
    {
        int w = Math.Max(1, ClientSize.Width), h = Math.Max(1, ClientSize.Height);
        _bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var pixels = new int[w * h];
        int bg = BackColor.ToArgb();

        for (int py = 0; py < h; py++)
        for (int px = 0; px < w; px++)
        {
            var (x, y) = ScreenToDisk(px, py);
            var ll = DiskToLatLon(x, y);
            if (ll == null || _map == null)
            {
                pixels[py * w + px] = ll == null ? bg : Color.FromArgb(60, 60, 64).ToArgb();
                continue;
            }
            float elev = _map.ElevationAt(ll.Value.Lat, ll.Value.Lon);
            float shade = _map.ShadeAt(ll.Value.Lat, ll.Value.Lon);

            // Alçak bölgeler (mare) koyu, yüksek araziler açık gri: gerçek Ay görünümüne benzer
            double t = Math.Clamp((elev + 3500) / 8000.0, 0, 1);
            double gray = 70 + 135 * t;
            double light = 0.45 + 0.75 * shade;
            // Kenara doğru hafif kararma (limb darkening)
            double mu = Math.Sqrt(Math.Max(0, 1 - x * x - y * y));
            light *= 0.75 + 0.25 * mu;
            int r = (int)Math.Clamp(gray * light * 1.02, 0, 255);
            int g = (int)Math.Clamp(gray * light * 1.00, 0, 255);
            int b = (int)Math.Clamp(gray * light * 0.95, 0, 255);
            pixels[py * w + px] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
        }

        var data = _bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (int row = 0; row < h; row++)
            Marshal.Copy(pixels, row * w, data.Scan0 + row * data.Stride, w);
        _bitmap.UnlockBits(data);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_bitmap == null) RenderBitmap();
        var g = e.Graphics;
        g.DrawImageUnscaled(_bitmap!, 0, 0);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        DrawGrid(g);

        using var font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        foreach (var m in _markers)
        {
            var d = LatLonToDisk(m.Lat, m.Lon);
            if (d == null) continue;
            var p = DiskToScreen(d.Value.X, d.Value.Y);
            Color c = m.Score >= 80 ? Color.LimeGreen : m.Score >= 65 ? Color.YellowGreen
                    : m.Score >= 50 ? Color.Gold : Color.OrangeRed;
            using var brush = new SolidBrush(c);
            g.FillEllipse(brush, p.X - 6, p.Y - 6, 12, 12);
            g.DrawEllipse(Pens.Black, p.X - 6, p.Y - 6, 12, 12);
            string label = $"{m.Score:F0}";
            g.DrawString(label, font, Brushes.Black, p.X + 8, p.Y - 7);
            g.DrawString(label, font, brush, p.X + 7, p.Y - 8);
        }

        if (_pending != null)
        {
            var d = LatLonToDisk(_pending.Value.Lat, _pending.Value.Lon);
            if (d != null)
            {
                var p = DiskToScreen(d.Value.X, d.Value.Y);
                using var pen = new Pen(Color.Cyan, 2);
                g.DrawEllipse(pen, p.X - 10, p.Y - 10, 20, 20);
                g.DrawLine(pen, p.X - 16, p.Y, p.X - 6, p.Y);
                g.DrawLine(pen, p.X + 6, p.Y, p.X + 16, p.Y);
                g.DrawLine(pen, p.X, p.Y - 16, p.X, p.Y - 6);
                g.DrawLine(pen, p.X, p.Y + 6, p.X, p.Y + 16);
            }
        }

        using var hint = new Font("Segoe UI", 9f);
        string info = _map == null
            ? "LOLA haritası yükleniyor..."
            : $"Kuzey ↑   Doğu →   |   Tekerlek: yakınlaştır   Sürükle: kaydır   Tık: nokta seç   |   Harita: LRO LOLA {_map.Product}";
        g.DrawString(info, hint, Brushes.LightGray, 8, ClientSize.Height - 22);
        g.DrawString("K", font, Brushes.White, ClientSize.Width / 2f - 5, 2);
    }

    private void DrawGrid(Graphics g)
    {
        using var pen = new Pen(Color.FromArgb(55, 120, 180, 255), 1);
        // Enlem çizgileri (her 30°)
        for (int lat = -60; lat <= 60; lat += 30)
        {
            var pts = new List<PointF>();
            for (int lon = -90; lon <= 90; lon += 3)
            {
                var d = LatLonToDisk(lat, lon);
                if (d != null) pts.Add(DiskToScreen(d.Value.X, d.Value.Y));
            }
            if (pts.Count > 1) g.DrawLines(pen, pts.ToArray());
        }
        // Boylam çizgileri (her 30°)
        for (int lon = -90; lon <= 90; lon += 30)
        {
            var pts = new List<PointF>();
            for (int lat = -90; lat <= 90; lat += 3)
            {
                var d = LatLonToDisk(lat, lon);
                if (d != null) pts.Add(DiskToScreen(d.Value.X, d.Value.Y));
            }
            if (pts.Count > 1) g.DrawLines(pen, pts.ToArray());
        }
    }

    // ---------------- Fare ----------------
    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        Focus();   // fare tekerleği olaylarını alabilmek için
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _mouseDown = e.Location;
        _dragging = false;
        _dragStartX = _viewX;
        _dragStartY = _viewY;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.Button == MouseButtons.Left)
        {
            int dxp = e.X - _mouseDown.X, dyp = e.Y - _mouseDown.Y;
            if (_dragging || Math.Abs(dxp) + Math.Abs(dyp) > 5)
            {
                _dragging = true;
                double s = DiskRadiusPx * _zoom;
                _viewX = _dragStartX - dxp / s;
                _viewY = _dragStartY + dyp / s;
                InvalidateBitmap();
            }
        }

        var ll = ScreenToLatLon(e.Location);
        if (ll == null) { HoverTextChanged?.Invoke("Ay diskinin dışında"); return; }
        string elev = _map != null ? $"  |  Yükseklik ≈ {_map.ElevationAt(ll.Value.Lat, ll.Value.Lon):F0} m" : "";
        HoverTextChanged?.Invoke(
            $"Enlem {Math.Abs(ll.Value.Lat):F3}° {(ll.Value.Lat >= 0 ? "K" : "G")}   " +
            $"Boylam {Math.Abs(ll.Value.Lon):F3}° {(ll.Value.Lon >= 0 ? "D" : "B")}{elev}");
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && !_dragging)
        {
            var ll = ScreenToLatLon(e.Location);
            if (ll != null) SiteClicked?.Invoke(ll.Value.Lat, ll.Value.Lon);
        }
        _dragging = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var before = ScreenToDisk(e.X, e.Y);
        double factor = e.Delta > 0 ? 1.4 : 1 / 1.4;
        _zoom = Math.Clamp(_zoom * factor, 1.0, 40.0);
        if (_zoom <= 1.0001) { _viewX = 0; _viewY = 0; }
        else
        {
            // İmlecin altındaki nokta sabit kalsın
            var after = ScreenToDisk(e.X, e.Y);
            _viewX += before.X - after.X;
            _viewY += before.Y - after.Y;
        }
        InvalidateBitmap();
    }
}

using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Windows.Forms;
using AyInisNavigatoru.Analysis;

namespace AyInisNavigatoru.UI;

/// <summary>
/// Ana pencere. Sade düzen:
///   ÜSTTE  : tek sıra araç çubuğu (en sağda 🌐 dil düğmesi) + altında seçili seçeneğin açıklaması
///   SOLDA  : 3B Ay küresi (ısı haritası açıkken Ay'ın tamamı uygunluğa göre renklenir)
///   SAĞDA  : yalnızca rapor (tek parça)
///   ALTTA  : durum çubuğu (imleç konumu, ilerleme, günlük)
/// Tüm metinler iki dillidir (L.T). Dil değişince ApplyLanguage() her şeyi yeniden yazar.
/// </summary>
public sealed class MainForm : Form
{
    private static readonly Color PanelColor = Color.FromArgb(18, 20, 30);
    private static readonly Color BarColor = Color.FromArgb(24, 28, 44);
    private static readonly Color AccentColor = Color.FromArgb(45, 95, 170);

    private readonly LandingNavigator _navigator = new();
    private readonly MoonGlobeControl _globe = new() { Dock = DockStyle.Fill };

    private readonly RichTextBox _report = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None,
        BackColor = PanelColor, ForeColor = Color.Gainsboro,
        Font = new Font("Segoe UI", 10f), DetectUrls = false
    };

    // Araç çubuğu
    private readonly Label _capProfile = Caption();
    private readonly Label _capGoto = Caption();
    private readonly ComboBox _profile = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    private readonly ComboBox _goto = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, DropDownHeight = 420 };
    private readonly CheckBox _heatToggle = ToggleButton();
    private readonly ComboBox _layer = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 175 };
    private readonly CheckBox _landingsToggle = ToggleButton(true);
    private readonly CheckBox _realSun = ToggleButton();
    private readonly Button _compareBtn = SmallButton();
    private readonly Button _htmlBtn = SmallButton();
    private readonly Button _settingsBtn = SmallButton();
    private readonly Button _helpBtn = SmallButton();
    private readonly Button _resetBtn = SmallButton();
    private readonly Button _langBtn = new()
    {
        AutoSize = false, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = AccentColor,
        Margin = new Padding(12, 3, 4, 3), UseMnemonic = false,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Cursor = Cursors.Hand
    };
    private readonly Label _hint = new()
    {
        Dock = DockStyle.Fill, ForeColor = Color.Silver, Padding = new Padding(10, 2, 10, 2),
        Font = new Font("Segoe UI", 9f), TextAlign = ContentAlignment.MiddleLeft
    };

    // Ayarlar
    private readonly NumericUpDown _radius = new() { Minimum = 250, Maximum = 10000, Increment = 250, Value = 2000, Width = 90 };
    private Form? _settingsForm;

    // Durum çubuğu
    private readonly ToolStripStatusLabel _hover = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripProgressBar _progress = new() { Width = 160, Visible = false };
    private readonly ToolStripStatusLabel _state = new();
    private readonly ToolStripButton _logButton = new() { ForeColor = Color.LightSkyBlue };
    private readonly StringBuilder _logText = new();
    private readonly ToolTip _tips = new() { AutoPopDelay = 20000, InitialDelay = 300 };

    // Durum
    private bool _busy, _globalBusy;
    private SiteEvaluation? _lastEval;
    private readonly List<SiteEvaluation> _history = new();
    private MoonMapData? _mapData;
    private GlobalSuitabilityMap? _global;
    private List<LandingSite> _gotoSites = new();

    private static readonly HeatLayerKind[] LayerKinds =
    {
        HeatLayerKind.Overall, HeatLayerKind.Slope, HeatLayerKind.Hazard, HeatLayerKind.Sun,
        HeatLayerKind.Thermal, HeatLayerKind.Comm, HeatLayerKind.Terrain
    };

    private static string LayerDesc(HeatLayerKind k) => k switch
    {
        HeatLayerKind.Overall => L.T("Genel uygunluk: seçili göreve göre toplam puan. Koyu kırmızı = kritik sınır aşıldı (çok dik, çok kayalık ya da Dünya görünmüyor).",
                                     "Overall suitability: total score for the selected mission. Dark red = critical limit exceeded (too steep, too rocky, or Earth not visible)."),
        HeatLayerKind.Slope => L.T("Yüzey eğimi: kırmızı yerler krater duvarları, dağ yamaçları gibi dik yüzeylerdir (LOLA, ~2 km ölçeği).",
                                   "Surface slope: red areas are steep surfaces such as crater walls and mountain slopes (LOLA, ~2 km scale)."),
        HeatLayerKind.Hazard => L.T("Krater ve taş: Diviner kaya yoğunluğu + krater duvarları. Kırmızı = kayalık veya kraterli. Bu kriter puanı yalnızca düşürür.",
                                    "Craters and rocks: Diviner rock abundance + crater walls. Red = rocky or cratered. This criterion can only lower the score."),
        HeatLayerKind.Sun => L.T("Güneş ışığı: ekvatora yakın yerlerde öğlen güneşi daha yüksektir → daha fazla güneş enerjisi.",
                                 "Sunlight: near the equator the noon Sun is higher → more solar energy."),
        HeatLayerKind.Thermal => L.T("Sıcaklık: gündüz en yüksek ve gece en düşük sıcaklığın elektronik cihazlar için ne kadar zorlayıcı olduğu.",
                                     "Temperature: how demanding the daytime maximum and nighttime minimum are for electronics."),
        HeatLayerKind.Comm => L.T("Dünya ile iletişim: yakın yüzün ortası yeşil; kenarlarda Dünya ufka yaklaşır, uzak yüzde hiç görünmez (kırmızı).",
                                  "Communication with Earth: the center of the near side is green; toward the limb Earth nears the horizon, and it is never visible from the far side (red)."),
        HeatLayerKind.Terrain => L.T("Arazi yapısı: pürüzlülük ve yükseklik farkı. Düz lav denizleri (mare) yeşil, engebeli yaylalar kırmızıdır.",
                                     "Terrain: roughness and relief. Flat lava plains (maria) are green, rugged highlands are red."),
        _ => ""
    };

    private static string DefaultHint => L.T(
        "① Ay'ı sürükleyerek döndürün   ② Bir noktaya tıklayın → rapor sağda çıkar   " +
        "③ \"🌡 Isı haritası\" ile Ay'ın tamamının uygunluğunu görün   ④ \"⚖ Karşılaştır\" ile noktaları kıyaslayın",
        "① Drag to rotate the Moon   ② Click a site → the report appears on the right   " +
        "③ \"🌡 Heat map\" shows the suitability of the whole Moon   ④ \"⚖ Compare\" to compare sites");

    public MainForm()
    {
        Width = 1600; Height = 960;
        MinimumSize = new Size(1100, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(8, 10, 20);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 9.5f);

        BuildLayout();
        WireEvents();
        ApplyLanguage();
        WriteWelcome();
    }

    // ============================== Yerleşim ==============================
    private static CheckBox ToggleButton(bool isChecked = false) => new()
    {
        Appearance = Appearance.Button, AutoSize = false, FlatStyle = FlatStyle.Flat, Checked = isChecked,
        ForeColor = Color.White, BackColor = Color.FromArgb(45, 50, 70), Margin = new Padding(4, 3, 4, 3),
        TextAlign = ContentAlignment.MiddleCenter, UseMnemonic = false,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Cursor = Cursors.Hand
    };

    private static Button SmallButton() => new()
    {
        AutoSize = false, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, UseMnemonic = false,
        BackColor = Color.FromArgb(45, 50, 70), Margin = new Padding(4, 3, 4, 3), Cursor = Cursors.Hand
    };

    private static Label Caption() => new()
    {
        AutoSize = true, ForeColor = Color.LightGray, Margin = new Padding(10, 9, 2, 0),
        Font = new Font("Segoe UI", 9f, FontStyle.Bold)
    };

    private static Panel Separator() => new() { Width = 1, Height = 28, BackColor = Color.FromArgb(70, 75, 100), Margin = new Padding(8, 5, 8, 0) };

    private static void StyleToggle(CheckBox cb, Color on) =>
        cb.CheckedChanged += (_, _) => cb.BackColor = cb.Checked ? on : Color.FromArgb(45, 50, 70);

    private void BuildLayout()
    {
        _bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
            BackColor = BarColor, Padding = new Padding(6, 4, 6, 4)
        };

        _layer.Enabled = false;
        StyleToggle(_heatToggle, Color.FromArgb(160, 70, 30));
        StyleToggle(_landingsToggle, Color.FromArgb(40, 90, 140));
        StyleToggle(_realSun, Color.FromArgb(150, 120, 30));
        _landingsToggle.BackColor = Color.FromArgb(40, 90, 140);

        _bar.Controls.AddRange(new Control[]
        {
            _capProfile, _profile,
            _capGoto, _goto,
            Separator(),
            _heatToggle, _layer,
            Separator(),
            _landingsToggle, _realSun,
            Separator(),
            _compareBtn, _htmlBtn, _settingsBtn, _helpBtn, _resetBtn,
            _langBtn
        });

        var hintStrip = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.FromArgb(14, 16, 26) };
        hintStrip.Controls.Add(_hint);

        var right = new Panel { Dock = DockStyle.Right, Width = 560, BackColor = PanelColor, Padding = new Padding(14, 10, 8, 10) };
        right.Controls.Add(_report);
        var divider = new Panel { Dock = DockStyle.Right, Width = 2, BackColor = Color.FromArgb(40, 45, 70) };

        var status = new StatusStrip { BackColor = BarColor, ForeColor = Color.Gainsboro };
        status.Items.Add(_hover);
        status.Items.Add(_progress);
        status.Items.Add(_state);
        status.Items.Add(_logButton);

        // Dock sırası: önce Fill, sonra kenarlar
        Controls.Add(_globe);
        Controls.Add(divider);
        Controls.Add(right);
        Controls.Add(hintStrip);
        Controls.Add(_bar);
        Controls.Add(status);
    }

    /// <summary>Tüm görünür metinleri seçili dile göre yazar. Dil değişince tekrar çağrılır.</summary>
    private void ApplyLanguage()
    {
        Text = L.T("Ay İniş Navigatörü 3.2 — NASA LRO verileriyle iniş uygunluk analizi (Space Apps Eskişehir)",
                   "Lunar Landing Navigator 3.2 — Landing suitability analysis with NASA LRO data (Space Apps Eskişehir)");

        _capProfile.Text = L.T("Görev:", "Mission:");
        _capGoto.Text = L.T("Git:", "Go to:");
        _bar.SuspendLayout();
        SetButtonText(_heatToggle, L.T("🌡 Isı haritası", "🌡 Heat map"));
        SetButtonText(_landingsToggle, L.T("🛬 Tarihî inişler", "🛬 Historic landings"));
        SetButtonText(_realSun, L.T("☀ Gerçek güneş", "☀ Real Sun"));
        SetButtonText(_compareBtn, L.T("⚖ Karşılaştır", "⚖ Compare"));
        SetButtonText(_htmlBtn, L.T("📄 Rapor (tarayıcı)", "📄 Report (browser)"));
        SetButtonText(_settingsBtn, L.T("⚙ Ayarlar", "⚙ Settings"));
        SetButtonText(_helpBtn, L.T("❓ Nasıl kullanılır?", "❓ How to use"));
        SetButtonText(_resetBtn, L.T("⟲ Sıfırla", "⟲ Reset"));
        SetButtonText(_langBtn, L.En ? "🌐 Türkçe" : "🌐 English");
        _bar.ResumeLayout(true);
        _logButton.Text = L.T("📜 Ayrıntılı günlük", "📜 Detailed log");
        if (!_busy && !_globalBusy) _state.Text = L.T("Hazır", "Ready");

        // Açılır listeler: seçimi koruyarak yeniden doldur
        int pi = Math.Max(0, _profile.SelectedIndex);
        _profile.Items.Clear();
        foreach (var p in MissionProfile.All) _profile.Items.Add(p);
        _profile.SelectedIndex = pi;

        _gotoSites = LandingCatalog.All.Select(l => l.ToSite()).ToList();
        _gotoSites.Add(new LandingSite(L.T("Zorlu: Tycho Krateri merkezi", "Challenging: Tycho crater center"), -43.31, -11.36));
        _gotoSites.Add(new LandingSite(L.T("Zorlu: Copernicus Krateri kenarı", "Challenging: Copernicus crater rim"), 9.62, -18.70));
        _suppressGoto = true;
        _goto.Items.Clear();
        _goto.Items.Add(L.T("— bir iniş noktasına git —", "— go to a landing site —"));
        _goto.Items.AddRange(_gotoSites.Select(s => (object)s.Name).ToArray());
        _goto.SelectedIndex = 0;
        _suppressGoto = false;

        int li = Math.Max(0, _layer.SelectedIndex);
        _layer.Items.Clear();
        foreach (var k in LayerKinds) _layer.Items.Add(MoonGlobeControl.LayerName(k));
        _layer.SelectedIndex = li;

        _tips.SetToolTip(_profile, L.T("Görev türüne göre kriterlerin önemi ve kritik sınırlar değişir.",
                                       "Criteria weights and critical limits change with the mission type."));
        _tips.SetToolTip(_goto, L.T("Ay'a yapılmış gerçek inişler (NASA NSSDC koordinatları) ve zorlu örnekler. Seçince oraya gidip analiz eder.",
                                    "Real lunar landings (NASA NSSDC coordinates) and challenging examples. Selecting one flies there and analyzes it."));
        _tips.SetToolTip(_heatToggle, L.T("Ay'ın TAMAMINI uygunluğuna göre renklendirir (yeşil uygun, kırmızı uygun değil).",
                                          "Colors the ENTIRE Moon by suitability (green suitable, red not suitable)."));
        _tips.SetToolTip(_layer, L.T("Isı haritasında hangi kriterin gösterileceği", "Which criterion the heat map shows"));
        _tips.SetToolTip(_landingsToggle, L.T("Apollo, Luna, Chang'e, Chandrayaan-3, SLIM... tüm başarılı inişleri gösterir.",
                                              "Shows all successful landings: Apollo, Luna, Chang'e, Chandrayaan-3, SLIM..."));
        _tips.SetToolTip(_realSun, L.T("Gece-gündüz çizgisini bugünün tarihine göre gösterir.", "Shows today's day–night boundary."));
        _tips.SetToolTip(_compareBtn, L.T("Analiz ettiğiniz tüm noktaları tek tabloda yan yana karşılaştırır.",
                                          "Compares all analyzed sites side by side in one table."));
        _tips.SetToolTip(_htmlBtn, L.T("Raporu tarayıcıda açar; buradan yazdırabilir veya PDF olarak kaydedebilirsiniz.",
                                       "Opens the report in your browser, where you can print it or save it as PDF."));
        _tips.SetToolTip(_settingsBtn, L.T("İniş elipsi yarıçapı", "Landing ellipse radius"));
        _tips.SetToolTip(_langBtn, L.T("Uygulama dilini İngilizce yap", "Switch the application to Turkish"));

        SetHint(DefaultHint);
        _bar.PerformLayout();
        _bar.Refresh();

        // _radius kutusu ayarlar penceresinin içinde durur; pencere yenilenirken onu koru
        _radius.Parent?.Controls.Remove(_radius);
        _settingsForm?.Dispose();
        BuildSettingsForm();
    }

    private bool _suppressGoto;
    private FlowLayoutPanel _bar = null!;

    /// <summary>
    /// Düğmenin yazısını değiştirir ve genişliğini yazıya göre AÇIKÇA yeniden hesaplar.
    /// (Otomatik boyutlandırma, yazı dil değişiminde değişince eski genişlikte kalabiliyor
    /// ve yazı kesik ya da boş görünebiliyordu.)
    /// </summary>
    private void SetButtonText(ButtonBase b, string text)
    {
        b.Text = text;
        var size = TextRenderer.MeasureText(text, b.Font, Size.Empty,
                                            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        // Emoji simgeleri yedek yazı tipiyle çizildiği için ölçümden geniş çıkabilir: her biri için pay bırak
        int emoji = text.Count(char.IsHighSurrogate);
        b.Size = new Size(size.Width + LogicalToDeviceUnits(30) + emoji * LogicalToDeviceUnits(8),
                          Math.Max(LogicalToDeviceUnits(30), size.Height + LogicalToDeviceUnits(12)));
        b.Invalidate();
    }

    private void OnLanguageChanged()
    {
        ApplyLanguage();

        // Analiz edilmiş noktaların tüm metinleri yeni dilde yeniden üretilir (veri tekrar indirilmez)
        var profile = CurrentProfile;
        foreach (var ev in _history) SuitabilityScorer.Score(ev, profile);
        if (_lastEval != null && !_history.Contains(_lastEval)) SuitabilityScorer.Score(_lastEval, profile);

        if (_busy) { /* süren analiz bittiğinde rapor yeni dilde çizilir */ }
        else if (_lastEval != null) RenderReport(_lastEval);
        else WriteWelcome();

        if (_global != null)
        {
            var g = _global;
            Task.Run(() => g.ApplyProfile(profile)).ContinueWith(_ => BeginInvoke(new Action(() => _globe.Redraw())));
        }
        if (_heatToggle.Checked) SetHint(LayerDesc(LayerKinds[_layer.SelectedIndex]));
        _globe.Redraw();
    }

    private void BuildSettingsForm()
    {
        var f = new Form
        {
            Text = L.T("Ayarlar", "Settings"), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(560, 250),
            BackColor = PanelColor, ForeColor = Color.Gainsboro, Font = new Font("Segoe UI", 9.5f)
        };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16) };
        flow.Controls.Add(new Label
        {
            Text = L.T("İniş elipsi yarıçapı (metre)", "Landing ellipse radius (meters)"), AutoSize = true,
            ForeColor = Color.White, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold)
        });
        _radius.Parent?.Controls.Remove(_radius);
        flow.Controls.Add(_radius);
        flow.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(510, 0), ForeColor = Color.DarkGray, Margin = new Padding(0, 6, 0, 6),
            Text = L.T("İniş aracı hedefe tam olarak inemez; bu yarıçap içindeki herhangi bir yere inebilir. " +
                       "Eğim, kaya ve pürüzlülük bu dairenin tamamında hesaplanır.\n" +
                       "250–500 m: modern hassas iniş (ör. SLIM) · 1–2 km: günümüz robotik araçları · 5 km+: Apollo dönemi",
                       "A lander cannot touch down exactly on target; it may land anywhere within this radius. " +
                       "Slope, rocks and roughness are evaluated over this entire circle.\n" +
                       "250–500 m: modern precision landing (e.g. SLIM) · 1–2 km: today's robotic landers · 5 km+: Apollo era")
        });
        var ok = new Button
        {
            Text = L.T("Tamam", "OK"), AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = AccentColor,
            ForeColor = Color.White, DialogResult = DialogResult.OK, Margin = new Padding(0, 10, 0, 0)
        };
        flow.Controls.Add(ok);
        f.AcceptButton = ok;
        f.Controls.Add(flow);
        _settingsForm = f;
    }

    private void ShowHelp()
    {
        string text = L.T(
            "AY İNİŞ NAVİGATÖRÜ — NASIL KULLANILIR?\r\n\r\n" +
            "1) AY'I DÖNDÜRÜN\r\n   Sol tuşla sürükleyin: Ay döner (uzak yüz dahil). Fare tekerleği: yakınlaştırır.\r\n\r\n" +
            "2) BİR NOKTAYA TIKLAYIN\r\n   O noktanın verileri NASA arşivinden indirilir ve sağda rapor çıkar:\r\n" +
            "   genel puan (0-100), karar ve 6 kriterin her biri. Rapor, en yakın gerçek iniş noktasını da söyler.\r\n\r\n" +
            "3) 🌡 ISI HARİTASI\r\n   Ay'ın TAMAMI uygunluğuna göre renklenir: yeşil uygun, sarı dikkat, kırmızı uygun değil,\r\n" +
            "   koyu kırmızı iniş yasak. Yanındaki listeden tek bir kriteri (eğim, krater ve taş, güneş,\r\n" +
            "   sıcaklık, iletişim, arazi) ayrı ayrı görebilirsiniz. İmleç altındaki hücrenin değerleri\r\n" +
            "   alt çubukta yazar. Küresel harita ~2 km ölçeğinde bir ön elemedir; kesin karar için noktaya tıklayın.\r\n\r\n" +
            "4) 🛬 TARİHÎ İNİŞLER\r\n   Ay'a yapılmış tüm başarılı inişler (Apollo altın, robotik mavi). Koordinatlar NASA NSSDC'den.\r\n" +
            "   \"Git\" listesinden birini seçince Ay oraya döner ve analiz yapılır.\r\n\r\n" +
            "5) ⚖ KARŞILAŞTIR\r\n   Analiz ettiğiniz tüm noktalar tek tabloda. Sütun başlığına tıklayarak sıralayın,\r\n" +
            "   bir satıra çift tıklayarak o noktaya gidin.\r\n\r\n" +
            "6) 📄 RAPOR (TARAYICI)\r\n   Raporu tarayıcıda açar; yazdırabilir veya PDF olarak kaydedebilirsiniz.\r\n\r\n" +
            "7) GÖREV PROFİLİ\r\n   Robotik görev, insanlı iniş, Ay üssü, gezgin. Değiştirince tüm noktalar ve ısı haritası\r\n" +
            "   veri tekrar indirilmeden anında yeniden puanlanır.\r\n\r\n" +
            "8) 🌐 DİL\r\n   Sağ üstteki düğmeyle uygulama Türkçe ve İngilizce arasında geçiş yapar. Seçim hatırlanır.\r\n\r\n" +
            "KRİTERLER\r\n" +
            "   • Yüzey eğimi (LRO LOLA): dik yüzeyde iniş aracı devrilir.\r\n" +
            "   • Krater ve taş yoğunluğu (LRO Diviner + LOLA): her zaman puanı DÜŞÜRÜR.\r\n" +
            "   • Güneş ışığı: güneş panelleri için güneş yüksekliği ve tepelerin gölgesi.\r\n" +
            "   • Sıcaklık: gündüz ışınım dengesi modeli, gece Diviner ölçümü.\r\n" +
            "   • Dünya ile iletişim: Dünya ufkun üzerinde mi? (librasyon dahil)\r\n" +
            "   • Yükseklik ve arazi: pürüzlülük ve yükseklik farkı.\r\n\r\n" +
            "İPUCU: İlk analizde veri indirildiği için 20-60 saniye sürebilir. Aynı bölge ikinci kez anında açılır.",

            "LUNAR LANDING NAVIGATOR — HOW TO USE\r\n\r\n" +
            "1) ROTATE THE MOON\r\n   Drag with the left mouse button to rotate the Moon (including the far side). Mouse wheel: zoom.\r\n\r\n" +
            "2) CLICK A SITE\r\n   Data for that site is downloaded from NASA archives and a report appears on the right:\r\n" +
            "   overall score (0-100), verdict and each of the 6 criteria. The report also names the nearest real landing site.\r\n\r\n" +
            "3) 🌡 HEAT MAP\r\n   The ENTIRE Moon is colored by suitability: green suitable, yellow caution, red not suitable,\r\n" +
            "   dark red no landing. Use the list next to it to view a single criterion (slope, craters and rocks,\r\n" +
            "   sunlight, temperature, communication, terrain). Values of the cell under the cursor appear in the\r\n" +
            "   status bar. The global map is a ~2 km-scale pre-screening; click a site for a final decision.\r\n\r\n" +
            "4) 🛬 HISTORIC LANDINGS\r\n   All successful lunar landings (Apollo gold, robotic blue). Coordinates from NASA NSSDC.\r\n" +
            "   Selecting one from the \"Go to\" list rotates the Moon there and analyzes it.\r\n\r\n" +
            "5) ⚖ COMPARE\r\n   All analyzed sites in one table. Click a column header to sort,\r\n" +
            "   double-click a row to go to that site.\r\n\r\n" +
            "6) 📄 REPORT (BROWSER)\r\n   Opens the report in your browser; you can print it or save it as PDF.\r\n\r\n" +
            "7) MISSION PROFILE\r\n   Robotic mission, crewed landing, lunar base, rover. Changing it instantly re-scores all sites\r\n" +
            "   and the heat map without downloading data again.\r\n\r\n" +
            "8) 🌐 LANGUAGE\r\n   The button at the top right switches the app between Turkish and English. Your choice is remembered.\r\n\r\n" +
            "CRITERIA\r\n" +
            "   • Surface slope (LRO LOLA): a lander tips over on steep ground.\r\n" +
            "   • Crater and rock density (LRO Diviner + LOLA): always LOWERS the score.\r\n" +
            "   • Sunlight: Sun elevation and terrain shadowing for solar panels.\r\n" +
            "   • Temperature: radiative-equilibrium model for daytime, Diviner measurements for night.\r\n" +
            "   • Communication with Earth: is Earth above the horizon? (including libration)\r\n" +
            "   • Elevation and terrain: roughness and relief.\r\n\r\n" +
            "TIP: The first analysis may take 20-60 seconds because data is downloaded. The same area opens instantly the second time.");
        using var f = new Form
        {
            Text = L.T("Nasıl kullanılır?", "How to use"), ClientSize = new Size(780, 660), StartPosition = FormStartPosition.CenterParent,
            BackColor = PanelColor, MinimizeBox = false
        };
        f.Controls.Add(new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = text,
            BackColor = PanelColor, ForeColor = Color.Gainsboro, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10.5f)
        });
        f.ShowDialog(this);
    }

    private void ShowLog()
    {
        using var f = new Form
        {
            Text = L.T("Ayrıntılı günlük (veri indirme ve hesaplama adımları)", "Detailed log (data download and computation steps)"),
            ClientSize = new Size(820, 480), StartPosition = FormStartPosition.CenterParent, BackColor = PanelColor
        };
        var box = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
            Text = _logText.ToString(), BackColor = Color.FromArgb(10, 12, 18), ForeColor = Color.Silver,
            BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9f)
        };
        f.Controls.Add(box);
        f.Shown += (_, _) => { box.SelectionStart = box.TextLength; box.ScrollToCaret(); };
        f.ShowDialog(this);
    }

    private void SetHint(string text) => _hint.Text = text;

    private void WireEvents()
    {
        _globe.HoverTextChanged += t => _hover.Text = t;
        _globe.SiteClicked += async (lat, lon) =>
            await EvaluateAsync(new LandingSite(L.T($"Seçilen nokta ({lat:F3}, {lon:F3})", $"Selected site ({lat:F3}, {lon:F3})"), lat, lon));
        _navigator.LogMessage += AppendLog;

        _langBtn.Click += (_, _) => L.Toggle();
        L.Changed += OnLanguageChanged;

        _compareBtn.Click += (_, _) => ShowComparison();
        _htmlBtn.Click += (_, _) => OpenHtmlReport();
        _settingsBtn.Click += (_, _) => _settingsForm?.ShowDialog(this);
        _helpBtn.Click += (_, _) => ShowHelp();
        _logButton.Click += (_, _) => ShowLog();
        _resetBtn.Click += (_, _) =>
        {
            _globe.ResetView();
            _globe.ClearMarkers();
            _history.Clear();
            _heatToggle.Checked = false;
            SetHint(DefaultHint);
        };

        _profile.SelectedIndexChanged += async (_, _) => await OnProfileChangedAsync();

        _goto.SelectedIndexChanged += async (_, _) =>
        {
            if (_suppressGoto || _goto.SelectedIndex <= 0) return;
            var site = _gotoSites[_goto.SelectedIndex - 1];
            _globe.FlyTo(site.LatitudeDeg, site.LongitudeEastDeg, 6);
            await EvaluateAsync(site);
        };

        _heatToggle.CheckedChanged += async (_, _) => await OnHeatToggledAsync();
        _layer.SelectedIndexChanged += (_, _) =>
        {
            if (_layer.SelectedIndex < 0) return;
            var kind = LayerKinds[_layer.SelectedIndex];
            SetHint(LayerDesc(kind));
            if (_heatToggle.Checked && _global != null) _globe.SetGlobalHeatmap(_global, kind);
        };
        _landingsToggle.CheckedChanged += (_, _) =>
        {
            _globe.SetShowLandings(_landingsToggle.Checked);
            if (_landingsToggle.Checked)
                SetHint(L.T($"Tarihî inişler: {LandingCatalog.All.Length} başarılı iniş (altın = insanlı Apollo, mavi = robotik). " +
                            "Koordinatlar NASA NSSDC'den. Etiketler yakınlaştırınca görünür.",
                            $"Historic landings: {LandingCatalog.All.Length} successful landings (gold = crewed Apollo, blue = robotic). " +
                            "Coordinates from NASA NSSDC. Labels appear when zoomed in."));
        };
        _realSun.CheckedChanged += (_, _) =>
        {
            _globe.SetRealSunLighting(_realSun.Checked, SolarAnalyzer.SubsolarLongitude(DateTime.UtcNow));
            SetHint(_realSun.Checked
                ? L.T("Gerçek güneş: Ay şu an nasıl aydınlanıyorsa öyle görünür; gece-gündüz çizgisi gerçektir. Karanlık taraf da seçilebilir.",
                      "Real Sun: the Moon is lit as it is right now; the day–night boundary is real. The dark side can still be selected.")
                : L.T("Sabit ışık: yüzey şekilleri (krater, tepe) her yerde net görünsün diye soldan-yukarıdan aydınlatılır.",
                      "Fixed light: lit from the upper left so surface features (craters, hills) are clearly visible everywhere."));
        };

        Load += async (_, _) => await LoadMapAsync();
        FormClosed += (_, _) => { L.Changed -= OnLanguageChanged; _navigator.Dispose(); };
    }

    private MissionProfile CurrentProfile => (MissionProfile)_profile.SelectedItem!;

    private void AppendLog(string msg)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action<string>(AppendLog), msg); return; }
        _logText.AppendLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
        if (_busy || _globalBusy) _state.Text = msg.Length > 90 ? msg[..90] + "…" : msg;
    }

    // ============================== Harita ve ısı haritası ==============================
    private async Task LoadMapAsync()
    {
        _state.Text = L.T("Ay haritası yükleniyor...", "Loading the Moon map...");
        foreach (var product in new[] { "LDEM_4", "LDEM_16" })
        {
            var mapData = await Task.Run(async () =>
            {
                var block = await _navigator.LoadGlobalDemAsync(product);
                if (block == null) return null;
                return MoonMapData.FromLola(block.RawData, block.Samples, block.RowCount, block.Source.Resolution, product);
            });
            if (mapData != null)
            {
                _mapData = mapData;
                _globe.SetMap(mapData);
                AppendLog(L.T($"3B küre hazır: LOLA {product} ({mapData.Width}×{mapData.Height})",
                              $"3D globe ready: LOLA {product} ({mapData.Width}×{mapData.Height})"));
                // Isı haritası kaba haritayla oluşturulduysa ayrıntılı haritayla yenile
                if (_global != null && _global.DemProduct != product)
                {
                    _global = null;
                    if (_heatToggle.Checked) await OnHeatToggledAsync();
                }
            }
        }
        _state.Text = L.T("Hazır — Ay'ı döndürün ve bir noktaya tıklayın", "Ready — rotate the Moon and click a site");
    }

    private async Task OnHeatToggledAsync()
    {
        _layer.Enabled = _heatToggle.Checked;
        if (!_heatToggle.Checked)
        {
            _globe.SetGlobalHeatmap(_global, HeatLayerKind.Off);
            SetHint(DefaultHint);
            return;
        }
        if (_global == null)
        {
            if (_mapData == null)
            {
                _state.Text = L.T("Ay haritası henüz yüklenmedi, birkaç saniye bekleyin.", "The Moon map is not loaded yet, please wait a few seconds.");
                _heatToggle.Checked = false;
                return;
            }
            if (_globalBusy) return;
            _globalBusy = true;
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.Visible = true;
            _state.Text = L.T("Ay'ın tamamı puanlanıyor (1 milyondan fazla hücre)...", "Scoring the entire Moon (over 1 million cells)...");
            try
            {
                var md = _mapData;
                var profile = CurrentProfile;
                var g = await Task.Run(() =>
                {
                    var map = GlobalSuitabilityMap.Build(md.Elevation, md.Width, md.Height, md.Resolution, md.Product);
                    map.ApplyProfile(profile);
                    return map;
                });
                _global = g;
                AppendLog(L.T($"Küresel uygunluk haritası hazır ({md.Product}): ortalama {g.MeanOverall:F1}, uygun yüzey %{g.PercentSuitable:F1}",
                              $"Global suitability map ready ({md.Product}): mean {g.MeanOverall:F1}, suitable surface {g.PercentSuitable:F1}%"));
                _ = LoadGlobalRocksAsync(g);
            }
            finally
            {
                _globalBusy = false;
                _progress.Visible = false;
            }
        }
        if (_heatToggle.Checked && _global != null)
        {
            var kind = LayerKinds[_layer.SelectedIndex];
            _globe.SetGlobalHeatmap(_global, kind);
            SetHint(LayerDesc(kind));
            _state.Text = L.T($"Isı haritası: Ay yüzeyinin %{_global.PercentSuitable:F0}'i bu görev için uygun (puan ≥ 65)",
                              $"Heat map: {_global.PercentSuitable:F0}% of the lunar surface is suitable for this mission (score ≥ 65)");
        }
    }

    private async Task LoadGlobalRocksAsync(GlobalSuitabilityMap map)
    {
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Value = 0;
        _progress.Visible = true;
        var progress = new Progress<double>(f =>
        {
            _progress.Value = Math.Clamp((int)(f * 100), 0, 100);
            _state.Text = L.T($"Diviner kaya haritası ekleniyor... %{f * 100:F0}", $"Adding Diviner rock map... {f * 100:F0}%");
        });
        bool ok = await Task.Run(() => _navigator.LoadGlobalRocksAsync(map, progress));
        _progress.Visible = false;
        if (ok && ReferenceEquals(map, _global))
        {
            var profile = CurrentProfile;
            await Task.Run(() => map.ApplyProfile(profile));
            _globe.Redraw();
            _state.Text = L.T($"Kaya verisi eklendi — Ay yüzeyinin %{map.PercentSuitable:F0}'i bu görev için uygun",
                              $"Rock data added — {map.PercentSuitable:F0}% of the lunar surface is suitable for this mission");
        }
        else if (!ok)
        {
            _state.Text = L.T("Kaya haritası indirilemedi; krater ve taş katmanı yalnızca krater duvarlarını gösteriyor.",
                              "Rock map could not be downloaded; the craters-and-rocks layer shows crater walls only.");
        }
    }

    private async Task OnProfileChangedAsync()
    {
        if (_profile.SelectedIndex < 0) return;
        var profile = CurrentProfile;
        SetHint(L.T($"Görev: {profile.Name} — {profile.Description}   [{profile.WeightSummary}]",
                    $"Mission: {profile.Name} — {profile.Description}   [{profile.WeightSummary}]"));

        // Analiz edilen tüm noktalar yeni profille anında yeniden puanlanır (veri tekrar indirilmez)
        foreach (var ev in _history) SuitabilityScorer.Score(ev, profile);
        if (_lastEval != null && !_busy)
        {
            RenderReport(_lastEval);
            _state.Text = L.T($"Yeniden puanlandı ({profile.Name}): {_lastEval.OverallScore:F1}/100",
                              $"Re-scored ({profile.Name}): {_lastEval.OverallScore:F1}/100");
        }
        _globe.ClearMarkers();
        foreach (var ev in _history) _globe.AddMarker(ev.Site.LatitudeDeg, ev.Site.LongitudeEastDeg, ev.OverallScore);

        if (_global != null)
        {
            var g = _global;
            await Task.Run(() => g.ApplyProfile(profile));
            _globe.Redraw();
            if (_heatToggle.Checked)
                _state.Text = L.T($"Isı haritası güncellendi: yüzeyin %{g.PercentSuitable:F0}'i uygun ({profile.Name})",
                                  $"Heat map updated: {g.PercentSuitable:F0}% of the surface is suitable ({profile.Name})");
        }
    }

    // ============================== Nokta analizi ==============================
    private async Task EvaluateAsync(LandingSite site)
    {
        if (_busy) { _state.Text = L.T("Önceki işlem sürüyor, lütfen bekleyin...", "A previous task is still running, please wait..."); return; }
        _busy = true;
        _globe.SetPending(site.LatitudeDeg, site.LongitudeEastDeg);
        _progress.Style = ProgressBarStyle.Marquee;
        _progress.Visible = true;
        _report.Clear();
        Write(L.T("⏳ Analiz ediliyor...\n\n", "⏳ Analyzing...\n\n"), Color.Cyan, 13, true);
        Write($"{site.Name}\n\n", Color.White);
        Write(L.T("NASA'nın LRO arşivinden bu bölgenin yükseklik, kaya ve sıcaklık verileri indiriliyor. " +
                  "İlk seferde 20-60 saniye sürebilir; aynı bölge ikinci kez anında açılır.\n",
                  "Elevation, rock and temperature data for this area are being downloaded from NASA's LRO archive. " +
                  "The first time may take 20-60 seconds; the same area opens instantly the second time.\n"), Color.Silver, 9.5f);

        try
        {
            double radius = (double)_radius.Value;
            var profile = CurrentProfile;
            var ev = await Task.Run(() => _navigator.EvaluateAsync(site, radius, profile));
            // Analiz sürerken dil değiştiyse metinleri güncel dilde yeniden üret
            SuitabilityScorer.Score(ev, CurrentProfile);
            _lastEval = ev;
            _history.Add(ev);
            RenderReport(ev);
            _globe.AddMarker(site.LatitudeDeg, site.LongitudeEastDeg, ev.OverallScore);
            string path = await SaveJsonAsync(ev, ev.Site.Name);
            AppendLog(L.T($"Rapor kaydedildi: {path}", $"Report saved: {path}"));
            _state.Text = L.T($"Tamamlandı: {ev.OverallScore:F1}/100 — {ev.Verdict}", $"Done: {ev.OverallScore:F1}/100 — {ev.Verdict}");
            SetHint(_history.Count >= 2
                ? L.T($"{_history.Count} nokta analiz edildi. \"⚖ Karşılaştır\" ile yan yana görün, \"📄 Rapor\" ile tarayıcıda açın.",
                      $"{_history.Count} sites analyzed. Use \"⚖ Compare\" to view them side by side, \"📄 Report\" to open them in your browser.")
                : L.T("Başka noktalara da tıklayın; sonra \"⚖ Karşılaştır\" ile hangisinin daha iyi olduğunu görün.",
                      "Click other sites too; then use \"⚖ Compare\" to see which one is better."));
        }
        catch (Exception ex)
        {
            Write(L.T($"\n❌ Hata: {ex.Message}\n", $"\n❌ Error: {ex.Message}\n"), Color.OrangeRed, 10, true);
            _state.Text = L.T("Hata oluştu", "An error occurred");
        }
        finally
        {
            _progress.Visible = false;
            _globe.ClearPending();
            _busy = false;
        }
    }

    // ============================== Karşılaştırma ==============================
    private void ShowComparison()
    {
        if (_history.Count == 0)
        {
            MessageBox.Show(this,
                L.T("Henüz analiz edilmiş nokta yok.\nAy üzerinde birkaç noktaya tıklayın, sonra tekrar deneyin.",
                    "No sites have been analyzed yet.\nClick a few sites on the Moon, then try again."),
                L.T("Karşılaştırma", "Comparison"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var f = new Form
        {
            Text = L.T($"Nokta karşılaştırması — {_history.Count} nokta · Görev: {CurrentProfile.Name}",
                       $"Site comparison — {_history.Count} sites · Mission: {CurrentProfile.Name}"),
            ClientSize = new Size(1250, 520), StartPosition = FormStartPosition.CenterParent, BackColor = PanelColor
        };
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = PanelColor,
            BorderStyle = BorderStyle.None, EnableHeadersVisualStyles = false, GridColor = Color.FromArgb(45, 50, 70),
            Font = new Font("Segoe UI", 9.5f)
        };
        grid.ColumnHeadersDefaultCellStyle.BackColor = BarColor;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        grid.DefaultCellStyle.BackColor = PanelColor;
        grid.DefaultCellStyle.ForeColor = Color.Gainsboro;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(50, 70, 110);

        void AddText(string name, float weight) => grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, FillWeight = weight });
        void AddNum(string name) => grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = name, FillWeight = 55, ValueType = typeof(double),
            DefaultCellStyle = { Format = "F0", Alignment = DataGridViewContentAlignment.MiddleCenter }
        });

        AddText(L.T("Nokta", "Site"), 170); AddText(L.T("Koordinat", "Coordinates"), 140);
        AddNum(L.T("GENEL", "OVERALL")); AddText(L.T("Karar", "Verdict"), 120);
        foreach (var c in _history[0].Criteria) AddNum(c.Name);
        AddText(L.T("En yakın gerçek iniş", "Nearest real landing"), 140);
        int firstScoreCol = 2, lastScoreCol = 4 + _history[0].Criteria.Count - 1;

        foreach (var ev in _history)
        {
            var near = LandingCatalog.Nearest(ev.Site);
            var cells = new List<object?> { ev.Site.Name, Coord(ev.Site), Math.Round(ev.OverallScore, 1), ev.Verdict };
            cells.AddRange(ev.Criteria.Select(c => (object?)(c.Score.HasValue ? Math.Round(c.Score.Value, 0) : null)));
            cells.Add($"{near.Landing.Name}, {near.DistanceKm:F0} km");
            int row = grid.Rows.Add(cells.ToArray());
            grid.Rows[row].Tag = ev;
        }

        grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < firstScoreCol || e.ColumnIndex > lastScoreCol || e.ColumnIndex == 3) return;
            if (e.Value is double v)
            {
                e.CellStyle!.ForeColor = Color.Black;
                e.CellStyle.BackColor = ScoreColor(v);
            }
        };
        grid.Sort(grid.Columns[2], System.ComponentModel.ListSortDirection.Descending);
        grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || grid.Rows[e.RowIndex].Tag is not SiteEvaluation ev) return;
            _lastEval = ev;
            _globe.FlyTo(ev.Site.LatitudeDeg, ev.Site.LongitudeEastDeg, 6);
            RenderReport(ev);
            f.Close();
        };

        var top = new Label
        {
            Dock = DockStyle.Top, Height = 44, ForeColor = Color.Silver, Padding = new Padding(10, 6, 10, 0),
            Text = L.T("Tüm noktalar seçili görev profiline göre puanlandı. Renkler: yeşil çok uygun, açık yeşil uygun, sarı şartlı, kırmızı uygun değil.\n" +
                       "Sütun başlığına tıklayarak sıralayın · Bir satıra çift tıklayarak o noktaya gidin.",
                       "All sites are scored with the selected mission profile. Colors: green highly suitable, light green suitable, yellow conditional, red not suitable.\n" +
                       "Click a column header to sort · Double-click a row to go to that site.")
        };
        f.Controls.Add(grid);
        f.Controls.Add(top);
        f.ShowDialog(this);
    }

    // ============================== HTML rapor ==============================
    private void OpenHtmlReport()
    {
        if (_lastEval == null && _history.Count == 0)
        {
            MessageBox.Show(this, L.T("Önce Ay üzerinde bir noktaya tıklayıp analiz yapın.", "First click a site on the Moon to analyze it."),
                            L.T("Rapor", "Report"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            Directory.CreateDirectory(Config.ReportDirectory);
            string path = Path.GetFullPath(Path.Combine(Config.ReportDirectory,
                $"{L.T("rapor", "report")}_{DateTime.Now:yyyyMMdd_HHmmss}.html"));
            File.WriteAllText(path, HtmlReport.Build(_lastEval, _history, _global), Encoding.UTF8);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            AppendLog(L.T($"HTML rapor: {path}", $"HTML report: {path}"));
            _state.Text = L.T("Rapor tarayıcıda açıldı (yazdırmak/PDF için sayfadaki düğmeyi kullanın)",
                              "Report opened in your browser (use the button on the page to print / save as PDF)");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L.T($"Rapor açılamadı: {ex.Message}", $"Could not open the report: {ex.Message}"),
                            L.T("Rapor", "Report"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ============================== Rapor paneli ==============================
    private void Write(string text, Color color, float size = 10f, bool bold = false)
    {
        _report.SelectionStart = _report.TextLength;
        _report.SelectionLength = 0;
        _report.SelectionColor = color;
        _report.SelectionFont = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
        _report.AppendText(text);
    }

    private static Color ScoreColor(double s) =>
        s >= 80 ? Color.LimeGreen : s >= 65 ? Color.YellowGreen : s >= 50 ? Color.Gold : Color.OrangeRed;

    private static string Bar(double score)
    {
        int filled = (int)Math.Round(Math.Clamp(score, 0, 100) / 5);
        return new string('█', filled) + new string('░', 20 - filled);
    }

    private static string Coord(LandingSite s)
    {
        double lon = SolarAnalyzer.Norm180(s.LongitudeEastDeg);
        string ns = s.LatitudeDeg >= 0 ? L.T("K", "N") : L.T("G", "S");
        string ew = lon >= 0 ? L.T("D", "E") : L.T("B", "W");
        return $"{Math.Abs(s.LatitudeDeg):F4}° {ns},  {Math.Abs(lon):F4}° {ew}";
    }

    private void WriteWelcome()
    {
        _report.Clear();
        Write(L.T("Hoş geldiniz! 👋\n\n", "Welcome! 👋\n\n"), Color.White, 15, true);
        Write(L.T("Soldaki üç boyutlu Ay, NASA LRO uydusunun lazerle ölçtüğü gerçek yükseklik verisinden çizildi.\n\n",
                  "The 3D Moon on the left is drawn from real elevation data measured by the laser on NASA's LRO spacecraft.\n\n"),
              Color.Silver, 10);

        Write(L.T("Nasıl kullanılır?\n\n", "How to use\n\n"), Color.Cyan, 12, true);
        Write(L.T("①  Ay'ı döndürün\n", "①  Rotate the Moon\n"), Color.White, 11, true);
        Write(L.T("     Fareyle sürükleyin, tekerlekle yakınlaştırın.\n\n", "     Drag with the mouse, zoom with the wheel.\n\n"), Color.Gainsboro, 10);
        Write(L.T("②  Bir noktaya tıklayın\n", "②  Click a site\n"), Color.White, 11, true);
        Write(L.T("     Rapor burada çıkar: 0-100 puan, karar ve en yakın gerçek iniş noktası.\n\n",
                  "     The report appears here: 0-100 score, verdict and the nearest real landing site.\n\n"), Color.Gainsboro, 10);
        Write(L.T("③  🌡 Isı haritası\n", "③  🌡 Heat map\n"), Color.White, 11, true);
        Write(L.T("     Ay'ın tamamı uygunluğuna göre renklenir. Yanındaki listeden tek bir kriteri seçebilirsiniz.\n\n",
                  "     The whole Moon is colored by suitability. Use the list next to it to choose a single criterion.\n\n"), Color.Gainsboro, 10);
        Write(L.T("④  ⚖ Karşılaştır ve 📄 Rapor\n", "④  ⚖ Compare and 📄 Report\n"), Color.White, 11, true);
        Write(L.T("     Analiz ettiğiniz noktaları yan yana görün, raporu tarayıcıda açıp yazdırın.\n\n",
                  "     See analyzed sites side by side, open the report in your browser and print it.\n\n"), Color.Gainsboro, 10);

        Write(L.T("Renkler\n", "Colors\n"), Color.Cyan, 11, true);
        Write(L.T("   ● Yeşil   ", "   ● Green   "), Color.LimeGreen, 10, true); Write(L.T("çok uygun (80+)\n", "highly suitable (80+)\n"), Color.Gainsboro, 10);
        Write(L.T("   ● Açık yeşil   ", "   ● Light green   "), Color.YellowGreen, 10, true); Write(L.T("uygun (65-80)\n", "suitable (65-80)\n"), Color.Gainsboro, 10);
        Write(L.T("   ● Sarı   ", "   ● Yellow   "), Color.Gold, 10, true); Write(L.T("şartlı uygun (50-65)\n", "conditionally suitable (50-65)\n"), Color.Gainsboro, 10);
        Write(L.T("   ● Kırmızı   ", "   ● Red   "), Color.OrangeRed, 10, true); Write(L.T("uygun değil (50 altı)\n", "not suitable (below 50)\n"), Color.Gainsboro, 10);
        Write(L.T("   ▲ Altın / mavi üçgen   ", "   ▲ Gold / blue triangle   "), Color.Gold, 10, true);
        Write(L.T("insanlı / robotik gerçek iniş noktası\n\n", "crewed / robotic real landing site\n\n"), Color.Gainsboro, 10);

        Write(L.T("Not: Krater ve taş yoğunluğu puanı her zaman düşürür. Taze bir kraterin içi veya çok kayalık bir alan " +
                  "doğrudan \"UYGUN DEĞİL\" sonucunu verir.\n\n",
                  "Note: crater and rock density always lowers the score. The inside of a fresh crater or a very rocky area " +
                  "results directly in \"NOT SUITABLE\".\n\n"), Color.Silver, 9.5f);
        Write(L.T("Takıldığınızda yukarıdaki \"❓ Nasıl kullanılır?\" düğmesine basın. Dili sağ üstteki 🌐 düğmesiyle değiştirebilirsiniz.\n",
                  "If you get stuck, press \"❓ How to use\" above. Change the language with the 🌐 button at the top right.\n"),
              Color.LightSkyBlue, 9.5f);
    }

    private void RenderReport(SiteEvaluation ev)
    {
        _report.Clear();
        Write(L.T("İNİŞ NOKTASI RAPORU\n", "LANDING SITE REPORT\n"), Color.Cyan, 13, true);
        Write($"{ev.Site.Name}\n", Color.White, 10, true);
        Write($"{Coord(ev.Site)}\n", Color.White, 10.5f);
        Write(L.T($"Görev profili: {ev.ProfileName}\n", $"Mission profile: {ev.ProfileName}\n"), Color.LightSkyBlue, 9.5f);
        var near = LandingCatalog.Nearest(ev.Site);
        Write(L.T($"En yakın gerçek iniş: {near.Landing.Name} ({near.Landing.Date:yyyy}, {near.Landing.Country}) — {near.DistanceKm:F0} km\n\n",
                  $"Nearest real landing: {near.Landing.Name} ({near.Landing.Date:yyyy}, {near.Landing.Country}) — {near.DistanceKm:F0} km\n\n"),
              Color.Silver, 9.5f);

        Write(L.T("GENEL PUAN:  ", "OVERALL SCORE:  "), Color.White, 14, true);
        Write($"{ev.OverallScore:F1} / 100\n", ScoreColor(ev.OverallScore), 18, true);
        Write($"{ev.Verdict}\n", ev.HardLimitViolations.Count > 0 ? Color.OrangeRed : ScoreColor(ev.OverallScore), 13, true);
        foreach (var v in ev.HardLimitViolations) Write($"✖ {v}\n", Color.OrangeRed, 10, true);
        foreach (var w in ev.Warnings) Write($"⚠ {w}\n", Color.Gold, 9.5f);
        Write("\n", Color.White);

        foreach (var c in ev.Criteria)
        {
            Write($"{c.Name.ToUpper()}", Color.White, 10.5f, true);
            Write(L.T($"   (ağırlık %{c.Weight:F0})\n", $"   (weight {c.Weight:F0}%)\n"), Color.Gray, 9);
            if (c.Score.HasValue)
            {
                Write($"{Bar(c.Score.Value)} ", ScoreColor(c.Score.Value), 10);
                Write($"{c.Score.Value:F0}/100\n", ScoreColor(c.Score.Value), 10.5f, true);
            }
            else Write(L.T("veri alınamadı — genel puana katılmadı\n", "no data — not included in the overall score\n"), Color.Goldenrod, 9.5f);
            Write($"{c.Summary}\n", Color.Gainsboro, 9.5f);
            foreach (var d in c.Details) Write($"   – {d}\n", Color.DarkGray, 9f);
            Write("\n", Color.White, 6);
        }

        if (ev.Sun != null)
        {
            Write(L.T("İNİŞ ZAMANLAMASI\n", "LANDING TIMING\n"), Color.White, 10.5f, true);
            string dayNight = ev.Sun.CurrentlyDaylight ? L.T("GÜNDÜZ ☀", "DAY ☀") : L.T("GECE ☾", "NIGHT ☾");
            Write(L.T($"Şu an noktada: {dayNight} (güneş yüksekliği {ev.Sun.CurrentSunElevationDeg:F1}°)\n",
                      $"Right now at the site: {dayNight} (Sun elevation {ev.Sun.CurrentSunElevationDeg:F1}°)\n"), Color.Gainsboro, 9.5f);
            if (ev.Sun.LandingWindowFound)
                Write(L.T("Önerilen iniş penceresi (sabah, güneş 7°–20°):\n", "Recommended landing window (morning, Sun 7°–20°):\n") +
                      $"   {ev.Sun.NextLandingWindowStartUtc.ToLocalTime():dd.MM.yyyy HH:mm}  →  {ev.Sun.NextLandingWindowEndUtc.ToLocalTime():dd.MM.yyyy HH:mm}\n",
                      Color.LightSkyBlue, 9.5f, true);
            else
                Write(L.T("Güneş bu enlemde iniş için yeterince (7°) yükselmiyor.\n", "At this latitude the Sun does not rise high enough (7°) for landing.\n"),
                      Color.Gold, 9.5f);
            Write(L.T("   – Alçak güneşte kayalar ve kraterler uzun gölge verir, kolayca görülür (Apollo inişleri ~10°).\n",
                      "   – A low Sun makes rocks and craters cast long shadows, making them easy to see (Apollo landings ~10°).\n"), Color.DarkGray, 9f);
            Write(L.T("   – Tarih basitleştirilmiş sinodik ay modeliyle hesaplandı (±12 saat).\n\n",
                      "   – Date computed with a simplified synodic-month model (±12 hours).\n\n"), Color.DarkGray, 9f);
        }

        Write(L.T("KULLANILAN VERİLER\n", "DATA USED\n"), Color.White, 10, true);
        foreach (var d in ev.DataSources.Distinct()) Write($"• {d}\n", Color.DarkGray, 8.5f);

        _report.SelectionStart = 0;
        _report.ScrollToCaret();
    }

    private static async Task<string> SaveJsonAsync(object data, string name)
    {
        Directory.CreateDirectory(Config.ReportDirectory);
        string safeName = string.Concat(name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
        string path = Path.Combine(Config.ReportDirectory, $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(data, data.GetType(), options));
        return Path.GetFullPath(path);
    }
}

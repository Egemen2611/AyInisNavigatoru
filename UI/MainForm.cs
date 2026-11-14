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
///   ÜSTTE  : tek sıra araç çubuğu + altında seçili seçeneğin açıklaması
///   SOLDA  : 3B Ay küresi (ısı haritası açıkken Ay'ın tamamı uygunluğa göre renklenir)
///   SAĞDA  : yalnızca rapor (tek parça)
///   ALTTA  : durum çubuğu (imleç konumu, ilerleme, günlük)
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
    private readonly ComboBox _profile = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ComboBox _goto = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210, DropDownHeight = 420 };
    private readonly CheckBox _heatToggle = ToggleButton("🌡  Isı haritası");
    private readonly ComboBox _layer = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly CheckBox _landingsToggle = ToggleButton("🛬 Tarihî inişler", true);
    private readonly CheckBox _realSun = ToggleButton("☀ Gerçek güneş");
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
    private readonly ToolStripStatusLabel _state = new() { Text = "Hazır" };
    private readonly StringBuilder _logText = new();
    private readonly ToolTip _tips = new() { AutoPopDelay = 20000, InitialDelay = 300 };

    // Durum
    private bool _busy, _globalBusy;
    private SiteEvaluation? _lastEval;
    private readonly List<SiteEvaluation> _history = new();
    private MoonMapData? _mapData;
    private GlobalSuitabilityMap? _global;

    private static readonly List<LandingSite> GotoSites = BuildGotoSites();

    private static List<LandingSite> BuildGotoSites()
    {
        var list = LandingCatalog.All.Select(l => l.ToSite()).ToList();
        list.Add(new LandingSite("Zorlu: Tycho Krateri merkezi", -43.31, -11.36));
        list.Add(new LandingSite("Zorlu: Copernicus Krateri kenarı", 9.62, -18.70));
        return list;
    }

    private static readonly (HeatLayerKind Layer, string Desc)[] Layers =
    {
        (HeatLayerKind.Overall, "Genel uygunluk: seçili göreve göre toplam puan. Koyu kırmızı = kritik sınır aşıldı (çok dik, çok kayalık ya da Dünya görünmüyor)."),
        (HeatLayerKind.Slope, "Yüzey eğimi: kırmızı yerler krater duvarları, dağ yamaçları gibi dik yüzeylerdir (LOLA, ~2 km ölçeği)."),
        (HeatLayerKind.Hazard, "Krater ve taş: Diviner kaya yoğunluğu + krater duvarları. Kırmızı = kayalık veya kraterli. Bu kriter puanı yalnızca düşürür."),
        (HeatLayerKind.Sun, "Güneş ışığı: ekvatora yakın yerlerde öğlen güneşi daha yüksektir → daha fazla güneş enerjisi."),
        (HeatLayerKind.Thermal, "Sıcaklık: gündüz en yüksek ve gece en düşük sıcaklığın elektronik cihazlar için ne kadar zorlayıcı olduğu."),
        (HeatLayerKind.Comm, "Dünya ile iletişim: yakın yüzün ortası yeşil; kenarlarda Dünya ufka yaklaşır, uzak yüzde hiç görünmez (kırmızı)."),
        (HeatLayerKind.Terrain, "Arazi yapısı: pürüzlülük ve yükseklik farkı. Düz lav denizleri (mare) yeşil, engebeli yaylalar kırmızıdır."),
    };

    private const string DefaultHint =
        "① Ay'ı sürükleyerek döndürün   ② Bir noktaya tıklayın → rapor sağda çıkar   " +
        "③ \"🌡 Isı haritası\" ile Ay'ın tamamının uygunluğunu görün   ④ \"⚖ Karşılaştır\" ile noktaları kıyaslayın";

    public MainForm()
    {
        Text = "Ay İniş Navigatörü 3.0 — NASA LRO verileriyle iniş uygunluk analizi (Space Apps Eskişehir)";
        Width = 1600; Height = 960;
        MinimumSize = new Size(1100, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(8, 10, 20);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 9.5f);

        BuildLayout();
        BuildSettingsForm();
        WireEvents();
        WriteWelcome();
    }

    // ============================== Yerleşim ==============================
    private static CheckBox ToggleButton(string text, bool isChecked = false) => new()
    {
        Text = text, Appearance = Appearance.Button, AutoSize = true, FlatStyle = FlatStyle.Flat, Checked = isChecked,
        ForeColor = Color.White, BackColor = Color.FromArgb(45, 50, 70), Margin = new Padding(4, 3, 4, 3),
        Padding = new Padding(6, 2, 6, 2), Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Cursor = Cursors.Hand
    };

    private static Button SmallButton(string text) => new()
    {
        Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = Color.White,
        BackColor = Color.FromArgb(45, 50, 70), Margin = new Padding(4, 4, 4, 3), Cursor = Cursors.Hand
    };

    private static Label Caption(string text) => new()
    {
        Text = text, AutoSize = true, ForeColor = Color.LightGray, Margin = new Padding(10, 9, 2, 0),
        Font = new Font("Segoe UI", 9f, FontStyle.Bold)
    };

    private static Panel Separator() => new() { Width = 1, Height = 28, BackColor = Color.FromArgb(70, 75, 100), Margin = new Padding(8, 5, 8, 0) };

    private static void StyleToggle(CheckBox cb, Color on) =>
        cb.CheckedChanged += (_, _) => cb.BackColor = cb.Checked ? on : Color.FromArgb(45, 50, 70);

    private void BuildLayout()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
            BackColor = BarColor, Padding = new Padding(6, 4, 6, 4)
        };

        foreach (var p in MissionProfile.All) _profile.Items.Add(p);
        _profile.SelectedIndex = 0;
        _goto.Items.Add("— bir iniş noktasına git —");
        _goto.Items.AddRange(GotoSites.Select(s => (object)s.Name).ToArray());
        _goto.SelectedIndex = 0;
        foreach (var l in Layers) _layer.Items.Add(MoonGlobeControl.LayerName(l.Layer));
        _layer.SelectedIndex = 0;
        _layer.Enabled = false;

        StyleToggle(_heatToggle, Color.FromArgb(160, 70, 30));
        StyleToggle(_landingsToggle, Color.FromArgb(40, 90, 140));
        StyleToggle(_realSun, Color.FromArgb(150, 120, 30));
        _landingsToggle.BackColor = Color.FromArgb(40, 90, 140);

        var compare = SmallButton("⚖ Karşılaştır");
        compare.Click += (_, _) => ShowComparison();
        var html = SmallButton("📄 Rapor (tarayıcı)");
        html.Click += (_, _) => OpenHtmlReport();
        var settings = SmallButton("⚙ Ayarlar");
        settings.Click += (_, _) => _settingsForm?.ShowDialog(this);
        var help = SmallButton("❓ Nasıl kullanılır?");
        help.Click += (_, _) => ShowHelp();
        var reset = SmallButton("⟲ Sıfırla");
        reset.Click += (_, _) =>
        {
            _globe.ResetView();
            _globe.ClearMarkers();
            _history.Clear();
            _heatToggle.Checked = false;
            SetHint(DefaultHint);
        };

        bar.Controls.AddRange(new Control[]
        {
            Caption("Görev:"), _profile,
            Caption("Git:"), _goto,
            Separator(),
            _heatToggle, _layer,
            Separator(),
            _landingsToggle, _realSun,
            Separator(),
            compare, html, settings, help, reset
        });

        var hintStrip = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.FromArgb(14, 16, 26) };
        hintStrip.Controls.Add(_hint);
        SetHint(DefaultHint);

        var right = new Panel { Dock = DockStyle.Right, Width = 560, BackColor = PanelColor, Padding = new Padding(14, 10, 8, 10) };
        right.Controls.Add(_report);
        var divider = new Panel { Dock = DockStyle.Right, Width = 2, BackColor = Color.FromArgb(40, 45, 70) };

        var status = new StatusStrip { BackColor = BarColor, ForeColor = Color.Gainsboro };
        var logButton = new ToolStripButton("📜 Ayrıntılı günlük") { ForeColor = Color.LightSkyBlue };
        logButton.Click += (_, _) => ShowLog();
        status.Items.Add(_hover);
        status.Items.Add(_progress);
        status.Items.Add(_state);
        status.Items.Add(logButton);

        // Dock sırası: önce Fill, sonra kenarlar
        Controls.Add(_globe);
        Controls.Add(divider);
        Controls.Add(right);
        Controls.Add(hintStrip);
        Controls.Add(bar);
        Controls.Add(status);

        _tips.SetToolTip(_profile, "Görev türüne göre kriterlerin önemi ve kritik sınırlar değişir.");
        _tips.SetToolTip(_goto, "Ay'a yapılmış gerçek inişler (NASA NSSDC koordinatları) ve zorlu örnekler. Seçince oraya gidip analiz eder.");
        _tips.SetToolTip(_heatToggle, "Ay'ın TAMAMINI uygunluğuna göre renklendirir (yeşil uygun, kırmızı uygun değil).");
        _tips.SetToolTip(_layer, "Isı haritasında hangi kriterin gösterileceği");
        _tips.SetToolTip(_landingsToggle, "Apollo, Luna, Chang'e, Chandrayaan-3, SLIM... tüm başarılı inişleri gösterir.");
        _tips.SetToolTip(_realSun, "Gece-gündüz çizgisini bugünün tarihine göre gösterir.");
        _tips.SetToolTip(compare, "Analiz ettiğiniz tüm noktaları tek tabloda yan yana karşılaştırır.");
        _tips.SetToolTip(html, "Raporu tarayıcıda açar; buradan yazdırabilir veya PDF olarak kaydedebilirsiniz.");
        _tips.SetToolTip(settings, "İniş elipsi yarıçapı");
    }

    private void BuildSettingsForm()
    {
        var f = new Form
        {
            Text = "Ayarlar", FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(560, 250),
            BackColor = PanelColor, ForeColor = Color.Gainsboro, Font = new Font("Segoe UI", 9.5f)
        };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16) };
        flow.Controls.Add(new Label { Text = "İniş elipsi yarıçapı (metre)", AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) });
        flow.Controls.Add(_radius);
        flow.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(510, 0), ForeColor = Color.DarkGray, Margin = new Padding(0, 6, 0, 6),
            Text = "İniş aracı hedefe tam olarak inemez; bu yarıçap içindeki herhangi bir yere inebilir. " +
                   "Eğim, kaya ve pürüzlülük bu dairenin tamamında hesaplanır.\n" +
                   "250–500 m: modern hassas iniş (ör. SLIM) · 1–2 km: günümüz robotik araçları · 5 km+: Apollo dönemi"
        });
        var ok = new Button { Text = "Tamam", AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = AccentColor, ForeColor = Color.White, DialogResult = DialogResult.OK, Margin = new Padding(0, 10, 0, 0) };
        flow.Controls.Add(ok);
        f.AcceptButton = ok;
        f.Controls.Add(flow);
        _settingsForm = f;
    }

    private void ShowHelp()
    {
        const string text =
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
            "KRİTERLER\r\n" +
            "   • Yüzey eğimi (LRO LOLA): dik yüzeyde iniş aracı devrilir.\r\n" +
            "   • Krater ve taş yoğunluğu (LRO Diviner + LOLA): her zaman puanı DÜŞÜRÜR.\r\n" +
            "   • Güneş ışığı: güneş panelleri için güneş yüksekliği ve tepelerin gölgesi.\r\n" +
            "   • Sıcaklık: gündüz ışınım dengesi modeli, gece Diviner ölçümü.\r\n" +
            "   • Dünya ile iletişim: Dünya ufkun üzerinde mi? (librasyon dahil)\r\n" +
            "   • Yükseklik ve arazi: pürüzlülük ve yükseklik farkı.\r\n\r\n" +
            "İPUCU: İlk analizde veri indirildiği için 20-60 saniye sürebilir. Aynı bölge ikinci kez anında açılır.";
        using var f = new Form
        {
            Text = "Nasıl kullanılır?", ClientSize = new Size(760, 640), StartPosition = FormStartPosition.CenterParent,
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
            Text = "Ayrıntılı günlük (veri indirme ve hesaplama adımları)", ClientSize = new Size(820, 480),
            StartPosition = FormStartPosition.CenterParent, BackColor = PanelColor
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
            await EvaluateAsync(new LandingSite($"Seçilen nokta ({lat:F3}, {lon:F3})", lat, lon));
        _navigator.LogMessage += AppendLog;

        _profile.SelectedIndexChanged += async (_, _) => await OnProfileChangedAsync();

        _goto.SelectedIndexChanged += async (_, _) =>
        {
            if (_goto.SelectedIndex <= 0) return;
            var site = GotoSites[_goto.SelectedIndex - 1];
            _globe.FlyTo(site.LatitudeDeg, site.LongitudeEastDeg, 6);
            await EvaluateAsync(site);
        };

        _heatToggle.CheckedChanged += async (_, _) => await OnHeatToggledAsync();
        _layer.SelectedIndexChanged += (_, _) =>
        {
            SetHint(Layers[_layer.SelectedIndex].Desc);
            if (_heatToggle.Checked && _global != null) _globe.SetGlobalHeatmap(_global, Layers[_layer.SelectedIndex].Layer);
        };
        _landingsToggle.CheckedChanged += (_, _) =>
        {
            _globe.SetShowLandings(_landingsToggle.Checked);
            if (_landingsToggle.Checked)
                SetHint($"Tarihî inişler: {LandingCatalog.All.Length} başarılı iniş (altın = insanlı Apollo, mavi = robotik). " +
                        "Koordinatlar NASA NSSDC'den. Etiketler yakınlaştırınca görünür.");
        };
        _realSun.CheckedChanged += (_, _) =>
        {
            _globe.SetRealSunLighting(_realSun.Checked, SolarAnalyzer.SubsolarLongitude(DateTime.UtcNow));
            SetHint(_realSun.Checked
                ? "Gerçek güneş: Ay şu an nasıl aydınlanıyorsa öyle görünür; gece-gündüz çizgisi gerçektir. Karanlık taraf da seçilebilir."
                : "Sabit ışık: yüzey şekilleri (krater, tepe) her yerde net görünsün diye soldan-yukarıdan aydınlatılır.");
        };

        Load += async (_, _) => await LoadMapAsync();
        FormClosed += (_, _) => _navigator.Dispose();
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
        _state.Text = "Ay haritası yükleniyor...";
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
                AppendLog($"3B küre hazır: LOLA {product} ({mapData.Width}×{mapData.Height})");
                // Isı haritası kaba haritayla oluşturulduysa ayrıntılı haritayla yenile
                if (_global != null && _global.DemProduct != product)
                {
                    _global = null;
                    if (_heatToggle.Checked) await OnHeatToggledAsync();
                }
            }
        }
        _state.Text = "Hazır — Ay'ı döndürün ve bir noktaya tıklayın";
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
            if (_mapData == null) { _state.Text = "Ay haritası henüz yüklenmedi, birkaç saniye bekleyin."; _heatToggle.Checked = false; return; }
            if (_globalBusy) return;
            _globalBusy = true;
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.Visible = true;
            _state.Text = "Ay'ın tamamı puanlanıyor (1 milyondan fazla hücre)...";
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
                AppendLog($"Küresel uygunluk haritası hazır ({md.Product}): ortalama {g.MeanOverall:F1}, uygun yüzey %{g.PercentSuitable:F1}");
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
            _globe.SetGlobalHeatmap(_global, Layers[_layer.SelectedIndex].Layer);
            SetHint(Layers[_layer.SelectedIndex].Desc);
            _state.Text = $"Isı haritası: Ay yüzeyinin %{_global.PercentSuitable:F0}'i bu görev için uygun (puan ≥ 65)";
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
            _state.Text = $"Diviner kaya haritası ekleniyor... %{f * 100:F0}";
        });
        bool ok = await Task.Run(() => _navigator.LoadGlobalRocksAsync(map, progress));
        _progress.Visible = false;
        if (ok && ReferenceEquals(map, _global))
        {
            var profile = CurrentProfile;
            await Task.Run(() => map.ApplyProfile(profile));
            _globe.Redraw();
            _state.Text = $"Kaya verisi eklendi — Ay yüzeyinin %{map.PercentSuitable:F0}'i bu görev için uygun";
        }
        else if (!ok)
        {
            _state.Text = "Kaya haritası indirilemedi; krater ve taş katmanı yalnızca krater duvarlarını gösteriyor.";
        }
    }

    private async Task OnProfileChangedAsync()
    {
        var profile = CurrentProfile;
        SetHint($"Görev: {profile.Name} — {profile.Description}   [{profile.WeightSummary}]");

        // Analiz edilen tüm noktalar yeni profille anında yeniden puanlanır (veri tekrar indirilmez)
        foreach (var ev in _history) SuitabilityScorer.Score(ev, profile);
        if (_lastEval != null && !_busy)
        {
            RenderReport(_lastEval);
            _state.Text = $"Yeniden puanlandı ({profile.Name}): {_lastEval.OverallScore:F1}/100";
        }
        _globe.ClearMarkers();
        foreach (var ev in _history) _globe.AddMarker(ev.Site.LatitudeDeg, ev.Site.LongitudeEastDeg, ev.OverallScore);

        if (_global != null)
        {
            var g = _global;
            await Task.Run(() => g.ApplyProfile(profile));
            _globe.Redraw();
            if (_heatToggle.Checked) _state.Text = $"Isı haritası güncellendi: yüzeyin %{g.PercentSuitable:F0}'i uygun ({profile.Name})";
        }
    }

    // ============================== Nokta analizi ==============================
    private async Task EvaluateAsync(LandingSite site)
    {
        if (_busy) { _state.Text = "Önceki işlem sürüyor, lütfen bekleyin..."; return; }
        _busy = true;
        _globe.SetPending(site.LatitudeDeg, site.LongitudeEastDeg);
        _progress.Style = ProgressBarStyle.Marquee;
        _progress.Visible = true;
        _report.Clear();
        Write("⏳ Analiz ediliyor...\n\n", Color.Cyan, 13, true);
        Write($"{site.Name}\n\n", Color.White);
        Write("NASA'nın LRO arşivinden bu bölgenin yükseklik, kaya ve sıcaklık verileri indiriliyor. " +
              "İlk seferde 20-60 saniye sürebilir; aynı bölge ikinci kez anında açılır.\n", Color.Silver, 9.5f);

        try
        {
            double radius = (double)_radius.Value;
            var profile = CurrentProfile;
            var ev = await Task.Run(() => _navigator.EvaluateAsync(site, radius, profile));
            _lastEval = ev;
            _history.Add(ev);
            RenderReport(ev);
            _globe.AddMarker(site.LatitudeDeg, site.LongitudeEastDeg, ev.OverallScore);
            string path = await SaveJsonAsync(ev, ev.Site.Name);
            AppendLog($"Rapor kaydedildi: {path}");
            _state.Text = $"Tamamlandı: {ev.OverallScore:F1}/100 — {ev.Verdict}";
            SetHint(_history.Count >= 2
                ? $"{_history.Count} nokta analiz edildi. \"⚖ Karşılaştır\" ile yan yana görün, \"📄 Rapor\" ile tarayıcıda açın."
                : "Başka noktalara da tıklayın; sonra \"⚖ Karşılaştır\" ile hangisinin daha iyi olduğunu görün.");
        }
        catch (Exception ex)
        {
            Write($"\n❌ Hata: {ex.Message}\n", Color.OrangeRed, 10, true);
            _state.Text = "Hata oluştu";
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
            MessageBox.Show(this, "Henüz analiz edilmiş nokta yok.\nAy üzerinde birkaç noktaya tıklayın, sonra tekrar deneyin.",
                            "Karşılaştırma", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var f = new Form
        {
            Text = $"Nokta karşılaştırması — {_history.Count} nokta · Görev: {CurrentProfile.Name}",
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

        AddText("Nokta", 170); AddText("Koordinat", 140); AddNum("GENEL"); AddText("Karar", 120);
        foreach (var c in _history[0].Criteria) AddNum(c.Name);
        AddText("En yakın gerçek iniş", 140);
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
            Text = "Tüm noktalar seçili görev profiline göre puanlandı. Renkler: yeşil çok uygun, açık yeşil uygun, sarı şartlı, kırmızı uygun değil.\n" +
                   "Sütun başlığına tıklayarak sıralayın · Bir satıra çift tıklayarak o noktaya gidin."
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
            MessageBox.Show(this, "Önce Ay üzerinde bir noktaya tıklayıp analiz yapın.", "Rapor",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            Directory.CreateDirectory(Config.ReportDirectory);
            string path = Path.GetFullPath(Path.Combine(Config.ReportDirectory, $"rapor_{DateTime.Now:yyyyMMdd_HHmmss}.html"));
            File.WriteAllText(path, HtmlReport.Build(_lastEval, _history, _global), Encoding.UTF8);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            AppendLog($"HTML rapor: {path}");
            _state.Text = "Rapor tarayıcıda açıldı (yazdırmak/PDF için sayfadaki düğmeyi kullanın)";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Rapor açılamadı: {ex.Message}", "Rapor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        return $"{Math.Abs(s.LatitudeDeg):F4}° {(s.LatitudeDeg >= 0 ? "K" : "G")},  {Math.Abs(lon):F4}° {(lon >= 0 ? "D" : "B")}";
    }

    private void WriteWelcome()
    {
        Write("Hoş geldiniz! 👋\n\n", Color.White, 15, true);
        Write("Soldaki üç boyutlu Ay, NASA LRO uydusunun lazerle ölçtüğü gerçek yükseklik verisinden çizildi.\n\n", Color.Silver, 10);

        Write("Nasıl kullanılır?\n\n", Color.Cyan, 12, true);
        Write("①  Ay'ı döndürün\n", Color.White, 11, true);
        Write("     Fareyle sürükleyin, tekerlekle yakınlaştırın.\n\n", Color.Gainsboro, 10);
        Write("②  Bir noktaya tıklayın\n", Color.White, 11, true);
        Write("     Rapor burada çıkar: 0-100 puan, karar ve en yakın gerçek iniş noktası.\n\n", Color.Gainsboro, 10);
        Write("③  🌡 Isı haritası\n", Color.White, 11, true);
        Write("     Ay'ın tamamı uygunluğuna göre renklenir. Yanındaki listeden tek bir kriteri seçebilirsiniz.\n\n", Color.Gainsboro, 10);
        Write("④  ⚖ Karşılaştır ve 📄 Rapor\n", Color.White, 11, true);
        Write("     Analiz ettiğiniz noktaları yan yana görün, raporu tarayıcıda açıp yazdırın.\n\n", Color.Gainsboro, 10);

        Write("Renkler\n", Color.Cyan, 11, true);
        Write("   ● Yeşil   ", Color.LimeGreen, 10, true); Write("çok uygun (80+)\n", Color.Gainsboro, 10);
        Write("   ● Açık yeşil   ", Color.YellowGreen, 10, true); Write("uygun (65-80)\n", Color.Gainsboro, 10);
        Write("   ● Sarı   ", Color.Gold, 10, true); Write("şartlı uygun (50-65)\n", Color.Gainsboro, 10);
        Write("   ● Kırmızı   ", Color.OrangeRed, 10, true); Write("uygun değil (50 altı)\n", Color.Gainsboro, 10);
        Write("   ▲ Altın / mavi üçgen   ", Color.Gold, 10, true); Write("insanlı / robotik gerçek iniş noktası\n\n", Color.Gainsboro, 10);

        Write("Not: Krater ve taş yoğunluğu puanı her zaman düşürür. Taze bir kraterin içi veya çok kayalık bir alan " +
              "doğrudan \"UYGUN DEĞİL\" sonucunu verir.\n\n", Color.Silver, 9.5f);
        Write("Takıldığınızda yukarıdaki \"❓ Nasıl kullanılır?\" düğmesine basın.\n", Color.LightSkyBlue, 9.5f);
    }

    private void RenderReport(SiteEvaluation ev)
    {
        _report.Clear();
        Write("İNİŞ NOKTASI RAPORU\n", Color.Cyan, 13, true);
        Write($"{ev.Site.Name}\n", Color.White, 10, true);
        Write($"{Coord(ev.Site)}\n", Color.White, 10.5f);
        Write($"Görev profili: {ev.ProfileName}\n", Color.LightSkyBlue, 9.5f);
        var near = LandingCatalog.Nearest(ev.Site);
        Write($"En yakın gerçek iniş: {near.Landing.Name} ({near.Landing.Date:yyyy}, {near.Landing.Country}) — {near.DistanceKm:F0} km\n\n",
              Color.Silver, 9.5f);

        Write("GENEL PUAN:  ", Color.White, 14, true);
        Write($"{ev.OverallScore:F1} / 100\n", ScoreColor(ev.OverallScore), 18, true);
        Write($"{ev.Verdict}\n", ev.HardLimitViolations.Count > 0 ? Color.OrangeRed : ScoreColor(ev.OverallScore), 13, true);
        foreach (var v in ev.HardLimitViolations) Write($"✖ {v}\n", Color.OrangeRed, 10, true);
        foreach (var w in ev.Warnings) Write($"⚠ {w}\n", Color.Gold, 9.5f);
        Write("\n", Color.White);

        foreach (var c in ev.Criteria)
        {
            Write($"{c.Name.ToUpper()}", Color.White, 10.5f, true);
            Write($"   (ağırlık %{c.Weight:F0})\n", Color.Gray, 9);
            if (c.Score.HasValue)
            {
                Write($"{Bar(c.Score.Value)} ", ScoreColor(c.Score.Value), 10);
                Write($"{c.Score.Value:F0}/100\n", ScoreColor(c.Score.Value), 10.5f, true);
            }
            else Write("veri alınamadı — genel puana katılmadı\n", Color.Goldenrod, 9.5f);
            Write($"{c.Summary}\n", Color.Gainsboro, 9.5f);
            foreach (var d in c.Details) Write($"   – {d}\n", Color.DarkGray, 9f);
            Write("\n", Color.White, 6);
        }

        if (ev.Sun != null)
        {
            Write("İNİŞ ZAMANLAMASI\n", Color.White, 10.5f, true);
            Write($"Şu an noktada: {(ev.Sun.CurrentlyDaylight ? "GÜNDÜZ ☀" : "GECE ☾")} (güneş yüksekliği {ev.Sun.CurrentSunElevationDeg:F1}°)\n",
                  Color.Gainsboro, 9.5f);
            if (ev.Sun.LandingWindowFound)
                Write($"Önerilen iniş penceresi (sabah, güneş 7°–20°):\n   {ev.Sun.NextLandingWindowStartUtc.ToLocalTime():dd.MM.yyyy HH:mm}  →  " +
                      $"{ev.Sun.NextLandingWindowEndUtc.ToLocalTime():dd.MM.yyyy HH:mm}\n", Color.LightSkyBlue, 9.5f, true);
            else
                Write("Güneş bu enlemde iniş için yeterince (7°) yükselmiyor.\n", Color.Gold, 9.5f);
            Write("   – Alçak güneşte kayalar ve kraterler uzun gölge verir, kolayca görülür (Apollo inişleri ~10°).\n", Color.DarkGray, 9f);
            Write("   – Tarih basitleştirilmiş sinodik ay modeliyle hesaplandı (±12 saat).\n\n", Color.DarkGray, 9f);
        }

        Write("KULLANILAN VERİLER\n", Color.White, 10, true);
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

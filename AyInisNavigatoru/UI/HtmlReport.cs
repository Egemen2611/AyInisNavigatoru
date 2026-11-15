using System.Globalization;
using System.Net;
using System.Text;
using AyInisNavigatoru.Analysis;

namespace AyInisNavigatoru.UI;

/// <summary>
/// Analiz sonuçlarını tarayıcıda açılan, yazdırılabilir (PDF'e kaydedilebilir) bir HTML rapora dönüştürür.
/// Rapor, uygulamanın seçili dilinde (Türkçe / İngilizce) üretilir.
/// </summary>
public static class HtmlReport
{
    private static string E(string s) => WebUtility.HtmlEncode(s);
    private static string F(double v, string fmt = "F1") => double.IsNaN(v) ? "—" : v.ToString(fmt, CultureInfo.InvariantCulture);

    private static string ColorOf(double s) =>
        double.IsNaN(s) ? "#666" : s >= 80 ? "#1a9850" : s >= 65 ? "#91cf60" : s >= 50 ? "#fdae61" : "#d73027";

    private static string Coord(LandingSite s)
    {
        double lon = SolarAnalyzer.Norm180(s.LongitudeEastDeg);
        string ns = s.LatitudeDeg >= 0 ? L.T("K", "N") : L.T("G", "S");
        string ew = lon >= 0 ? L.T("D", "E") : L.T("B", "W");
        return $"{Math.Abs(s.LatitudeDeg):F4}° {ns}, {Math.Abs(lon):F4}° {ew}";
    }

    public static string Build(SiteEvaluation? main, IReadOnlyList<SiteEvaluation> all, GlobalSuitabilityMap? global)
    {
        var sb = new StringBuilder();
        sb.Append($"<!DOCTYPE html><html lang=\"{(L.En ? "en" : "tr")}\"><head><meta charset=\"utf-8\">");
        sb.Append($"<title>{E(L.T("Ay İniş Navigatörü – Rapor", "Lunar Landing Navigator – Report"))}</title>");
        sb.Append("""
<style>
 body{font-family:Segoe UI,Arial,sans-serif;background:#0d1020;color:#e6e6ee;margin:0;padding:32px 48px;line-height:1.5}
 h1{margin:0 0 4px;font-size:28px} h2{margin-top:36px;border-bottom:1px solid #334;padding-bottom:6px}
 .muted{color:#9aa0b4} .card{background:#161a2e;border-radius:12px;padding:18px 22px;margin:14px 0}
 .score{font-size:44px;font-weight:700} .verdict{font-size:20px;font-weight:700}
 .bar{height:12px;border-radius:6px;background:#2a2f48;overflow:hidden;width:260px;display:inline-block;vertical-align:middle}
 .bar>span{display:block;height:100%}
 table{border-collapse:collapse;width:100%;font-size:14px} th,td{padding:7px 10px;border-bottom:1px solid #2a2f48;text-align:left}
 th{background:#1c2140} .pill{padding:2px 8px;border-radius:10px;color:#111;font-weight:700}
 .bad{color:#ff7b6b;font-weight:700} .warn{color:#ffd166}
 ul{margin:4px 0 0 18px;padding:0} li{margin:2px 0;color:#b8bccc}
 button{background:#2d5faa;color:#fff;border:0;border-radius:8px;padding:8px 16px;font-size:14px;cursor:pointer}
 @media print{body{background:#fff;color:#000}.card{background:#f3f4f8}th{background:#e6e8f0}li,.muted{color:#333}button{display:none}}
</style></head><body>
""");
        sb.Append($"<h1>🌕 {E(L.T("Ay İniş Navigatörü – İniş Uygunluk Raporu", "Lunar Landing Navigator – Landing Suitability Report"))}</h1>");
        sb.Append($"<div class='muted'>NASA Space Apps Challenge – Eskişehir · {E(L.T("Oluşturulma", "Generated"))}: {DateTime.Now:dd.MM.yyyy HH:mm} · " +
                  $"{E(L.T("Veriler", "Data"))}: NASA Lunar Reconnaissance Orbiter (LOLA, Diviner)</div>");
        sb.Append($"<p><button onclick='window.print()'>🖨 {E(L.T("Yazdır / PDF olarak kaydet", "Print / Save as PDF"))}</button></p>");

        if (main != null)
        {
            var near = LandingCatalog.Nearest(main.Site);
            sb.Append($"<h2>{E(L.T("Seçilen nokta", "Selected site"))}</h2><div class='card'>");
            sb.Append($"<div style='font-size:18px;font-weight:600'>{E(main.Site.Name)}</div>");
            sb.Append($"<div class='muted'>{Coord(main.Site)} · {E(L.T("Görev profili", "Mission profile"))}: {E(main.ProfileName)} · " +
                      $"{E(L.T("En yakın gerçek iniş", "Nearest real landing"))}: {E(near.Landing.Name)} ({near.Landing.Date:yyyy}), {near.DistanceKm:F0} km</div>");
            sb.Append($"<div class='score' style='color:{ColorOf(main.OverallScore)}'>{F(main.OverallScore)} / 100</div>");
            sb.Append($"<div class='verdict' style='color:{(main.HardLimitViolations.Count > 0 ? "#ff7b6b" : ColorOf(main.OverallScore))}'>{E(main.Verdict)}</div>");
            foreach (var v in main.HardLimitViolations) sb.Append($"<div class='bad'>✖ {E(v)}</div>");
            foreach (var w in main.Warnings) sb.Append($"<div class='warn'>⚠ {E(w)}</div>");
            sb.Append("</div>");

            sb.Append($"<h2>{E(L.T("Kriterler", "Criteria"))}</h2>");
            foreach (var c in main.Criteria)
            {
                double s = c.Score ?? double.NaN;
                sb.Append("<div class='card'>");
                sb.Append($"<b>{E(c.Name)}</b> <span class='muted'>({E(L.T("ağırlık", "weight"))} {c.Weight:F0}%)</span><br>");
                sb.Append($"<span class='bar'><span style='width:{(double.IsNaN(s) ? 0 : s):F0}%;background:{ColorOf(s)}'></span></span> " +
                          $"<b style='color:{ColorOf(s)}'>{F(s, "F0")}/100</b> &nbsp; {E(c.Summary)}");
                if (c.Details.Count > 0)
                {
                    sb.Append("<ul>");
                    foreach (var d in c.Details) sb.Append($"<li>{E(d)}</li>");
                    sb.Append("</ul>");
                }
                sb.Append("</div>");
            }

            if (main.Sun is { LandingWindowFound: true } sun)
                sb.Append($"<div class='card'><b>{E(L.T("Önerilen iniş penceresi", "Recommended landing window"))}</b> " +
                          $"({E(L.T("sabah, güneş 7°–20°", "morning, Sun 7°–20°"))}): " +
                          $"{sun.NextLandingWindowStartUtc.ToLocalTime():dd.MM.yyyy HH:mm} → {sun.NextLandingWindowEndUtc.ToLocalTime():dd.MM.yyyy HH:mm}" +
                          $"<div class='muted'>{E(L.T("Alçak güneşte kayalar ve kraterler uzun gölge verir (Apollo inişleri ~10°). Hata payı ±12 saat.", "A low Sun makes rocks and craters cast long shadows (Apollo landings ~10°). Uncertainty ±12 hours."))}</div></div>");
        }

        if (all.Count > 0)
        {
            sb.Append($"<h2>{E(L.T("Analiz edilen tüm noktaların karşılaştırması", "Comparison of all analyzed sites"))}</h2><table><tr>" +
                      $"<th>{E(L.T("Nokta", "Site"))}</th><th>{E(L.T("Koordinat", "Coordinates"))}</th><th>{E(L.T("Genel", "Overall"))}</th><th>{E(L.T("Karar", "Verdict"))}</th>");
            foreach (var c in all[0].Criteria) sb.Append($"<th>{E(c.Name)}</th>");
            sb.Append("</tr>");
            foreach (var ev in all.OrderByDescending(x => x.OverallScore))
            {
                sb.Append($"<tr><td>{E(ev.Site.Name)}</td><td>{Coord(ev.Site)}</td>" +
                          $"<td><span class='pill' style='background:{ColorOf(ev.OverallScore)}'>{F(ev.OverallScore)}</span></td>" +
                          $"<td>{E(ev.Verdict)}</td>");
                foreach (var c in ev.Criteria)
                {
                    double s = c.Score ?? double.NaN;
                    sb.Append($"<td style='color:{ColorOf(s)};font-weight:600'>{F(s, "F0")}</td>");
                }
                sb.Append("</tr>");
            }
            sb.Append("</table>");
        }

        if (global != null)
        {
            sb.Append($"<h2>{E(L.T("Küresel uygunluk haritası özeti", "Global suitability map summary"))}</h2><div class='card'>");
            sb.Append(L.T(
                $"Profil: <b>{E(global.ProfileName)}</b> · Ay yüzeyinin ortalama puanı: <b>{global.MeanOverall:F1}</b> · " +
                $"\"Uygun\" (≥65 ve kritik sınırı aşmayan) yüzey oranı: <b>%{global.PercentSuitable:F1}</b>",
                $"Profile: <b>{E(global.ProfileName)}</b> · Mean score of the lunar surface: <b>{global.MeanOverall:F1}</b> · " +
                $"\"Suitable\" surface (≥65 and within critical limits): <b>{global.PercentSuitable:F1}%</b>"));
            sb.Append($"<div class='muted'>{E(L.T("Küresel harita ~2 km ölçeğinde bir ön eleme haritasıdır (LOLA LDEM_16 + Diviner kaya haritası). Kesin karar için 237 m çözünürlüklü tekli analiz kullanılmıştır.", "The global map is a ~2 km-scale pre-screening map (LOLA LDEM_16 + Diviner rock map). The 237 m-resolution site analysis is used for final decisions."))}</div></div>");
        }

        sb.Append($"<h2>{E(L.T("Yöntem ve veri kaynakları", "Methods and data sources"))}</h2><div class='card'><ul>");
        string[][] methods =
        {
            new[] { "Yükseklik ve eğim: LRO LOLA LDEM_128 (~237 m/piksel), Horn (1981) eğim yöntemi.",
                    "Elevation and slope: LRO LOLA LDEM_128 (~237 m/pixel), Horn (1981) slope method." },
            new[] { "Kaya yoğunluğu: LRO Diviner GDR L3 (Bandfield vd. 2011). Krater tespiti: LOLA halka profili, d/D ≥ 0.05 (Pike 1977).",
                    "Rock abundance: LRO Diviner GDR L3 (Bandfield et al. 2011). Crater detection: LOLA ring profiles, d/D ≥ 0.05 (Pike 1977)." },
            new[] { "Gündüz sıcaklığı: ışınım dengesi εσT⁴ = (1−A(i))·S·cos i (Hayne vd. 2017). Gece: Diviner ölçümü.",
                    "Daytime temperature: radiative equilibrium εσT⁴ = (1−A(i))·S·cos i (Hayne et al. 2017). Night: Diviner measurement." },
            new[] { "Güneş ve Dünya geometrisi: Ay ekseni eğikliği 1.54°, librasyon ±7.9° / ±6.7°, 30 km'lik LOLA ufuk profili.",
                    "Sun and Earth geometry: lunar axial tilt 1.54°, libration ±7.9° / ±6.7°, 30 km LOLA horizon profile." },
            new[] { "Eğim sınırları: Apollo LM 12°; NASA Artemis iniş aracı ≤8° (dikeyden sapma), bölge analizleri ≤10°.",
                    "Slope limits: Apollo LM 12°; NASA Artemis lander ≤8° (tilt from vertical), regional analyses ≤10°." },
            new[] { "Tarihî iniş koordinatları: NASA NSSDC, Table of Anthropogenic Impacts and Spacecraft on the Moon.",
                    "Historic landing coordinates: NASA NSSDC, Table of Anthropogenic Impacts and Spacecraft on the Moon." },
        };
        foreach (var m in methods) sb.Append($"<li>{E(L.T(m[0], m[1]))}</li>");
        sb.Append("</ul></div>");

        if (main != null && main.DataSources.Count > 0)
        {
            sb.Append($"<div class='muted' style='font-size:12px'>{E(L.T("Veri dosyaları", "Data files"))}:<br>");
            foreach (var d in main.DataSources.Distinct()) sb.Append($"{E(d)}<br>");
            sb.Append("</div>");
        }
        sb.Append("</body></html>");
        return sb.ToString();
    }
}

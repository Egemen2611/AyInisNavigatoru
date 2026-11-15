using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AyInisNavigatoru.Data;

/// <summary>
/// PDS3 (Planetary Data System) etiket (.LBL) dosyası çözümleyicisi.
/// Örnek satırlar:  LINES = 23040   |  MAP_RESOLUTION = 128 &lt;PIX/DEG&gt;
/// OBJECT = IMAGE ... END_OBJECT = IMAGE bloklarını "IMAGE.LINES" şeklinde saklar.
/// </summary>
public sealed class PdsLabel
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public static PdsLabel Parse(string text)
    {
        var label = new PdsLabel();
        var objectStack = new Stack<string>();
        string[] lines = text.Replace("\r", "").Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = StripComment(lines[i]).Trim();
            if (line.Length == 0) continue;
            if (line.Equals("END", StringComparison.OrdinalIgnoreCase)) break;

            int eq = line.IndexOf('=');
            if (eq < 0) continue;

            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();

            // Birden çok satıra yayılan tırnaklı metin
            if (value.StartsWith('"') && value.Count(ch => ch == '"') < 2)
            {
                var sb = new StringBuilder(value);
                while (++i < lines.Length)
                {
                    sb.Append(' ').Append(lines[i].Trim());
                    if (lines[i].Contains('"')) break;
                }
                value = sb.ToString();
            }
            // Birden çok satıra yayılan parantezli liste
            else if (value.StartsWith('(') && !value.Contains(')'))
            {
                var sb = new StringBuilder(value);
                while (++i < lines.Length)
                {
                    sb.Append(' ').Append(StripComment(lines[i]).Trim());
                    if (lines[i].Contains(')')) break;
                }
                value = sb.ToString();
            }

            if (key.Equals("OBJECT", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("GROUP", StringComparison.OrdinalIgnoreCase))
            {
                objectStack.Push(Unquote(value));
                continue;
            }
            if (key.Equals("END_OBJECT", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("END_GROUP", StringComparison.OrdinalIgnoreCase))
            {
                if (objectStack.Count > 0) objectStack.Pop();
                continue;
            }

            if (objectStack.Count > 0)
                label._values[objectStack.Peek() + "." + key] = value;

            // Çıplak anahtar: ilk görülen değer saklanır
            label._values.TryAdd(key, value);
        }
        return label;
    }

    public string? GetRaw(string key) => _values.TryGetValue(key, out var v) ? v : null;

    /// <summary>Önce "NESNE.ANAHTAR", sonra çıplak anahtar aranır.</summary>
    public string? GetRaw(string objectName, string key) =>
        GetRaw(objectName + "." + key) ?? GetRaw(key);

    public double? GetNumber(string objectName, string key) => ParseNumber(GetRaw(objectName, key));
    public double? GetNumber(string key) => ParseNumber(GetRaw(key));

    public string? GetString(string objectName, string key)
    {
        var raw = GetRaw(objectName, key);
        return raw == null ? null : Unquote(raw);
    }

    /// <summary>"128 &lt;PIX/DEG&gt;", "0.5", "16#FFFF#" gibi değerleri sayıya çevirir.</summary>
    public static double? ParseNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string s = Unquote(raw);
        int unit = s.IndexOf('<');
        if (unit >= 0) s = s[..unit];
        s = s.Trim();

        // PDS onaltılık gösterim: 16#FF7FFFFB#
        var hex = Regex.Match(s, @"^(-?)(\d+)#([0-9A-Fa-f]+)#$");
        if (hex.Success)
        {
            int radix = int.Parse(hex.Groups[2].Value, CultureInfo.InvariantCulture);
            long v = Convert.ToInt64(hex.Groups[3].Value, radix);
            return hex.Groups[1].Value == "-" ? -v : v;
        }

        string first = s.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;
    }

    public static string Unquote(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') s = s[1..^1];
        return s.Trim();
    }

    private static string StripComment(string line)
    {
        int start = line.IndexOf("/*", StringComparison.Ordinal);
        if (start < 0) return line;
        int end = line.IndexOf("*/", start + 2, StringComparison.Ordinal);
        return end < 0 ? line[..start] : line[..start] + line[(end + 2)..];
    }
}

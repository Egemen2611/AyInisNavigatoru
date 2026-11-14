using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace AyInisNavigatoru.Data;

/// <summary>
/// Uzak sunucudaki bir PDS3 IMG raster dosyası (ör. LOLA LDEM_128.IMG, 2 GB).
/// Dosyanın tamamı İNDİRİLMEZ: HTTP "Range" istekleriyle yalnızca seçilen
/// noktanın etrafındaki satırlar okunur ve diskte önbelleğe alınır.
/// </summary>
public sealed class PdsImageSource
{
    private readonly HttpClient _http;
    private readonly string _cacheDir;
    private bool _littleEndian, _isReal, _unsigned;

    public string Name { get; }
    public string ImageUrl { get; private set; } = "";
    public PdsLabel Label { get; private set; } = new();

    public int Lines { get; private set; }
    public int Samples { get; private set; }
    public int BytesPerSample { get; private set; }
    public string SampleType { get; private set; } = "";
    public double ScalingFactor { get; private set; } = 1.0;
    public double Offset { get; private set; }
    public long? MissingDn { get; private set; }
    public long ImageByteOffset { get; private set; }
    public long LineBytes => (long)Samples * BytesPerSample;

    /// <summary>Piksel/derece</summary>
    public double Resolution { get; private set; }
    public double MaximumLatitude { get; private set; }
    public double WesternmostLongitude { get; private set; }
    public bool IsGlobalInLongitude => Math.Abs(Samples - 360.0 * Resolution) < 1.0;

    /// <summary>Bir pikselin kuzey-güney boyutu (metre)</summary>
    public double PixelSizeM => Math.PI * Config.MoonRadiusM / (180.0 * Resolution);

    private PdsImageSource(HttpClient http, string name, string cacheDir)
    {
        _http = http;
        Name = name;
        _cacheDir = cacheDir;
    }

    /// <summary>Etiketi indirir, çözümler ve görüntü dosyasının adresini doğrular.</summary>
    public static async Task<PdsImageSource> OpenAsync(HttpClient http, string baseUrl, string productName, string cacheDir)
    {
        var src = new PdsImageSource(http, productName, cacheDir);
        Directory.CreateDirectory(cacheDir);

        // 1) Etiket (.LBL) dosyası: önce önbellek, sonra farklı yazımlar denenir
        string labelCache = Path.Combine(cacheDir, productName + ".lbl.txt");
        string? labelText = File.Exists(labelCache) ? await File.ReadAllTextAsync(labelCache) : null;
        string? labelUrlUsed = null;

        if (labelText == null)
        {
            foreach (var candidate in new[] { productName + ".LBL", productName + ".lbl",
                                              productName.ToUpperInvariant() + ".LBL",
                                              productName.ToLowerInvariant() + ".lbl" }.Distinct())
            {
                try
                {
                    labelText = await http.GetStringAsync(baseUrl + candidate);
                    labelUrlUsed = baseUrl + candidate;
                    await File.WriteAllTextAsync(labelCache, labelText);
                    break;
                }
                catch (HttpRequestException) { /* sıradaki yazımı dene */ }
            }
        }
        if (labelText == null)
            throw new InvalidOperationException($"{productName} için PDS etiketi indirilemedi ({baseUrl}).");

        src.Label = PdsLabel.Parse(labelText);
        src.ReadLabelMetadata();

        // 2) Görüntü dosyasının adı: ^IMAGE işaretçisinden veya ürün adından
        var imageNames = new List<string>();
        string? pointer = src.Label.GetRaw("^IMAGE");
        if (pointer != null)
        {
            var m = Regex.Match(pointer, "\"([^\"]+)\"");
            if (m.Success)
            {
                imageNames.Add(m.Groups[1].Value);
                imageNames.Add(m.Groups[1].Value.ToLowerInvariant());
                imageNames.Add(m.Groups[1].Value.ToUpperInvariant());

                // ("dosya", 5) veya ("dosya", 1024 <BYTES>) biçimindeki başlangıç ofseti
                var off = Regex.Match(pointer, @",\s*(\d+)\s*(<\s*BYTES\s*>)?", RegexOptions.IgnoreCase);
                if (off.Success)
                {
                    long n = long.Parse(off.Groups[1].Value);
                    long recordBytes = (long)(src.Label.GetNumber("RECORD_BYTES") ?? src.LineBytes);
                    src.ImageByteOffset = off.Groups[2].Success ? n - 1 : (n - 1) * recordBytes;
                }
            }
        }
        imageNames.Add(productName + ".IMG");
        imageNames.Add(productName + ".img");
        imageNames.Add(productName.ToLowerInvariant() + ".img");

        string urlCache = Path.Combine(cacheDir, productName + ".url.txt");
        if (File.Exists(urlCache))
        {
            src.ImageUrl = (await File.ReadAllTextAsync(urlCache)).Trim();
            return src;
        }

        foreach (var name in imageNames.Distinct())
        {
            string url = baseUrl + name;
            if (await src.SupportsRangeAsync(url))
            {
                src.ImageUrl = url;
                await File.WriteAllTextAsync(urlCache, url);
                return src;
            }
        }
        throw new InvalidOperationException(
            $"{productName} görüntü dosyasına erişilemedi veya sunucu kısmi indirmeyi (HTTP Range) desteklemiyor. Etiket: {labelUrlUsed ?? "önbellek"}");
    }

    private void ReadLabelMetadata()
    {
        const string img = "IMAGE";
        const string proj = "IMAGE_MAP_PROJECTION";

        Lines = (int)(Label.GetNumber(img, "LINES") ?? throw new InvalidOperationException($"{Name}: LINES yok"));
        Samples = (int)(Label.GetNumber(img, "LINE_SAMPLES") ?? throw new InvalidOperationException($"{Name}: LINE_SAMPLES yok"));
        int bits = (int)(Label.GetNumber(img, "SAMPLE_BITS") ?? 16);
        BytesPerSample = bits / 8;
        SampleType = (Label.GetString(img, "SAMPLE_TYPE") ?? "LSB_INTEGER").ToUpperInvariant();
        _littleEndian = SampleType.Contains("LSB") || SampleType.Contains("PC_") || SampleType.Contains("VAX");
        _isReal = SampleType.Contains("REAL");
        _unsigned = SampleType.Contains("UNSIGNED");
        ScalingFactor = Label.GetNumber(img, "SCALING_FACTOR") ?? 1.0;
        Offset = Label.GetNumber(img, "OFFSET") ?? 0.0;

        double? missing = Label.GetNumber(img, "MISSING_CONSTANT") ?? Label.GetNumber(img, "NULL")
                          ?? Label.GetNumber(img, "INVALID_CONSTANT");
        if (missing.HasValue && !SampleType.Contains("REAL"))
        {
            long dn = (long)missing.Value;
            // 16#FFFF# gibi işaretsiz yazılmış işaretli 16-bit değerler
            if (BytesPerSample == 2 && !SampleType.Contains("UNSIGNED") && dn > short.MaxValue) dn -= 65536;
            MissingDn = dn;
        }

        Resolution = Label.GetNumber(proj, "MAP_RESOLUTION")
                     ?? throw new InvalidOperationException($"{Name}: MAP_RESOLUTION yok");

        double centerLat = Label.GetNumber(proj, "CENTER_LATITUDE") ?? 0;
        double centerLon = Label.GetNumber(proj, "CENTER_LONGITUDE") ?? 180;
        double? maxLat = Label.GetNumber(proj, "MAXIMUM_LATITUDE");
        double? westLon = Label.GetNumber(proj, "WESTERNMOST_LONGITUDE");
        double? lineOffset = Label.GetNumber(proj, "LINE_PROJECTION_OFFSET");
        double? sampleOffset = Label.GetNumber(proj, "SAMPLE_PROJECTION_OFFSET");

        // Haritanın sol-üst köşesi. Öncelik kenar değerlerinde, yoksa projeksiyon ofsetlerinden türetilir.
        MaximumLatitude = maxLat ?? (lineOffset.HasValue ? centerLat + (lineOffset.Value + 0.5) / Resolution : 90);
        WesternmostLongitude = westLon ?? (sampleOffset.HasValue ? centerLon - (sampleOffset.Value + 0.5) / Resolution : 0);
    }

    /// <summary>
    /// Enlem/boylamı (doğu pozitif, derece) sıfır tabanlı kesirli piksel koordinatına çevirir.
    /// Piksel merkezleri tam sayılardadır.
    /// </summary>
    public (double Row, double Col) ToPixel(double latDeg, double lonEastDeg)
    {
        double row = (MaximumLatitude - latDeg) * Resolution - 0.5;
        double dLon = ((lonEastDeg - WesternmostLongitude) % 360.0 + 360.0) % 360.0;
        double col = dLon * Resolution - 0.5;
        return (row, col);
    }

    public double RowToLatitude(double row) => MaximumLatitude - (row + 0.5) / Resolution;
    public double ColToLongitude(double col) => WesternmostLongitude + (col + 0.5) / Resolution;

    private async Task<bool> SupportsRangeAsync(string url)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new RangeHeaderValue(0, 1);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            return resp.StatusCode == HttpStatusCode.PartialContent;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    /// <summary>[firstRow, lastRow] aralığındaki tam satırları okur (önbellekli).</summary>
    public async Task<RasterBlock> ReadRowsAsync(int firstRow, int lastRow)
    {
        firstRow = Math.Clamp(firstRow, 0, Lines - 1);
        lastRow = Math.Clamp(lastRow, 0, Lines - 1);
        int rowCount = lastRow - firstRow + 1;
        long length = rowCount * LineBytes;

        string cacheFile = Path.Combine(_cacheDir, $"{Name}_r{firstRow}-{lastRow}.bin");
        byte[] bytes;
        if (File.Exists(cacheFile) && new FileInfo(cacheFile).Length == length)
        {
            bytes = await File.ReadAllBytesAsync(cacheFile);
        }
        else
        {
            long start = ImageByteOffset + firstRow * LineBytes;
            bytes = await ReadRangeAsync(start, length);
            await File.WriteAllBytesAsync(cacheFile, bytes);
        }

        var data = new float[rowCount * (long)Samples];
        for (long i = 0; i < data.LongLength; i++)
            data[i] = Decode(bytes, i * BytesPerSample);

        return new RasterBlock(this, firstRow, rowCount, Samples, data);
    }

    private async Task<byte[]> ReadRangeAsync(long start, long length)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, ImageUrl);
        req.Headers.Range = new RangeHeaderValue(start, start + length - 1);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        if (resp.StatusCode != HttpStatusCode.PartialContent)
            throw new InvalidOperationException($"{Name}: sunucu kısmi indirme yanıtı vermedi (HTTP {(int)resp.StatusCode}).");

        var buffer = new byte[length];
        await using var stream = await resp.Content.ReadAsStreamAsync();
        int read = 0;
        while (read < length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read, (int)Math.Min(length - read, 1 << 20)));
            if (n == 0) break;
            read += n;
        }
        if (read < length)
            throw new InvalidOperationException($"{Name}: veri eksik indirildi ({read}/{length} bayt).");
        return buffer;
    }

    private float Decode(byte[] b, long index)
    {
        int i = (int)index;
        bool littleEndian = _littleEndian, isReal = _isReal, unsigned = _unsigned;
        double value;

        if (isReal)
        {
            if (BytesPerSample == 4)
            {
                int raw = littleEndian ? BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(i, 4))
                                       : BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(i, 4));
                value = BitConverter.Int32BitsToSingle(raw);
            }
            else
            {
                long raw = littleEndian ? BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(i, 8))
                                        : BinaryPrimitives.ReadInt64BigEndian(b.AsSpan(i, 8));
                value = BitConverter.Int64BitsToDouble(raw);
            }
            if (double.IsNaN(value) || Math.Abs(value) > 1e30) return float.NaN;
        }
        else
        {
            long dn;
            if (BytesPerSample == 2)
            {
                dn = unsigned
                    ? (littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(i, 2)) : BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(i, 2)))
                    : (littleEndian ? BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(i, 2)) : BinaryPrimitives.ReadInt16BigEndian(b.AsSpan(i, 2)));
                if (!unsigned && dn == short.MinValue) return float.NaN;
            }
            else if (BytesPerSample == 4)
            {
                dn = unsigned
                    ? (littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(i, 4)) : BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(i, 4)))
                    : (littleEndian ? BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(i, 4)) : BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(i, 4)));
            }
            else
            {
                dn = b[i];
            }
            if (MissingDn.HasValue && dn == MissingDn.Value) return float.NaN;
            value = dn;
        }
        return (float)(value * ScalingFactor + Offset);
    }
}

/// <summary>Bellekteki bir satır bloğu. Boylamda 0/360 sarması desteklenir.</summary>
public sealed class RasterBlock
{
    public PdsImageSource Source { get; }
    public int FirstRow { get; }
    public int RowCount { get; }
    public int Samples { get; }
    private readonly float[] _data;
    public float[] RawData => _data;

    public RasterBlock(PdsImageSource source, int firstRow, int rowCount, int samples, float[] data)
    {
        Source = source; FirstRow = firstRow; RowCount = rowCount; Samples = samples; _data = data;
    }

    public float Get(int row, int col)
    {
        if (row < FirstRow || row >= FirstRow + RowCount) return float.NaN;
        if (Source.IsGlobalInLongitude) col = ((col % Samples) + Samples) % Samples;
        else if (col < 0 || col >= Samples) return float.NaN;
        return _data[(row - FirstRow) * (long)Samples + col];
    }

    /// <summary>Çift doğrusal (bilinear) enterpolasyon.</summary>
    public float Bilinear(double row, double col)
    {
        int r0 = (int)Math.Floor(row), c0 = (int)Math.Floor(col);
        double fr = row - r0, fc = col - c0;
        float v00 = Get(r0, c0), v01 = Get(r0, c0 + 1), v10 = Get(r0 + 1, c0), v11 = Get(r0 + 1, c0 + 1);
        if (float.IsNaN(v00) || float.IsNaN(v01) || float.IsNaN(v10) || float.IsNaN(v11))
        {
            // Kenarda/boşlukta en yakın komşuya düş
            return Get((int)Math.Round(row), (int)Math.Round(col));
        }
        return (float)((v00 * (1 - fc) + v01 * fc) * (1 - fr) + (v10 * (1 - fc) + v11 * fc) * fr);
    }

    public float AtLatLon(double latDeg, double lonEastDeg)
    {
        var (r, c) = Source.ToPixel(latDeg, lonEastDeg);
        return Bilinear(r, c);
    }
}

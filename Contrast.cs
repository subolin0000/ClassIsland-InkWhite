namespace AdaptiveText;

public sealed record ColorPresetOption(string Name, string Value);

public static class ColorPresets
{
    public const string Balanced = "Balanced";
    public const string Black = "Black";
    public const string White = "White";
    public static readonly ColorPresetOption[] All =
    [
        new("黑白均衡", Balanced),
        new("黑色为主", Black),
        new("白色为主", White),
    ];
    public static string Normalize(string? preset) => preset == Black || preset == White ? preset : Balanced;
}

public sealed record BackgroundDecision(double Luminance, double DominantLuminance, double Complexity, bool UseWhite);

public static class Contrast
{
    // A single explicit boundary: neutral #606060 and brighter always choose black.
    public static readonly double DarkBackgroundCutoff = Luminance(96, 96, 96);
    public static readonly double BlackBiasCutoff = Luminance(48, 48, 48);
    public static readonly double WhiteBiasCutoff = Luminance(160, 160, 160);
    public static double DarkBackgroundCutoffFor(string? preset) => ColorPresets.Normalize(preset) switch
    {
        ColorPresets.Black => BlackBiasCutoff,
        ColorPresets.White => WhiteBiasCutoff,
        _ => DarkBackgroundCutoff,
    };
    public static bool UseWhiteForLuminance(double luminance) => UseWhiteForLuminance(luminance, null);
    public static bool UseWhiteForLuminance(double luminance, string? preset) => luminance < DarkBackgroundCutoffFor(preset);
    public static double BackgroundLuminance(ReadOnlySpan<double> luminances)
    {
        if (luminances.Length == 0) throw new ArgumentException("No background samples.");
        Span<double> values = luminances.Length <= 256 ? stackalloc double[luminances.Length] : new double[luminances.Length];
        luminances.CopyTo(values); values.Sort();
        int middle = values.Length / 2;
        return values.Length % 2 == 0 ? (values[middle - 1] + values[middle]) / 2 : values[middle];
    }
    public static bool UseWhiteForPixels(ReadOnlySpan<byte> pixels) => UseWhiteForPixels(pixels, null);
    public static bool UseWhiteForPixels(ReadOnlySpan<byte> pixels, string? preset)
    {
        if (pixels.Length == 0 || pixels.Length % 4 != 0) throw new ArgumentException("BGRA samples required.");
        int count = pixels.Length / 4;
        Span<double> values = count <= 256 ? stackalloc double[count] : new double[count];
        for (int i = 0; i < count; i++) values[i] = Luminance(pixels[i * 4 + 2], pixels[i * 4 + 1], pixels[i * 4]);
        return UseWhiteForLuminance(BackgroundLuminance(values), preset);
    }
    public static double Luminance(byte r, byte g, byte b)
    {
        static double Linear(byte c) { var s = c / 255.0; return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b);
    }

    // Element complexity measures how heterogeneous the sampled background is. A wallpaper with text
    // forms multiple luminance clusters; relying only on the median can let minority text pixels win.
    // Uses fixed histogram buckets and no heap allocations so low-end classroom machines stay cheap.
    public static BackgroundDecision AnalyzeBackground(ReadOnlySpan<double> luminances, string? preset = null)
    {
        if (luminances.IsEmpty) throw new ArgumentException("No background samples.");
        const int BinCount = 64;
        Span<int> counts = stackalloc int[BinCount];
        Span<double> sums = stackalloc double[BinCount];
        foreach (var luminance in luminances)
        {
            int bin = Math.Clamp((int)(luminance * BinCount), 0, BinCount - 1);
            counts[bin]++; sums[bin] += luminance;
        }
        int top = 0;
        for (int i = 1; i < BinCount; i++) if (counts[i] > counts[top]) top = i;
        double dominantLuminance = counts[top] > 0 ? sums[top] / counts[top] : 0;
        double topShare = counts[top] / (double)luminances.Length;
        double complexity = 1 - topShare;
        int middle = luminances.Length / 2;
        int cumulative = 0; int medianBin = BinCount - 1;
        for (int i = 0; i < BinCount; i++)
        {
            cumulative += counts[i];
            if (cumulative > middle) { medianBin = i; break; }
        }
        double median = (medianBin + .5) / BinCount;
        // Only switch to the dominant element when there is a clear enough mixed-element pattern.
        bool useWhite = complexity > .15 && counts[top] >= 3 && topShare >= .35
            ? UseWhiteForLuminance(dominantLuminance, preset)
            : UseWhiteForLuminance(median, preset);
        return new BackgroundDecision(median, dominantLuminance, complexity, useWhite);
    }
}


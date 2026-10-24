namespace AdaptiveText;
public static class Contrast
{
    // A single explicit boundary: neutral #606060 and brighter always choose black.
    public static readonly double DarkBackgroundCutoff = Luminance(96, 96, 96);
    public static bool UseWhiteForLuminance(double luminance) => luminance < DarkBackgroundCutoff;
    public static double BackgroundLuminance(ReadOnlySpan<double> luminances)
    {
        if (luminances.Length == 0) throw new ArgumentException("No background samples.");
        Span<double> values = luminances.Length <= 256 ? stackalloc double[luminances.Length] : new double[luminances.Length];
        luminances.CopyTo(values); values.Sort();
        int middle = values.Length / 2;
        return values.Length % 2 == 0 ? (values[middle - 1] + values[middle]) / 2 : values[middle];
    }
    public static bool UseWhiteForPixels(ReadOnlySpan<byte> pixels)
    {
        if (pixels.Length == 0 || pixels.Length % 4 != 0) throw new ArgumentException("BGRA samples required.");
        int count = pixels.Length / 4;
        Span<double> values = count <= 256 ? stackalloc double[count] : new double[count];
        for (int i = 0; i < count; i++) values[i] = Luminance(pixels[i * 4 + 2], pixels[i * 4 + 1], pixels[i * 4]);
        return UseWhiteForLuminance(BackgroundLuminance(values));
    }
    public static double Luminance(byte r, byte g, byte b)
    {
        static double Linear(byte c) { var s = c / 255.0; return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b);
    }
}


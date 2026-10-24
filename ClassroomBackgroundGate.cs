namespace AdaptiveText;
// Detect a settled background, rather than merely a consistently winning text color.
public sealed class ClassroomBackgroundGate
{
    private byte[]? _anchor;
    private long _since, _lastSample;
    private bool _dirty = true;
    private int _samples;
    public bool Waiting => _dirty;
    public bool Observe(ReadOnlySpan<byte> rgb, long now, int quietMs)
    {
        if (_anchor == null || _anchor.Length != rgb.Length)
        {
            _anchor = rgb.ToArray(); _since = now; _lastSample = now; _samples = 1; _dirty = true; return false;
        }
        // Long sampling interruptions cannot be counted as observed stable time.
        if (now - _lastSample > Math.Max(3000, quietMs)) { _since = now; _samples = 0; _dirty = true; }
        _lastSample = now;
        if (Changed(rgb, _anchor))
        {
            rgb.CopyTo(_anchor); _since = now; _samples = 1; _dirty = true; return false;
        }
        _samples++;
        if (!_dirty || _samples < 3 || now - _since < quietMs) return false;
        _dirty = false; return true;
    }
    public void Pause(long now) { _since = now; _samples = 0; _dirty = true; }
    public static bool Changed(ReadOnlySpan<byte> sample, ReadOnlySpan<byte> anchor)
    {
        if (sample.Length != anchor.Length || sample.Length == 0) return true;
        if (Contrast.UseWhiteForPixels(sample) != Contrast.UseWhiteForPixels(anchor)) return true;
        double sum = 0; int changedPoints = 0;
        for (int i = 0; i < sample.Length; i += 4)
        {
            int distance = Math.Abs(sample[i] - anchor[i]) + Math.Abs(sample[i + 1] - anchor[i + 1]) + Math.Abs(sample[i + 2] - anchor[i + 2]);
            sum += distance; if (distance > 3 * 32) changedPoints++;
        }
        return sum / (sample.Length / 4 * 3) > 10 || changedPoints >= Math.Max(2, sample.Length / 4 / 4);
    }
}
public static class ClassroomTween
{
    public static double Progress(long now, long start, int duration) => Math.Clamp((double)(now - start) / Math.Max(1, duration), 0, 1);
    public static byte Channel(byte from, byte to, double progress)
    {
        double t = Math.Clamp(progress, 0, 1); t = t * t * (3 - 2 * t);
        return (byte)Math.Round(from + (to - from) * t);
    }
}


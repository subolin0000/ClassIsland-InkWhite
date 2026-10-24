using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Assists;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace AdaptiveText;
public sealed class AdaptiveService(SettingsStore settings, ILogger<AdaptiveService> logger) : IHostedService
{
    private DispatcherTimer? _timer;
    private NativeCapture? _capture;
    private DispatcherTimer? _fadeTimer;
    private readonly Dictionary<Window, ClassroomGroup> _classrooms = new();
    private int SamplingInterval => settings.Value.IntervalMs;
    private readonly Dictionary<TextBlock, TextState> _texts = new();
    private readonly Dictionary<Window, (nint Handle, uint Previous)> _windows = new();
    private readonly Dictionary<Window, (long ScannedAt, TextBlock[] Texts)> _treeCache = new();
    private readonly HashSet<Window> _failed = new();
    public string Status { get; private set; } = "等待主窗口";
    public Task StartAsync(CancellationToken cancellationToken)
    {
        settings.Changed += OnSettingsChanged;
        Dispatcher.UIThread.Post(() =>
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SamplingInterval) };
            _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _fadeTimer.Tick += (_, _) =>
            {
                long now = Environment.TickCount64;
                foreach (var group in _classrooms.Values) group.Tick(now);
                if (!_classrooms.Values.Any(g => g.Animating)) _fadeTimer.Stop();
            };
            _timer.Tick += Tick; _timer.Start();
        });
        return Task.CompletedTask;
    }
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        settings.Changed -= OnSettingsChanged;
        if (Dispatcher.UIThread.CheckAccess()) { _timer?.Stop(); Reset(); }
        else await Dispatcher.UIThread.InvokeAsync(() => { _timer?.Stop(); Reset(); });
    }
    private void OnSettingsChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (_timer != null) _timer.Interval = TimeSpan.FromMilliseconds(SamplingInterval);
        Reset(); _timer?.Start();
    });
    private void Reset()
    {
        foreach (var state in _texts.Values) state.Dispose();
        _texts.Clear();
        _fadeTimer?.Stop(); foreach (var group in _classrooms.Values) group.Dispose(); _classrooms.Clear();
        foreach (var entry in _windows.Values) NativeCapture.SetWindowDisplayAffinity(entry.Handle, entry.Previous);
        _windows.Clear(); _treeCache.Clear(); _failed.Clear();
        _capture?.Dispose(); _capture = null;
    }
    private void Tick(object? sender, EventArgs args)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            Update();
            // Back off on slow machines instead of immediately starting another expensive frame.
            double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (_timer != null) _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(SamplingInterval, Math.Min(2000, Math.Ceiling(elapsed * 5))));
        }
        catch (Exception e)
        {
            logger.LogError(e, "自适应文字已暂停，可保存设置后重新启用");
            Reset(); _timer?.Stop(); Status = "已暂停：" + e.Message;
        }
    }
    private void Update()
    {
        if (!settings.Value.Enabled) { Status = "已停用，原始字体与截图行为已恢复"; return; }
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) { Status = "需要 Windows 10 2004 或更高版本"; return; }
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        long now = Environment.TickCount64;

        var live = desktop.Windows.Where(w => w.GetType().FullName == "ClassIsland.MainWindow" && w.IsVisible).ToHashSet();
        foreach (var window in _windows.Keys.Where(w => !live.Contains(w)).ToArray())
        {
            var entry = _windows[window]; NativeCapture.SetWindowDisplayAffinity(entry.Handle, entry.Previous); _windows.Remove(window);
        }
        foreach (var window in _treeCache.Keys.Where(w => !live.Contains(w)).ToArray()) _treeCache.Remove(window);
        _failed.RemoveWhere(w => !live.Contains(w));
        foreach (var window in _classrooms.Keys.Where(w => !live.Contains(w)).ToArray()) { _classrooms[window].Dispose(); _classrooms.Remove(window); }
        var retained = new HashSet<TextBlock>(); var sampled = new HashSet<TextBlock>();
        int captures = 0;
        Span<byte> pixels = stackalloc byte[84];
        foreach (var window in live)
        {
            if (!_treeCache.TryGetValue(window, out var cache) || now - cache.ScannedAt >= 1000)
            {
                cache = (now, window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.GetVisualAncestors().Any(v => v is ComponentBase)).ToArray());
                _treeCache[window] = cache;
            }
            // The attached edit flag is inherited by component controls.
            if (MainWindowStylesAssist.GetMainWindowInEditMode(window) || cache.Texts.Any(MainWindowStylesAssist.GetMainWindowInEditMode))
            {
                if (_classrooms.Remove(window, out var editingGroup)) editingGroup.Dispose();
                if (_windows.Remove(window, out var old)) NativeCapture.SetWindowDisplayAffinity(old.Handle, old.Previous);
                continue;
            }
            ClassroomGroup? group = null;
            {
                if (!_classrooms.TryGetValue(window, out group)) _classrooms.Add(window, group = new ClassroomGroup(settings.Value.Shadow));
                group.SyncTargets(window, now); group.BeginScores();
            }
            var regions = new List<(TextBlock Text, int X, int Y, int W, int H)>();
            foreach (var text in cache.Texts)
            {
                if (TopLevel.GetTopLevel(text) != window) continue;
                retained.Add(text); // Keep decisions through hidden frames and failed screen reads.
                if (!text.IsEffectivelyVisible || text.Bounds.Width < 1 || text.Bounds.Height < 1 ||
                    (string.IsNullOrWhiteSpace(text.Text) && (text.Inlines?.Count ?? 0) == 0)) continue;
                var start = text.PointToScreen(default); var end = text.PointToScreen(new Point(text.Bounds.Width, text.Bounds.Height));
                int w = Math.Abs(end.X - start.X), h = Math.Abs(end.Y - start.Y);
                if (w > 0 && h > 0) regions.Add((text, Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), w, h));
            }
            if (_failed.Contains(window) || regions.Count == 0) { group?.Gate.Pause(now); group?.Pause(now); continue; }
            nint handle = window.TryGetPlatformHandle()?.Handle ?? 0;
            if (handle == 0) { group?.Gate.Pause(now); group?.Pause(now); continue; }
            if (_windows.TryGetValue(window, out var registered) && registered.Handle != handle)
            {
                NativeCapture.SetWindowDisplayAffinity(registered.Handle, registered.Previous); _windows.Remove(window);
            }
            bool fresh = !_windows.ContainsKey(window);
            if (!NativeCapture.GetWindowDisplayAffinity(handle, out uint affinity)) { _failed.Add(window); continue; }
            if (fresh || affinity != 0x11)
            {
                if (!NativeCapture.SetWindowDisplayAffinity(handle, 0x11))
                {
                    logger.LogWarning("无法排除窗口，跳过采样。Win32={Error}", System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                    _failed.Add(window); continue;
                }
                if (fresh) _windows.Add(window, (handle, affinity));
                NativeCapture.DwmFlush();
                group?.Gate.Pause(now); group?.Pause(now);
                continue; // Never sample a frame in which exclusion is being re-established.
            }
            int left = regions.Min(r => r.X), top = regions.Min(r => r.Y);
            int right = regions.Max(r => r.X + r.W), bottom = regions.Max(r => r.Y + r.H);
            {
                // Quantize the footprint: clock digit widths should not move every fingerprint point.
                left = (int)Math.Floor(left / 64.0) * 64; top = (int)Math.Floor(top / 64.0) * 64;
                right = (int)Math.Ceiling(right / 64.0) * 64; bottom = (int)Math.Ceiling(bottom / 64.0) * 64;
            }
            _capture ??= new NativeCapture();
            var frame = _capture.Capture(left, top, right - left, bottom - top);
            if (frame == null) { group?.Gate.Pause(now); group?.Pause(now); continue; }
            captures++;
            bool settled = false;
            if (group != null && frame.Sample(left, top, right - left, bottom - top, pixels))
            {
                settled = group.Gate.Observe(pixels, now, settings.Value.ClassroomStableMs);
                if (group.Gate.Waiting) group.Pause(now);
            }
            if (group != null && !settled) continue; // Classroom scores text only once, after the background settles.
            foreach (var region in regions)
            {
                var text = region.Text;
                if (!frame.Sample(region.X, region.Y, region.W, region.H, pixels)) continue;
                sampled.Add(text);
                if (!_texts.TryGetValue(text, out var state)) _texts.Add(text, state = new TextState());
                state.RefreshLayers(text, now);
                for (int i = 0; i < 21; i++)
                {
                    double b = pixels[i * 4], g = pixels[i * 4 + 1], r = pixels[i * 4 + 2];
                    foreach (var layer in state.Layers)
                    {
                        double alpha = layer.Brush.Color.A / 255.0 * layer.Brush.Opacity * layer.Control.Opacity;
                        r = r * (1 - alpha) + layer.Brush.Color.R * alpha;
                        g = g * (1 - alpha) + layer.Brush.Color.G * alpha;
                        b = b * (1 - alpha) + layer.Brush.Color.B * alpha;
                    }
                    state.Luminances[i] = Contrast.Luminance((byte)r, (byte)g, (byte)b);
                }
                group!.AddScores(state.Luminances);
            }
            if (group != null && settled && group.GetBackgroundLuminance() is { } brightness)
            {
                bool white = Contrast.UseWhiteForLuminance(brightness);
                group.SetTarget(white, now, settings.Value.ClassroomFadeMs);
                if (group.Animating) _fadeTimer?.Start();
            }
        }
        foreach (var text in _texts.Keys.ToArray())
        {
            if (!retained.Contains(text)) { _texts[text].Dispose(); _texts.Remove(text); }

        }
        Status = _failed.Count > 0 ? "无法排除窗口，已暂停该窗口采样；请查看日志" : $"运行中 · {sampled.Count} 段文字 · 每轮 {captures} 次区域捕获";
        if (_failed.Count == 0) Status = _classrooms.Values.Any(g => g.Animating) ? "课堂稳定 · 正在统一缓慢变色" : _classrooms.Values.Any(g => g.Gate.Waiting) ? "课堂稳定 · 等待背景稳定" : "课堂稳定 · 背景稳定，保持颜色";
        if (live.Count == 0) Status = "等待主窗口显示";
    }
    private static ISolidColorBrush? Background(Control control) => control switch
    {
        Border b => b.Background as ISolidColorBrush, Panel p => p.Background as ISolidColorBrush,
        TextBlock t => t.Background as ISolidColorBrush,
        Avalonia.Controls.Primitives.TemplatedControl t => t.Background as ISolidColorBrush, _ => null
    };
    private sealed class TextState : IDisposable
    {
        public double[] Luminances { get; } = new double[21];
        public (ISolidColorBrush Brush, Control Control)[] Layers { get; private set; } = [];
        private long _layersAt = long.MinValue;
        public void RefreshLayers(TextBlock text, long now)
        {
            if (_layersAt != long.MinValue && now - _layersAt < 1000) return;
            _layersAt = now;
            Layers = text.GetVisualAncestors().OfType<Control>().Reverse().Append(text)
                .Select(c => (Brush: Background(c), Control: c)).Where(x => x.Brush != null)
                .Select(x => (x.Brush!, x.Control)).ToArray();
        }
        public void Dispose() { }
    }
}

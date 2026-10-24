using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using ClassIsland.Core.Abstractions.Controls;
namespace AdaptiveText;
// Timer ownership remains in the service: it runs only while an actual fade is active.
internal sealed class ClassroomGroup : IDisposable
{
    private readonly bool _glowEnabled;
    private readonly Dictionary<Visual, IDisposable> _glows = new();
    private readonly HashSet<Visual> _glowSeen = new();
    private readonly DropShadowEffect _glow = new() { Color = Colors.Black, BlurRadius = 3, OffsetX = 0, OffsetY = 0, Opacity = .85 };
    private Color _glowFrom, _glowTo;
    public ClassroomGroup() : this(false) { }
    public ClassroomGroup(bool glowEnabled) { _glowEnabled = glowEnabled; }
    public ClassroomBackgroundGate Gate { get; } = new();
    private readonly Dictionary<(AvaloniaObject, StyledProperty<IBrush?>), Paint> _paints = new();
    private readonly HashSet<(AvaloniaObject, StyledProperty<IBrush?>)> _seen = new();
    private readonly double[] _sums = new double[21];
    private int _scoreCount;
    private long _syncedAt = long.MinValue;
    private long _started;
    private int _duration;
    private Color? _target;
    public bool Animating { get; private set; }
    public bool? UseWhite { get; private set; }
    public void BeginScores() { Array.Clear(_sums); _scoreCount = 0; }
    public void AddScores(double[] luminances) { for (int i = 0; i < 21; i++) _sums[i] += luminances[i]; _scoreCount++; }
    public double? GetBackgroundLuminance()
    {
        if (_scoreCount == 0) return null;
        for (int i = 0; i < 21; i++) _sums[i] /= _scoreCount;
        return Contrast.BackgroundLuminance(_sums);
    }
    public void SetTarget(bool white, long now, int duration)
    {
        var target = white ? Colors.White : Colors.Black;
        if (_target == target && (Animating || _paints.Values.All(p => p.AtTarget(target)))) return;
        Tick(now); // A new transition starts from the currently displayed intermediate color.
        _glowFrom = _glow.Color; _glowTo = white ? Colors.Black : Colors.White;
        _target = target; UseWhite = white; _started = now; _duration = duration;
        foreach (var paint in _paints.Values) paint.Begin(target);
        Animating = _paints.Values.Any(p => !p.AtTarget(target));
        if (!Animating) { foreach (var paint in _paints.Values) paint.Tick(1); _glow.Color = _glowTo; }
    }
    public void Pause(long now) { Tick(now); Animating = false; }
    public void SyncTargets(Window window, long now)
    {
        if (_syncedAt != long.MinValue && now - _syncedAt < 1000) return;
        _syncedAt = now; _seen.Clear(); _glowSeen.Clear();
        foreach (var control in window.GetVisualDescendants().OfType<Control>())
        {
            if (!control.GetVisualAncestors().Any(v => v is ComponentBase)) continue;
            if (control is TextBlock text)
            {
                Add(text, TextBlock.ForegroundProperty, now);
                if (text.Inlines != null) foreach (var inline in text.Inlines) AddInline(inline, now);
            }
            // Built-in Lucide/Fluent/SF weather templates use foreground-aware IconElements.
            if (control.GetVisualAncestors().Any(v => IsWeather(v) || IsSeparator(v)) || IsWeather(control) || IsSeparator(control))
            {
                if (control is IconElement) Add(control, IconElement.ForegroundProperty, now);
                if (control is FAIconElement) Add(control, FAIconElement.ForegroundProperty, now);
                if (control is Shape shape)
                {
                    if (shape.Fill != null) Add(shape, Shape.FillProperty, now);
                    if (shape.Stroke != null) Add(shape, Shape.StrokeProperty, now);
                }
            }
        }
        foreach (var visual in _glows.Keys.Where(v => !_glowSeen.Contains(v)).ToArray()) { _glows[visual].Dispose(); _glows.Remove(visual); }
        foreach (var key in _paints.Keys.Where(k => !_seen.Contains(k)).ToArray()) { _paints[key].Dispose(); _paints.Remove(key); }
    }
    private static bool IsWeather(Visual visual) => visual.GetType().FullName is "ClassIsland.Controls.WeatherPackIconControl" or "ClassIsland.Controls.WeatherRangePackIconControl" or "ClassIsland.Controls.Components.WeatherComponent";
    private static bool IsSeparator(Visual visual) => visual.GetType().FullName == "ClassIsland.Controls.Components.SeparatorComponent";
    private void AddInline(Inline inline, long now)
    {
        Add(inline, TextElement.ForegroundProperty, now);
        if (inline is Span span) foreach (var child in span.Inlines) AddInline(child, now);
    }
    private void Add(AvaloniaObject owner, StyledProperty<IBrush?> property, long now)
    {
        if (_glowEnabled && owner is Visual visual)
        {
            _glowSeen.Add(visual);
            if (!_glows.ContainsKey(visual)) _glows.Add(visual, visual.Bind(Visual.EffectProperty, new EffectSource(_glow), BindingPriority.Animation));
        }
        var key = (owner, property); _seen.Add(key);
        if (_paints.ContainsKey(key)) return;
        var paint = new Paint(owner, property); _paints.Add(key, paint);
        if (_target is { } target)
        {
            // New clock/weather visuals join the current uniform color without restarting the group fade.
            paint.Begin(target);
            paint.Tick(Animating ? ClassroomTween.Progress(now, _started, _duration) : 1);
        }
    }
    public bool Tick(long now)
    {
        if (!Animating || _target == null) return false;
        double progress = ClassroomTween.Progress(now, _started, _duration);
        foreach (var paint in _paints.Values) paint.Tick(progress);
        _glow.Color = Color.FromRgb(ClassroomTween.Channel(_glowFrom.R, _glowTo.R, progress), ClassroomTween.Channel(_glowFrom.G, _glowTo.G, progress), ClassroomTween.Channel(_glowFrom.B, _glowTo.B, progress));
        if (progress >= 1) Animating = false;
        return Animating;
    }
    public void Dispose() { foreach (var paint in _paints.Values) paint.Dispose(); _paints.Clear(); foreach (var binding in _glows.Values) binding.Dispose(); _glows.Clear(); Animating = false; }
    private sealed class EffectSource(IEffect effect) : IObservable<IEffect?>
    {
        public IDisposable Subscribe(IObserver<IEffect?> observer) { observer.OnNext(effect); return new Empty(); }
        private sealed class Empty : IDisposable { public void Dispose() { } }
    }
    private sealed class Paint(AvaloniaObject owner, StyledProperty<IBrush?> property) : IDisposable
    {
        private readonly SolidColorBrush _brush = new((owner.GetValue(property) as ISolidColorBrush)?.Color ?? Colors.White);
        private IDisposable? _binding;
        private Color _from, _to;
        public bool AtTarget(Color color) => _brush.Color == color;
        public void Begin(Color target) { if (_binding == null && owner.GetValue(property) is ISolidColorBrush original) _brush.Color = original.Color; _from = _brush.Color; _to = target; }
        public void Tick(double progress)
        {
            _brush.Color = Color.FromArgb(ClassroomTween.Channel(_from.A, _to.A, progress), ClassroomTween.Channel(_from.R, _to.R, progress), ClassroomTween.Channel(_from.G, _to.G, progress), ClassroomTween.Channel(_from.B, _to.B, progress));
            _binding ??= owner.Bind(property, new Constant(_brush), BindingPriority.Animation);
        }
        public void Dispose() { _binding?.Dispose(); _binding = null; }
        private sealed class Constant(IBrush value) : IObservable<IBrush?>
        {
            public IDisposable Subscribe(IObserver<IBrush?> observer) { observer.OnNext(value); return new Empty(); }
            private sealed class Empty : IDisposable { public void Dispose() { } }
        }
    }
}








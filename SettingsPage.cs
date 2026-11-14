using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
namespace AdaptiveText;
[SettingsPageInfo("local.classisland.adaptive-text.settings", "墨白 · InkWhite")]
public sealed class SettingsPage : SettingsPageBase
{
    public SettingsPage(SettingsStore settings, AdaptiveService service)
    {
        var enabled = new CheckBox { Content = "启用自适应文字颜色", IsChecked = settings.Value.Enabled };
        var glow = new CheckBox { Content = "描边 / 柔光（默认开启）", IsChecked = settings.Value.Shadow };
        var preset = new ComboBox
        {
            ItemsSource = ColorPresets.All.Select(p => new ComboBoxItem { Content = p.Name, Tag = p.Value }).ToArray(),
            SelectedIndex = Math.Max(0, Array.FindIndex(ColorPresets.All, p => p.Value == settings.Value.Preset)),
            Width = 220, HorizontalAlignment = HorizontalAlignment.Left
        };
        var quiet = new NumericUpDown { Minimum = 0, Maximum = 30, Increment = .5m, Value = settings.Value.ClassroomStableMs / 1000m, Width = 150, HorizontalAlignment = HorizontalAlignment.Left };
        var fade = new NumericUpDown { Minimum = .5m, Maximum = 10, Increment = .5m, Value = settings.Value.ClassroomFadeMs / 1000m, Width = 150, HorizontalAlignment = HorizontalAlignment.Left };
        var interval = new NumericUpDown { Minimum = 1000, Maximum = 2000, Increment = 250, Value = settings.Value.IntervalMs, Width = 150, HorizontalAlignment = HorizontalAlignment.Left };
        var status = new TextBlock { Text = service.Status, TextWrapping = TextWrapping.Wrap };
        var result = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var save = new Button { Content = "保存并立即应用" };
        save.Click += (_, _) =>
        {
            try
            {
                settings.Value.Enabled = enabled.IsChecked == true; settings.Value.Shadow = glow.IsChecked == true;
                settings.Value.Preset = preset.SelectedItem is ComboBoxItem selected && selected.Tag is string presetValue ? ColorPresets.Normalize(presetValue) : ColorPresets.Balanced;
                settings.Value.ClassroomStableMs = (int)((quiet.Value ?? 3) * 1000);
                settings.Value.ClassroomFadeMs = (int)((fade.Value ?? 2) * 1000);
                settings.Value.IntervalMs = (int)(interval.Value ?? 1000);
                settings.Save(); result.Text = "已保存";
            }
            catch (Exception e) { result.Text = "保存失败：" + e.Message; }
        };
        Content = new ScrollViewer { Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 14,
            Children =
            {
                new TextBlock { Text = "适用版本：仅限 ClassIsland 2.1.0.1", FontSize = 16, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "本插件代码由 GLM 生成。其他 ClassIsland 版本需另行适配。", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "背景稳定后，通知遮罩、通知正文、文字、天气图标与分割线缓慢统一变色；背景不变时保持颜色。", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "严格黑白：只在足够深的背景上用纯白字，中灰、浅灰与浅色背景用纯黑。渐变仅在切换过程中出现。", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "检测结合元素复杂度：复杂背景优先参考主导元素，避免背景文字干扰；低开销分桶统计，不额外截屏。", TextWrapping = TextWrapping.Wrap },
                enabled, glow,
                new TextBlock { Text = "柔光只作用于文字和图标；遇到玻璃材质或轮播容器会自动避让，避免抹平主题材质或造成边缘发光不一致。", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "颜色预设" }, preset,
                new TextBlock { Text = "黑白均衡沿用标准判断；黑色为主只在更深背景用白字；白色为主在稍暗背景也用白字。", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "等待背景稳定（秒；0 = 不等待，立即跟随背景）" }, quiet,
                new TextBlock { Text = "统一渐变时长（秒）" }, fade,
                new TextBlock { Text = "检测间隔（毫秒；默认 1000）" }, interval,
                save, result, status,
                new TextBlock { Text = "启用期间主窗口在部分截图、录屏和屏幕共享中不可见，以采到真实背景；停用后恢复。支持 Windows 10 2004+ / Windows 11。", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "支持组件文字、富文本、通知遮罩/正文、内置天气字体/矢量图标与分割线。自绘文字、独立桌面小组件和自定义位图天气图标需另行适配。", TextWrapping = TextWrapping.Wrap },
                new Separator(),
                new TextBlock { Text = "作者：苏泊林 / subolin", FontWeight = FontWeight.SemiBold },
                new HyperlinkButton { Content = "GitHub：github.com/subolin0000/ClassIsland-InkWhite", NavigateUri = new Uri("https://github.com/subolin0000/ClassIsland-InkWhite"), HorizontalAlignment = HorizontalAlignment.Left },
                new SelectableTextBlock { Text = "QQ：1570130637\n邮箱：subolinChina@outlook.com", TextWrapping = TextWrapping.Wrap }
            }
        }};
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => status.Text = service.Status;
        AttachedToVisualTree += (_, _) => timer.Start(); DetachedFromVisualTree += (_, _) => timer.Stop();
        if (settings.LoadError != null) result.Text = "配置读取失败，已使用默认值：" + settings.LoadError;
    }
}



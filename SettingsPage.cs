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
        var glow = new CheckBox { Content = "描边 / 柔光（默认关闭）", IsChecked = settings.Value.Shadow };
        var quiet = new NumericUpDown { Minimum = 1.5m, Maximum = 30, Increment = .5m, Value = settings.Value.ClassroomStableMs / 1000m, Width = 150, HorizontalAlignment = HorizontalAlignment.Left };
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
                new TextBlock { Text = "本插件代码由 GPT 生成。其他 ClassIsland 版本需另行适配。", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "背景稳定后，文字、天气图标与分割线缓慢统一变色；背景不变时保持颜色。", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "严格黑白：只在足够深的背景上用纯白字，中灰、浅灰与浅色背景用纯黑。渐变仅在切换过程中出现。", TextWrapping = TextWrapping.Wrap },
                enabled, glow,
                new TextBlock { Text = "柔光使用反色软边，开启会增加绘制开销；关闭更适合低配置电脑。", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "等待背景稳定（秒）" }, quiet,
                new TextBlock { Text = "统一渐变时长（秒）" }, fade,
                new TextBlock { Text = "检测间隔（毫秒；默认 1000）" }, interval,
                save, result, status,
                new TextBlock { Text = "启用期间主窗口在部分截图、录屏和屏幕共享中不可见，以采到真实背景；停用后恢复。支持 Windows 10 2004+ / Windows 11。", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "支持组件文字、富文本、内置天气字体/矢量图标与分割线。自绘文字、独立桌面小组件和自定义位图天气图标需另行适配。", TextWrapping = TextWrapping.Wrap },
                new Separator(),
                new TextBlock { Text = "作者：苏泊林 / subolin", FontWeight = FontWeight.SemiBold },
                new SelectableTextBlock { Text = "QQ：1570130637\n邮箱：subolinChina@outlook.com", TextWrapping = TextWrapping.Wrap }
            }
        }};
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => status.Text = service.Status;
        AttachedToVisualTree += (_, _) => timer.Start(); DetachedFromVisualTree += (_, _) => timer.Stop();
        if (settings.LoadError != null) result.Text = "配置读取失败，已使用默认值：" + settings.LoadError;
    }
}



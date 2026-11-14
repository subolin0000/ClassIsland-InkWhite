using System.Reflection;
using System.Runtime.Loader;
using Avalonia.Styling;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Plugin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
static void Check(bool pass,string name) { if(!pass) throw new Exception("FAIL: "+name); Console.WriteLine("PASS: "+name); }
Console.WriteLine("Runtime: "+System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
Check(Environment.Version.Major==8,"process actually runs on .NET 8");
Check(typeof(TextBlock).Assembly.GetName().Version!.Major==11,"Avalonia 11 host libraries");
var lifetime=DispatchProxy.Create<IClassicDesktopStyleApplicationLifetime,LifetimeProxy>();
AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithLifetime(lifetime);
var pluginPath=Path.GetFullPath(args[0]);
var asm=AssemblyLoadContext.Default.LoadFromAssemblyPath(pluginPath);
Console.WriteLine("Plugin: "+asm.GetName().FullName);
if(args.Contains("--legacy"))
{
    try {
        AvaloniaRuntimeXamlLoader.Load("<Styles xmlns=\"https://github.com/avaloniaui\"><Style Selector=\"TextBlock\"><Setter Property=\"Foreground\" Value=\"Red\"/></Style></Styles>");
        throw new Exception("Legacy problem was not reproduced");
    }
    catch(Exception e) when(e.ToString().Contains("System.Runtime, Version=10.0.0.0"))
    { Console.WriteLine("PASS: reproduced original System.Runtime 10 theme-loading failure on .NET 8"); return; }
}
Check(asm.GetReferencedAssemblies().Single(n=>n.Name=="System.Runtime").Version!.Major==8,"plugin targets System.Runtime 8");
foreach(var reference in asm.GetReferencedAssemblies()) Console.WriteLine(reference.FullName);
Check(!asm.GetReferencedAssemblies().Any(n=>n.Name is "System.Runtime" or "System.Private.CoreLib" && n.Version!.Major>8),"no newer core runtime references");
Assembly? hostAssembly=null;
if(args.Length>1 && !args[1].StartsWith("--"))
{
    var hostDirectory=Path.GetFullPath(args[1]);
    AssemblyLoadContext.Default.Resolving += (_, name) =>
    {
        var file=Path.Combine(hostDirectory,name.Name+".dll");
        return File.Exists(file)?AssemblyLoadContext.Default.LoadFromAssemblyPath(file):null;
    };
    foreach(var reference in asm.GetReferencedAssemblies())
    {
        var file=Path.Combine(hostDirectory,reference.Name+".dll");
        if(File.Exists(file)) Check(AssemblyName.GetAssemblyName(file).Version>=reference.Version,"official 2.1.0.1 supplies "+reference.Name);
    }
    hostAssembly=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(hostDirectory,"ClassIsland.dll"));
    Check(hostAssembly.GetName().Version==new Version(2,1,0,1),"official ClassIsland 2.1.0.1 host assembly loaded");
}
var entrance=asm.ExportedTypes.Single(t=>t.GetCustomAttributes().Any(a=>a is PluginEntrance));
var plugin=(PluginBase)Activator.CreateInstance(entrance)!;
typeof(PluginBase).GetProperty("PluginConfigFolder")!.SetValue(plugin,Path.Combine(Environment.CurrentDirectory,"work","stable-test-config"));
var services=new ServiceCollection(); services.AddLogging();
plugin.Initialize(new HostBuilderContext(new Dictionary<object,object>()),services);
using var provider=services.BuildServiceProvider();
Check(provider.GetServices<IHostedService>().Count()==1,"stable host resolves plugin hosted service");
var settingsType=asm.GetType("AdaptiveText.SettingsPage")!;
Check(settingsType.GetCustomAttributes().Any(a=>a is SettingsPageInfo),"settings entry metadata exists");
var page=(Control)ActivatorUtilities.CreateInstance(provider,settingsType);
Check(page!=null,"settings page and all DI dependencies construct under stable host");
var manifest=new DeserializerBuilder().IgnoreUnmatchedProperties().WithNamingConvention(CamelCaseNamingConvention.Instance).Build().Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(pluginPath)!,"manifest.yml")));
Check((string)manifest["apiVersion"]=="2.1.0.1" && (string)manifest["entranceAssembly"]=="AdaptiveText.dll","stable manifest matches host and assembly");
foreach(var theme in new[]{"<Styles xmlns=\"https://github.com/avaloniaui\"><Style Selector=\"TextBlock\"><Setter Property=\"Foreground\" Value=\"Red\"/></Style></Styles>","<Styles xmlns=\"https://github.com/avaloniaui\"><Style Selector=\"Border\"><Setter Property=\"Background\" Value=\"#223344\"/></Style></Styles>"})
    Check(AvaloniaRuntimeXamlLoader.Load(theme,hostAssembly) is Styles,"runtime XAML theme loads after plugin initialization");
var presetBox=page!.GetLogicalDescendants().OfType<ComboBox>().Single();
var presetItems=presetBox.Items.OfType<ComboBoxItem>().ToArray();
Check(presetItems.Length==3 && presetItems.Select(i=>i.Content?.ToString()).SequenceEqual(new[]{"黑白均衡","黑色为主","白色为主"}),"settings page exposes exactly the three requested presets");
Check(presetItems.Select(i=>(string?)i.Tag).SequenceEqual(new[]{"Balanced","Black","White"}),"preset option values match the stable color algorithms");
Check(((ComboBoxItem?)presetBox.SelectedItem)?.Tag as string=="Balanced","settings page defaults to the balanced preset");
var glowSwitch=page!.GetLogicalDescendants().OfType<CheckBox>().Single(c=>c.Content?.ToString()?.StartsWith("描边")==true);
Check(glowSwitch.IsChecked==true,"independent glow switch defaults on");
var quietInput=page!.GetLogicalDescendants().OfType<NumericUpDown>().Single(n=>n.Minimum==0 && n.Maximum==30);
Check(quietInput.Value==3,"quiet time allows zero while preserving the three-second default");
var githubLink=page!.GetLogicalDescendants().OfType<HyperlinkButton>().Single();
Check(githubLink.NavigateUri?.AbsoluteUri=="https://github.com/subolin0000/ClassIsland-InkWhite","settings page links to the official GitHub repository");
var store=provider.GetRequiredService(asm.GetType("AdaptiveText.SettingsStore")!);
var options=store.GetType().GetProperty("Value")!.GetValue(store)!;
store.GetType().GetMethod("Save")!.Invoke(store,null);
Check(File.Exists(Path.Combine(Environment.CurrentDirectory,"work","stable-test-config","settings.json")),"classroom settings save normally");
var text=new TextBlock{Text="Test",Foreground=Brushes.Blue,Effect=new BlurEffect{Radius=1}};
var originalEffect=text.Effect;
var fakeWindow=new ClassIsland.MainWindow { Width=300, Height=80, Content=new FakeComponent { Content=text } };
lifetime.MainWindow=fakeWindow; fakeWindow.Show(); Dispatcher.UIThread.RunJobs();
((LifetimeProxy)(object)lifetime).Windows.Add(fakeWindow);
options.GetType().GetProperty("Enabled")!.SetValue(options,true);
var service=provider.GetServices<IHostedService>().Single();
var run=new Avalonia.Controls.Documents.Run("rich") { Foreground=Brushes.Red };
var rich=new TextBlock(); rich.Inlines!.Add(run);
var weatherIcon=new FluentAvalonia.UI.Controls.FontIcon { Glyph="x", Foreground=Brushes.Orange };
Console.WriteLine($"Icon base={weatherIcon.GetType().BaseType!.FullName}; foreground owner={FluentAvalonia.UI.Controls.FontIcon.ForegroundProperty.OwnerType}");
var weatherPath=new Avalonia.Controls.Shapes.Path { Fill=Brushes.Cyan, Stroke=Brushes.Green };
var weatherPanel=new StackPanel(); weatherPanel.Children.Add(weatherIcon); weatherPanel.Children.Add(weatherPath);
var weather=new ClassIsland.Controls.WeatherPackIconControl { Content=weatherPanel };
((FakeComponent)fakeWindow.Content!).Content=null; Dispatcher.UIThread.RunJobs();
Application.Current!.Resources["TextFillColorSecondaryBrush"]=Brushes.Gray;
var separator=(Control)Activator.CreateInstance(hostAssembly!.GetType("ClassIsland.Controls.Components.SeparatorComponent")!)!;
var notificationHost=new ClassIsland.Controls.MainWindowLine();
var notificationBorder=new Border();
notificationBorder.SetValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty,Brushes.Yellow);
var notificationText=new TextBlock { Text="Mask" };
notificationBorder.Child=notificationText; notificationHost.Content=notificationBorder;
var rainyIcon=new FluentAvalonia.UI.Controls.FontIcon { Glyph="r", Foreground=Brushes.Orange };
var rainyWeather=new ClassIsland.Controls.WeatherPackIconControl();
SetPseudoClass(rainyWeather,":rainy",true); rainyWeather.Content=rainyIcon;
var panel=new StackPanel(); panel.Children.Add(separator); panel.Children.Add(text); panel.Children.Add(rich); panel.Children.Add(weather); panel.Children.Add(notificationHost); panel.Children.Add(rainyWeather);
fakeWindow.Content=new FakeComponent { Content=panel }; fakeWindow.UpdateLayout(); Dispatcher.UIThread.RunJobs();
var separatorLine=separator.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Line>().Single();
Check(((ISolidColorBrush)separatorLine.Stroke!).Color==Colors.Gray,"actual official separator starts with dynamic theme stroke");
var originalThickness=separatorLine.StrokeThickness; var originalOpacity=separatorLine.Opacity;
var groupType=asm.GetType("AdaptiveText.ClassroomGroup")!;
var group=Activator.CreateInstance(groupType,true)!;
groupType.GetMethod("SyncTargets")!.Invoke(group,[fakeWindow,10000L]);
groupType.GetMethod("SetTarget")!.Invoke(group,[false,10000L,2000]);
Check(((ISolidColorBrush)weatherIcon.Foreground!).Color==Colors.Orange,"classroom does not abruptly recolor weather at fade start");
groupType.GetMethod("Tick")!.Invoke(group,[11000L]);
var mid=((ISolidColorBrush)weatherIcon.Foreground!).Color; Console.WriteLine($"Classroom targets={((System.Collections.IDictionary)groupType.GetField("_paints",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(group)!).Count}, icon color={mid}");
Check(mid.R>0 && mid.R<Colors.Orange.R,"weather icon uses smooth intermediate color");
var separatorMid=((ISolidColorBrush)separatorLine.Stroke!).Color;
Check(separatorMid!=Colors.Gray && separatorMid!=Colors.Black,"official separator shares intermediate fade timing");
groupType.GetMethod("Tick")!.Invoke(group,[12000L]);
Check(((ISolidColorBrush)text.Foreground!).Color==Colors.Black && ((ISolidColorBrush)run.Foreground!).Color==Colors.Black,"classroom unifies plain and explicit rich text");
Check(((ISolidColorBrush)weatherIcon.Foreground!).Color==Colors.Black,"weather font icon reaches same unified target");
Check(((ISolidColorBrush)separatorLine.Stroke!).Color==Colors.Black,"official separator reaches black with text and weather");
Check(separatorLine.StrokeThickness==originalThickness && separatorLine.Opacity==originalOpacity,"separator width and opacity remain unchanged");
Check(((ISolidColorBrush)weatherPath.Fill!).Color==Colors.Black && ((ISolidColorBrush)weatherPath.Stroke!).Color==Colors.Black,"weather vector fill and stroke reach unified target");
Check(((ISolidColorBrush)notificationText.Foreground!).Color==Colors.Black && ((ISolidColorBrush)notificationBorder.GetValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty)!).Color==Colors.Yellow,"notification text recolors without repainting glass/material borders");
Check(((ISolidColorBrush)rainyIcon.Foreground!).Color==Colors.Orange,"rainy weather keeps ClassIsland's semantic accent color");
Check(!(bool)groupType.GetProperty("Animating")!.GetValue(group)!,"fade stops instead of continuously repainting static classroom");
groupType.GetMethod("SetTarget")!.Invoke(group,[true,13000L,2000]); groupType.GetMethod("Tick")!.Invoke(group,[14000L]);
groupType.GetMethod("Pause")!.Invoke(group,[14000L]); var paused=((ISolidColorBrush)text.Foreground!).Color;
groupType.GetMethod("Tick")!.Invoke(group,[14500L]);
Check(((ISolidColorBrush)text.Foreground!).Color==paused,"new changing background can pause an ongoing fade");
groupType.GetMethod("SetTarget")!.Invoke(group,[true,15000L,2000]); groupType.GetMethod("Tick")!.Invoke(group,[17000L]);
Check(((ISolidColorBrush)weatherIcon.Foreground!).Color==Colors.White,"paused fade resumes toward settled unified target");
groupType.GetMethod("SetTarget")!.Invoke(group,[true,20000L,2000]);
Check(!(bool)groupType.GetProperty("Animating")!.GetValue(group)!,"same settled color never restarts animation");
Check(((ISolidColorBrush)separatorLine.Stroke!).Color==Colors.White,"official separator also reaches white");
Application.Current.Resources["TextFillColorSecondaryBrush"]=Brushes.Salmon;
((IDisposable)group).Dispose();
Check(((ISolidColorBrush)separatorLine.Stroke!).Color==Colors.Salmon,"separator restores latest dynamic theme stroke after preset stops");
Check(((ISolidColorBrush)text.Foreground!).Color==Colors.Blue && ((ISolidColorBrush)run.Foreground!).Color==Colors.Red,"leaving classroom preset restores plain and rich text");
Check(((ISolidColorBrush)weatherIcon.Foreground!).Color==Colors.Orange && ((ISolidColorBrush)weatherPath.Fill!).Color==Colors.Cyan,"leaving classroom preset restores original weather colors");
Check(((ISolidColorBrush)notificationText.Foreground!).Color==Colors.Yellow && ((ISolidColorBrush)notificationBorder.GetValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty)!).Color==Colors.Yellow,"notification mask restores its on-accent foreground without changing host border");
SetPseudoClass(rainyWeather,":rainy",false); Dispatcher.UIThread.RunJobs();
var rainToggleGroup=Activator.CreateInstance(groupType)!;
groupType.GetMethod("SyncTargets")!.Invoke(rainToggleGroup,[fakeWindow,25000L]);
groupType.GetMethod("SetTarget")!.Invoke(rainToggleGroup,[false,25000L,2000]); groupType.GetMethod("Tick")!.Invoke(rainToggleGroup,[27000L]);
Check(((ISolidColorBrush)rainyIcon.Foreground!).Color==Colors.Black,"non-rainy weather rejoins the unified black/white target");
((IDisposable)rainToggleGroup).Dispose();
Check(((ISolidColorBrush)rainyIcon.Foreground!).Color==Colors.Orange,"non-rainy weather toggle group restores the original icon brush");

// A real theme switch must remove plugin bindings so ClassIsland can apply its latest theme brushes immediately.
var themeText=new TextBlock{Text="Theme test",Foreground=Brushes.Blue,Effect=new BlurEffect{Radius=1}};
var originalThemeEffect=themeText.Effect;
fakeWindow.Content=new FakeComponent { Content=themeText }; fakeWindow.UpdateLayout(); Dispatcher.UIThread.RunJobs();
var updateMethod=service.GetType().GetMethod("Update",BindingFlags.NonPublic|BindingFlags.Instance)!;
updateMethod.Invoke(service,null);
var serviceClassrooms=(System.Collections.IDictionary)service.GetType().GetField("_classrooms",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(service)!;
Check(serviceClassrooms.Count==1,"service has an active classroom group before switching themes");
var themeBefore=Application.Current!.ActualThemeVariant;
Check(themeBefore!=null,"theme variant is observable before switching themes");
var requestedTheme=Equals(themeBefore,ThemeVariant.Dark)?ThemeVariant.Light:ThemeVariant.Dark;
Application.Current.RequestedThemeVariant=requestedTheme; Dispatcher.UIThread.RunJobs();
var themeAfter=Application.Current.ActualThemeVariant;
Console.WriteLine($"Theme switch probe: before={themeBefore}, after={themeAfter}, requested={requestedTheme}");
Check(themeAfter!=themeBefore,"opposite requested theme becomes the actual theme in the integration host");
updateMethod.Invoke(service,null);
Check(serviceClassrooms.Count==0,"theme switch clears active classroom bindings");
Check(((ISolidColorBrush)themeText.Foreground!).Color==Colors.Blue,"theme switch restores the latest host text brush");
Check(((string)service.GetType().GetProperty("Status")!.GetValue(service)!).Contains("主题"),"service reports the theme-switch recovery state");
Application.Current.RequestedThemeVariant=themeBefore; Dispatcher.UIThread.RunJobs();

fakeWindow.Content=null; Dispatcher.UIThread.RunJobs();
var effectText=new TextBlock{Text="Effect",Foreground=Brushes.Blue,Effect=new BlurEffect{Radius=1}};
var originalGlowHostEffect=effectText.Effect;
var plainGlowText=new TextBlock{Text="Glow",Foreground=Brushes.Blue};
var glowPanel=new StackPanel(); glowPanel.Children.Add(effectText); glowPanel.Children.Add(plainGlowText);
fakeWindow.Content=new FakeComponent { Content=glowPanel }; fakeWindow.UpdateLayout(); Dispatcher.UIThread.RunJobs();
var glowGroup=Activator.CreateInstance(groupType,new object[]{true})!;
groupType.GetMethod("SyncTargets")!.Invoke(glowGroup,[fakeWindow,30000L]);
Check(ReferenceEquals(effectText.Effect,originalGlowHostEffect) && plainGlowText.Effect==null,"startup does not attach a glow before the first color target");
groupType.GetMethod("SetTarget")!.Invoke(glowGroup,[false,30000L,2000]);
Check(ReferenceEquals(effectText.Effect,originalGlowHostEffect) && plainGlowText.Effect is DropShadowEffect,"host effects are preserved while plain text receives glow");
Check(((DropShadowEffect)plainGlowText.Effect!).Color==Colors.White,"black text target starts with an immediately opposite white glow");
groupType.GetMethod("Tick")!.Invoke(glowGroup,[32000L]);
Check(((DropShadowEffect)plainGlowText.Effect!).Color==Colors.White,"black target retains its white glow during fade completion");
groupType.GetMethod("SetTarget")!.Invoke(glowGroup,[true,33000L,2000]); groupType.GetMethod("Tick")!.Invoke(glowGroup,[35000L]);
Check(((DropShadowEffect)plainGlowText.Effect!).Color==Colors.Black,"white target receives black glow");
((IDisposable)glowGroup).Dispose();
Check(ReferenceEquals(effectText.Effect,originalGlowHostEffect) && plainGlowText.Effect==null,"turning glow off restores original effects");

fakeWindow.Content=null; Dispatcher.UIThread.RunJobs();
var glassText=new TextBlock{Text="Glass",Foreground=Brushes.Blue};
var glassBorder=new Border{BoxShadow=BoxShadows.Parse("0 2 16 0 #30000000"),Child=glassText};
var originalGlassShadow=glassBorder.BoxShadow.ToString();
fakeWindow.Content=new FakeComponent{Content=glassBorder}; fakeWindow.UpdateLayout(); Dispatcher.UIThread.RunJobs();
var glassGroup=Activator.CreateInstance(groupType,true)!;
groupType.GetMethod("SyncTargets")!.Invoke(glassGroup,[fakeWindow,40000L]);
groupType.GetMethod("SetTarget")!.Invoke(glassGroup,[false,40000L,2000]); groupType.GetMethod("Tick")!.Invoke(glassGroup,[42000L]);
Check(glassBorder.Effect==null && glassText.Effect==null,"glass material suppresses per-control glow");
Check(glassBorder.BoxShadow.ToString()==originalGlassShadow,"glass material border stays unchanged");
Check(((ISolidColorBrush)glassText.Foreground!).Color==Colors.Black,"text coloring still works under glass material");
((IDisposable)glassGroup).Dispose();

var slideText=new TextBlock{Text="Slide",Foreground=Brushes.Blue};
var slide=new ClassIsland.Controls.Components.SlideComponent{Content=slideText};
fakeWindow.Content=new FakeComponent{Content=slide}; fakeWindow.UpdateLayout(); Dispatcher.UIThread.RunJobs();
var slideGroup=Activator.CreateInstance(groupType,true)!;
groupType.GetMethod("SyncTargets")!.Invoke(slideGroup,[fakeWindow,50000L]);
groupType.GetMethod("SetTarget")!.Invoke(slideGroup,[false,50000L,2000]); groupType.GetMethod("Tick")!.Invoke(slideGroup,[52000L]);
Check(slideText.Effect==null,"carousel content keeps uniform edges without per-child glow");
Check(((ISolidColorBrush)slideText.Foreground!).Color==Colors.Black,"carousel text still receives unified color");
((IDisposable)slideGroup).Dispose();
Check(AvaloniaRuntimeXamlLoader.Load("<Styles xmlns=\"https://github.com/avaloniaui\"/>") is Styles,"theme reload still succeeds after classroom and glow cleanup");
fakeWindow.Close();
Console.WriteLine("All stable compatibility checks passed.");

static void SetPseudoClass(Control control,string name,bool value)
{
    var property=typeof(StyledElement).GetProperty("PseudoClasses",BindingFlags.NonPublic|BindingFlags.Instance)!;
    ((IPseudoClasses)property.GetValue(control)!).Set(name,value);
}

public class LifetimeProxy : DispatchProxy
{
    public List<Window> Windows { get; } = new();
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
    {
        "get_Windows" => Windows,
        "get_MainWindow" => Windows.FirstOrDefault(),
        _ => method?.ReturnType == typeof(void) ? null : method?.ReturnType.IsValueType == true ? Activator.CreateInstance(method.ReturnType) : null
    };
}
public sealed class FakeComponent : ClassIsland.Core.Abstractions.Controls.ComponentBase { }
namespace ClassIsland { public sealed class MainWindow : Window { } }

namespace ClassIsland.Controls { public sealed class MainWindowLine : ContentControl { } }










namespace ClassIsland.Controls { public sealed class WeatherPackIconControl : UserControl { } }

namespace ClassIsland.Controls.Components { public sealed class SlideComponent : ContentControl { } }








using System.Reflection;
using System.Runtime.Loader;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
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
Check(!page!.GetLogicalDescendants().OfType<ComboBox>().Any(),"removed realtime preset selector from settings");
var glowSwitch=page!.GetLogicalDescendants().OfType<CheckBox>().Single(c=>c.Content?.ToString()?.StartsWith("描边")==true);
Check(glowSwitch.IsChecked==false,"independent glow switch defaults off");
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
var panel=new StackPanel(); panel.Children.Add(separator); panel.Children.Add(text); panel.Children.Add(rich); panel.Children.Add(weather);
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
var glowGroup=Activator.CreateInstance(groupType,new object[]{true})!;
groupType.GetMethod("SyncTargets")!.Invoke(glowGroup,[fakeWindow,30000L]);
Check(text.Effect is DropShadowEffect && weatherIcon.Effect is DropShadowEffect && separatorLine.Effect is DropShadowEffect,"optional glow covers text weather and separator");
Check(ReferenceEquals(text.Effect,weatherIcon.Effect),"optional glow shares one effect resource");
groupType.GetMethod("SetTarget")!.Invoke(glowGroup,[false,30000L,2000]); groupType.GetMethod("Tick")!.Invoke(glowGroup,[32000L]);
Check(((DropShadowEffect)text.Effect!).Color==Colors.White,"black target receives white glow");
groupType.GetMethod("SetTarget")!.Invoke(glowGroup,[true,33000L,2000]); groupType.GetMethod("Tick")!.Invoke(glowGroup,[35000L]);
Check(((DropShadowEffect)text.Effect!).Color==Colors.Black,"white target receives black glow");
((IDisposable)glowGroup).Dispose();
Check(ReferenceEquals(text.Effect,originalEffect) && weatherIcon.Effect==null,"turning glow off restores original effects");
Check(AvaloniaRuntimeXamlLoader.Load("<Styles xmlns=\"https://github.com/avaloniaui\"/>") is Styles,"theme reload still succeeds after classroom and glow cleanup");
fakeWindow.Close();
Console.WriteLine("All stable compatibility checks passed.");

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










namespace ClassIsland.Controls { public sealed class WeatherPackIconControl : UserControl { } }








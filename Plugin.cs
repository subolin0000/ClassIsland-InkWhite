using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace AdaptiveText;
[PluginEntrance]
public sealed class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        services.AddSingleton(new SettingsStore(Path.Combine(PluginConfigFolder, "settings.json")));
        services.AddSingleton<AdaptiveService>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<AdaptiveService>());
        services.AddSettingsPage<SettingsPage>();
    }
}

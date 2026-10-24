using AdaptiveText;
using System.Runtime.InteropServices;

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
}
Check(Math.Abs(Contrast.Luminance(255,255,255)-1)<1e-9, "white luminance");
Check(Contrast.Luminance(0,0,0)==0, "black luminance");
for(int gray=0;gray<=255;gray++)
    if(Contrast.UseWhiteForLuminance(Contrast.Luminance((byte)gray,(byte)gray,(byte)gray))!=(gray<96)) throw new Exception("Gray boundary failure: "+gray);
Check(true,"all 256 neutral grays obey one explicit boundary: below 96 white, 96+ black");
Check(!Contrast.UseWhiteForLuminance(Contrast.Luminance(128,128,128)) && !Contrast.UseWhiteForLuminance(Contrast.Luminance(192,192,192)),"medium gray and light gray always select black");
Check(Contrast.UseWhiteForLuminance(Contrast.Luminance(64,64,64)),"sufficiently dark background selects white");
Check(!Contrast.UseWhiteForLuminance(Contrast.BackgroundLuminance([.4,.4,.4,.01,.4])),"a small dark detail cannot turn a mostly light background white");
var nearDark=Enumerable.Repeat((byte)95,84).ToArray(); var nearLight=Enumerable.Repeat((byte)96,84).ToArray();
Check(ClassroomBackgroundGate.Changed(nearLight,nearDark),"crossing the brightness boundary is detected even for a one-level change");
var synthetic=new byte[NativeCapture.MaxWidth*6*4];
for(int y=0;y<6;y++) for(int x=0;x<7;x++) for(int c=0;c<3;c++) synthetic[(y*NativeCapture.MaxWidth+x)*4+c]=255;
var shared=new NativeCapture.Frame(synthetic,-100,-50,140,60,14,6);
var samples=new byte[84];
Check(shared.Sample(-100,-50,70,60,samples) && samples[0]==255 && samples[80]==255,"shared capture maps left text on negative-coordinate monitor");
Check(shared.Sample(-30,-50,70,60,samples) && samples[0]==0 && samples[80]==0,"shared capture maps right text independently");
Check(shared.Sample(-150,-50,60,60,samples) && samples[0]==255,"partially clipped text samples only visible frame");
Check(!shared.Sample(100,-50,20,20,samples),"text outside shared capture is skipped");
var classroomBright=Enumerable.Repeat((byte)255,84).ToArray(); var darkPixels=new byte[84];
var gate=new ClassroomBackgroundGate();
Check(!gate.Observe(classroomBright,0,3000),"classroom waits on first sample");
gate.Observe(darkPixels,1000,3000); gate.Observe(classroomBright,2000,3000);
Check(!gate.Observe(classroomBright,4000,3000),"changing apps restarts classroom quiet time");
Check(gate.Observe(classroomBright,5000,3000),"background commits only after 3 observed stable seconds");
int idleCommits=0;
for(long now=6000;now<=1800000;now+=1000) if(gate.Observe(classroomBright,now,3000)) idleCommits++;
Check(idleCommits==0,"half-hour static classroom background never requests another transition");
Check(!gate.Observe(darkPixels,1801000,3000) && gate.Waiting,"new background returns to waiting");
gate.Observe(darkPixels,1802000,3000); gate.Observe(darkPixels,1803000,3000);
Check(gate.Observe(darkPixels,1804000,3000),"new stable background produces one new request");
var slow=new ClassroomBackgroundGate(); slow.Observe(darkPixels,0,3000); int early=0;
for(int second=1;second<=10;second++) if(slow.Observe(Enumerable.Repeat((byte)(second*8),84).ToArray(),second*1000,3000)) early++;
Check(early==0,"gradual background drift cannot masquerade as settled");
Check(ClassroomTween.Channel(0,255,0)==0 && ClassroomTween.Channel(0,255,1)==255 && ClassroomTween.Channel(0,255,.5)==128,"fade endpoints and intermediate gray are correct");
Check(ClassroomTween.Progress(2000,1000,2000)==.5,"fade uses elapsed time rather than frame count");
var path=Path.Combine(Path.GetTempPath(),"AdaptiveText-test-"+Guid.NewGuid(),"settings.json");
try
{
    var store=new SettingsStore(path); store.Value.IntervalMs=1; store.Value.Enabled=false; store.Save();
    var reloaded=new SettingsStore(path);
    Check(!reloaded.Value.Enabled && reloaded.Value.IntervalMs==1000,"atomic settings round trip and bounds");
    Check(!reloaded.Value.Shadow,"glow defaults off");
    File.WriteAllText(path,"{\"Preset\":\"Realtime\",\"Shadow\":true,\"IntervalMs\":250}");
    var migrated=new SettingsStore(path);
    Check(!migrated.Value.Shadow && migrated.Value.IntervalMs==1000,"legacy realtime config migrates to classroom with glow off");
    migrated.Value.Shadow=true; migrated.Save();
    Check(new SettingsStore(path).Value.Shadow,"explicitly enabling independent glow persists");
    File.WriteAllText(path,"broken json");
    Check(new SettingsStore(path).LoadError!=null,"corrupt settings fallback");
}
finally { if(File.Exists(path)) File.Delete(path); Directory.Delete(Path.GetDirectoryName(path)!); }
if(args.Contains("--capture"))
{
    if(!OperatingSystem.IsWindowsVersionAtLeast(10,0,19041)) throw new Exception("Unsupported Windows");
    Probe.SetProcessDpiAwarenessContext(-4); nint back=0,front=0;
    try
    {
        back=Probe.CreateWindowEx(0x08000088,"STATIC","AdaptiveText test background",0x90000006,100,100,180,80,0,0,0,0);
        front=Probe.CreateWindowEx(0x08000088,"STATIC","AdaptiveText test foreground",0x90000004,100,100,180,80,0,0,0,0);
        Check(back!=0 && front!=0,"create isolated native probe windows");
        Probe.UpdateWindow(back); Probe.UpdateWindow(front); Probe.Paint(back, 0xFFFFFF); Probe.Paint(front, 0); NativeCapture.DwmFlush(); Thread.Sleep(150);
        using var capture=new NativeCapture();
        var before=capture.Sample(120,120,100,30)!;
        Console.WriteLine($"Before RGB = {before[2]},{before[1]},{before[0]}"); Check(before[0]<10 && before[1]<10 && before[2]<10,"capture foreground before exclusion");
        Check(NativeCapture.SetWindowDisplayAffinity(front,0x11),"enable capture exclusion");
        NativeCapture.DwmFlush(); Thread.Sleep(150);
        var after=capture.Sample(120,120,100,30)!;
        Console.WriteLine($"Captured underlying RGB = {after[2]},{after[1]},{after[0]}");
        Check(after[0]>245 && after[1]>245 && after[2]>245,"capture sees actual window underneath");
        Check(NativeCapture.SetWindowDisplayAffinity(front,0),"restore capture affinity");
        NativeCapture.DwmFlush(); Thread.Sleep(100);
        var restored=capture.Sample(120,120,100,30)!;
        Check(restored[0]<10,"capture restoration");
        var frame=capture.Capture(120,120,100,30)!;
        var sharedBuffer=new byte[84];
        var stopwatch=System.Diagnostics.Stopwatch.StartNew();
        long allocatedBefore=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++) if(!frame.Sample(120,120,100,30,sharedBuffer)) throw new Exception("shared sample failed");
        long allocated=GC.GetAllocatedBytesForCurrentThread()-allocatedBefore;
        stopwatch.Stop();
        Check(allocated==0,"10000 text samples from one capture allocate zero managed bytes");
        Console.WriteLine($"Shared frame: 10000 text samples in {stopwatch.Elapsed.TotalMilliseconds:F1} ms on this test machine (not a low-end PC guarantee).");
        Check(capture.Sample(int.MinValue/2,0,10,10)==null,"off-screen capture is skipped");
    }
    finally { if(front!=0) Probe.DestroyWindow(front); if(back!=0) Probe.DestroyWindow(back); }
}
Console.WriteLine("All checks passed.");
internal static class Probe
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct RECT { public int L,T,R,B; }
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd,nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint brush);
    [DllImport("user32.dll")] private static extern int FillRect(nint dc,ref RECT rect,nint brush);
    internal static void Paint(nint hwnd,uint color) { var dc=GetDC(hwnd); var b=CreateSolidBrush(color); var r=new RECT{R=180,B=80}; FillRect(dc,ref r,b); DeleteObject(b); ReleaseDC(hwnd,dc); }
    [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(nint context);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern nint CreateWindowEx(uint ex,string cls,string title,uint style,int x,int y,int width,int height,nint parent,nint menu,nint instance,nint param);
    [DllImport("user32.dll")] internal static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool UpdateWindow(nint hwnd);
}









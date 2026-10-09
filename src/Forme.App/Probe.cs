using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Forme.App;

internal static class Probe
{
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters {public ulong ReadOperations,WriteOperations,OtherOperations,ReadBytes,WriteBytes,OtherBytes;}
    [DllImport("kernel32.dll")] private static extern bool GetProcessIoCounters(IntPtr handle,out IoCounters counters);
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr handle,uint flags);
    private static int Option(string name,int fallback,int min,int max)
    {
        var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,name);
        return i>=0&&i+1<args.Length&&int.TryParse(args[i+1],out var n)?Math.Clamp(n,min,max):fallback;
    }
    private sealed record Sample(double Seconds,long PrivateBytes,long WorkingSet,long ManagedBytes,long GcCommittedBytes,long LohBytes,int Handles,uint Gdi,uint User,int Gen0,int Gen1,int Gen2);
    private static Sample Measure(Process process,double seconds)
    {
        process.Refresh();var info=GC.GetGCMemoryInfo();
        return new(seconds,process.PrivateMemorySize64,process.WorkingSet64,GC.GetTotalMemory(false),info.TotalCommittedBytes,
            info.GenerationInfo.Length>3?info.GenerationInfo[3].SizeAfterBytes:0,process.HandleCount,
            GetGuiResources(process.Handle,0),GetGuiResources(process.Handle,1),GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2));
    }
    private static void Save(string file,object value)
    {Directory.CreateDirectory("artifacts");File.WriteAllText(Path.Combine("artifacts",file),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));}
    private static async Task Enter(DesktopHost host,Controller c,string state)
    {
        if(state=="tray-after-100-switches")
        {
            for(int n=0;n<100;n++){host.ShowHouse("home");await Task.Delay(8);host.HidePet();await Task.Delay(8);}
            host.HidePet();return;
        }
        host.HidePet();
        if(state.StartsWith("house-",StringComparison.Ordinal))
        {
            host.ShowHouse("home");var room=Descendants(host.House!).OfType<Room3DView>().FirstOrDefault();
            if(state.StartsWith("house-outdoor-",StringComparison.Ordinal))room?.SwitchScene(true);else room?.SwitchScene(false);
            if(state.EndsWith("-moving",StringComparison.Ordinal))room?.TryMove(state.StartsWith("house-outdoor-",StringComparison.Ordinal)?new(3,3):new(0,1));
        }
        else if(state.StartsWith("pet-",StringComparison.Ordinal))
        {
            c.SavePreferences(c.Preferences with{PetIdleMode=state switch{"pet-sleep"=>"sleep","pet-walk"=>"walk","pet-run"=>"run",_=>"idle"}});host.ShowPet();
        }
        else if(state.StartsWith("floating-chat-",StringComparison.Ordinal))
        {
            host.ShowFloatingChat();if(state=="floating-chat-streaming-mock")host.FloatingChat?.StartProbeStreamingMock();
        }
        else host.HidePet();
    }
    public static async Task Run(DesktopHost host,Controller c)
    {
        try
        {
            c.SavePreferences(new(){Onboarded=true});
            int cycles=Option("--probe-cycles",0,0,1000);
            if(cycles>0){await Cycles(host,c,cycles);return;}
            int seconds=Option("--probe-seconds",600,10,28800),warmup=Option("--probe-warmup",120,0,600);
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--probe-state");
            string[] states=i>=0&&i+1<args.Length?[args[i+1]]:["house-indoor-idle","pet-idle","tray-cold"];
            var allowed=new HashSet<string>(StringComparer.Ordinal){"pet-idle","pet-sleep","pet-walk","pet-run","house-indoor-idle","house-indoor-moving","house-outdoor-idle","house-outdoor-moving","floating-chat-idle","floating-chat-streaming-mock","tray-cold","tray-after-100-switches"};
            if(states.Any(s=>!allowed.Contains(s)))throw new ArgumentException("Unknown probe state");
            var reports=new List<object>();
            foreach(var state in states)
            {
                await Enter(host,c,state);await Task.Delay(TimeSpan.FromSeconds(warmup));
                using var process=Process.GetCurrentProcess();var initial=Measure(process,0);var cpu=process.TotalProcessorTime;
                GetProcessIoCounters(process.Handle,out var before);var watch=Stopwatch.StartNew();var samples=new List<Sample>();
                var presentation=new List<object>();int activeSamples=0;
                for(int n=0;n<seconds;n++)
                {
                    await Task.Delay(1000);samples.Add(Measure(process,watch.Elapsed.TotalSeconds));
                    var window=state.StartsWith("floating-chat-",StringComparison.Ordinal)?(Window?)host.FloatingChat:state.StartsWith("tray-",StringComparison.Ordinal)?null:(Window?)host.House??host.Pet;
                    bool active=state.StartsWith("tray-",StringComparison.Ordinal)?window is null:window is {IsVisible:true,WindowState:WindowState.Normal};
                    if(active)activeSamples++;
                    presentation.Add(new{Seconds=watch.Elapsed.TotalSeconds,Active=active,Visible=window?.IsVisible,WindowState=window?.WindowState.ToString(),RoomAnimation=host.House is {} house?Descendants(house).OfType<Room3DView>().FirstOrDefault()?.AnimationRunning:null});
                }
                GetProcessIoCounters(process.Handle,out var after);
                reports.Add(new{State=state,Seconds=watch.Elapsed.TotalSeconds,NormalizedCpuPercent=(process.TotalProcessorTime-cpu).TotalSeconds/watch.Elapsed.TotalSeconds/Environment.ProcessorCount*100,
                    AveragePrivateMB=samples.Average(s=>s.PrivateBytes)/1048576,PeakPrivateMB=samples.Max(s=>s.PrivateBytes)/1048576d,
                    WriteBytes=after.WriteBytes-before.WriteBytes,WriteOperations=after.WriteOperations-before.WriteOperations,Initial=initial,Samples=samples,ActiveSamples=activeSamples,PresentationValid=activeSamples==seconds,Presentation=presentation});
                Save("performance.json",new{Date=DateTimeOffset.Now,OS=Environment.OSVersion.ToString(),CpuCount=Environment.ProcessorCount,
                    WarmupSeconds=warmup,DurationPerState=seconds,FreshProcessPerState=states.Length==1,GpuMeasured=false,Results=reports});
            }
        }
        catch(Exception ex){Save("probe-error.json",new{Type=ex.GetType().FullName});Environment.ExitCode=1;}
        finally{await host.Exit();}
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var item in Descendants(child))yield return item;}
    }
    private static void Track(Window window,List<(string Type,WeakReference Reference)> references)
    {
        references.Add((window.GetType().Name,new(window)));
        foreach(var view in Descendants(window).Where(v=>v is Pet3DView or Room3DView))
        {
            references.Add((view.GetType().Name,new(view)));
            foreach(var field in view.GetType().GetFields(BindingFlags.NonPublic|BindingFlags.Instance))
            {
                if(field.GetValue(view) is not {} item)continue;
                if(item is PetAnimator animator)
                {
                    references.Add(("PetAnimator",new(animator)));
                    foreach(var part in typeof(PetAnimator).GetFields(BindingFlags.NonPublic|BindingFlags.Instance))
                        if(part.GetValue(animator) is IPetModel or DispatcherTimer)references.Add((part.Name,new(part.GetValue(animator)!)));
                }
                else if(item is System.Windows.Media.Media3D.ModelVisual3D or IPetModel or DispatcherTimer)references.Add((field.Name,new(item)));
            }
        }
    }
    private static async Task Cycles(DesktopHost host,Controller c,int count)
    {
        using var process=Process.GetCurrentProcess();var watch=Stopwatch.StartNew();var samples=new List<Sample>();
        var references=new List<(string Type,WeakReference Reference)>();
        for(int n=0;n<count;n++)
        {
            await Enter(host,c,"house-indoor-idle");await Task.Delay(70);Track(host.House!,references);
            await Enter(host,c,"pet-idle");await Task.Delay(70);Track(host.Pet!,references);
            await Enter(host,c,"tray-cold");await Task.Delay(70);
            if(n%10==9)samples.Add(Measure(process,watch.Elapsed.TotalSeconds));
        }
        await Task.Delay(1000);
        // Diagnostic reachability check only; never used to manage production memory.
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();await Task.Delay(1000);
        var survivors=references.Where(r=>r.Reference.IsAlive).GroupBy(r=>r.Type).ToDictionary(g=>g.Key,g=>g.Count());
        int activeTimers=references.Count(r=>r.Reference.Target is DispatcherTimer timer&&timer.IsEnabled);
        Save("lifecycle.json",new{Date=DateTimeOffset.Now,Cycles=count,DiagnosticGcAtEndOnly=true,Samples=samples,
            AfterCollection=Measure(process,watch.Elapsed.TotalSeconds),Survivors=survivors,ActiveTimers=activeTimers});
        if(activeTimers!=0||survivors.Keys.Any(k=>k is "PetAnimator" or "_model" or "_pet"))throw new InvalidOperationException("Closed animation resources retained");
    }
}

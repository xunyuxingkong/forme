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
    private static void Enter(DesktopHost host,string state)
    {
        host.HidePet();
        if(state=="house")host.ShowHouse("home");else if(state=="pet")host.ShowPet();
    }
    public static async Task Run(DesktopHost host,Controller c)
    {
        try
        {
            c.SavePreferences(new(){Onboarded=true});
            int cycles=Option("--probe-cycles",0,0,1000);
            if(cycles>0){await Cycles(host,cycles);return;}
            int seconds=Option("--probe-seconds",600,10,28800),warmup=Option("--probe-warmup",120,0,600);
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--probe-state");
            string[] states=i>=0&&i+1<args.Length?[args[i+1]]:["house","pet","tray"];
            if(states.Any(s=>s is not ("house" or "pet" or "tray")))throw new ArgumentException("Unknown probe state");
            var reports=new List<object>();
            foreach(var state in states)
            {
                Enter(host,state);await Task.Delay(TimeSpan.FromSeconds(warmup));
                using var process=Process.GetCurrentProcess();var initial=Measure(process,0);var cpu=process.TotalProcessorTime;
                GetProcessIoCounters(process.Handle,out var before);var watch=Stopwatch.StartNew();var samples=new List<Sample>();
                for(int n=0;n<seconds;n++){await Task.Delay(1000);samples.Add(Measure(process,watch.Elapsed.TotalSeconds));}
                GetProcessIoCounters(process.Handle,out var after);
                reports.Add(new{State=state,Seconds=watch.Elapsed.TotalSeconds,NormalizedCpuPercent=(process.TotalProcessorTime-cpu).TotalSeconds/watch.Elapsed.TotalSeconds/Environment.ProcessorCount*100,
                    AveragePrivateMB=samples.Average(s=>s.PrivateBytes)/1048576,PeakPrivateMB=samples.Max(s=>s.PrivateBytes)/1048576d,
                    WriteBytes=after.WriteBytes-before.WriteBytes,WriteOperations=after.WriteOperations-before.WriteOperations,Initial=initial,Samples=samples});
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
    private static async Task Cycles(DesktopHost host,int count)
    {
        using var process=Process.GetCurrentProcess();var watch=Stopwatch.StartNew();var samples=new List<Sample>();
        var references=new List<(string Type,WeakReference Reference)>();
        for(int n=0;n<count;n++)
        {
            Enter(host,"house");await Task.Delay(70);Track(host.House!,references);
            Enter(host,"pet");await Task.Delay(70);Track(host.Pet!,references);
            Enter(host,"tray");await Task.Delay(70);
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

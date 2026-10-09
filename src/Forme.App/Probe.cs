using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Forme.App;

internal static class Probe
{
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters {public ulong ReadOperations,WriteOperations,OtherOperations,ReadBytes,WriteBytes,OtherBytes;}
    [DllImport("kernel32.dll")] private static extern bool GetProcessIoCounters(IntPtr handle,out IoCounters counters);
    public static async Task Run(DesktopHost host,Controller c)
    {
        try
        {
            int seconds=600;var args=Environment.GetCommandLineArgs();int position=Array.IndexOf(args,"--probe-seconds");
            if(position>=0&&position+1<args.Length&&int.TryParse(args[position+1],out int selected))seconds=Math.Clamp(selected,10,28800);
            var reports=new List<object>();c.SavePreferences(new(){Onboarded=true});
            foreach(string state in new[]{"house","pet","tray"})
            {
                if(state=="house")host.ShowHouse("home");else if(state=="pet"){host.HidePet();host.ShowPet();}else host.HidePet();
                await Task.Delay(2000);using var process=Process.GetCurrentProcess();process.Refresh();var cpu=process.TotalProcessorTime;GetProcessIoCounters(process.Handle,out var ioBefore);var watch=Stopwatch.StartNew();var memories=new List<long>();
                for(int elapsed=0;elapsed<seconds;elapsed++) {await Task.Delay(1000);process.Refresh();memories.Add(process.PrivateMemorySize64);}
                process.Refresh();GetProcessIoCounters(process.Handle,out var ioAfter);
                reports.Add(new {State=state,Seconds=watch.Elapsed.TotalSeconds,NormalizedCpuPercent=(process.TotalProcessorTime-cpu).TotalSeconds/watch.Elapsed.TotalSeconds/Environment.ProcessorCount*100,AveragePrivateMB=memories.Average()/1024/1024,PeakPrivateMB=memories.Max()/1024/1024,WriteBytes=ioAfter.WriteBytes-ioBefore.WriteBytes,WriteOperations=ioAfter.WriteOperations-ioBefore.WriteOperations});
                Directory.CreateDirectory("artifacts");File.WriteAllText("artifacts/performance.json",JsonSerializer.Serialize(new{Date=DateTimeOffset.Now,OS=Environment.OSVersion.ToString(),CpuCount=Environment.ProcessorCount,DurationPerState=seconds,Results=reports},new JsonSerializerOptions{WriteIndented=true}));
            }
        }
        catch(Exception ex){Directory.CreateDirectory("artifacts");File.WriteAllText("artifacts/probe-error.txt",ex.ToString());Environment.ExitCode=1;}
        await host.Exit();
    }
}

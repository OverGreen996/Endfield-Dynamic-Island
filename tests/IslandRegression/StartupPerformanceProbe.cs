using System.Diagnostics;
using EndfieldChargePlus.Customization;

public static class StartupPerformanceProbe
{
    public static async Task MonitorAsync()
    {
        var settings=CustomHudSettings.CreateDefault();var profile=settings.Profiles.First(p=>p.BuiltInKey=="system.overview");
        using var data=new HudDataService();using var process=Process.GetCurrentProcess();
        var before=process.TotalProcessorTime;var timer=Stopwatch.StartNew();int samples=0;
        while(timer.Elapsed.TotalSeconds<25)
        {
            var frame=Stopwatch.StartNew();var cached=data.Read(settings,profile);
            Console.WriteLine($"cached display read: {frame.Elapsed.TotalMilliseconds:0.0} ms; CPU={cached.Metrics![0].Value}");
            var started=Stopwatch.GetTimestamp();await data.RefreshAsync(settings,profile);samples++;
            var elapsed=Stopwatch.GetElapsedTime(started);if(elapsed.TotalSeconds<5)await Task.Delay(TimeSpan.FromSeconds(5)-elapsed);
        }
        process.Refresh();var cpuSeconds=(process.TotalProcessorTime-before).TotalSeconds;
        Console.WriteLine($"25s real hardware sampling: samples={samples}, CPU seconds={cpuSeconds:0.000}, machine CPU fraction={cpuSeconds/timer.Elapsed.TotalSeconds/Environment.ProcessorCount*100:0.00}%, working set={process.WorkingSet64/1048576d:0.0} MB, private={process.PrivateMemorySize64/1048576d:0.0} MB");
        before=process.TotalProcessorTime;timer.Restart();samples=0;
        while(timer.Elapsed.TotalSeconds<10)
        {
            var started=Stopwatch.GetTimestamp();await data.RefreshAsync(settings,profile);samples++;
            var elapsed=Stopwatch.GetElapsedTime(started);if(elapsed.TotalSeconds<1)await Task.Delay(TimeSpan.FromSeconds(1)-elapsed);
        }
        process.Refresh();cpuSeconds=(process.TotalProcessorTime-before).TotalSeconds;
        Console.WriteLine($"10s visible cadence: refresh requests={samples} (recent/inflight samples coalesce), CPU seconds={cpuSeconds:0.000}, machine CPU fraction={cpuSeconds/timer.Elapsed.TotalSeconds/Environment.ProcessorCount*100:0.00}%, working set={process.WorkingSet64/1048576d:0.0} MB, private={process.PrivateMemorySize64/1048576d:0.0} MB");
    }
    public static async Task BaselineAsync()
    {
        var settings=CustomHudSettings.CreateDefault();
        var profile=settings.Profiles.First(p=>p.BuiltInKey=="system.overview");
        var required=HudProfileRenderer.GetRequiredVariables(profile);
        using var variables=new VariableHub();
        for(int i=0;i<3;i++)
        {
            var timer=Stopwatch.StartNew();
            var values=await variables.SnapshotAsync(settings,required,profile.GpuAdapterId,profile.PingTarget,profile.ProbeProtocol,profile.ProbePort);
            Console.WriteLine($"overview sample {i}: {timer.Elapsed.TotalMilliseconds:0} ms; required={required.Count}, collected={values.Count}");
        }
    }
    public static async Task UnitAsync()
    {
        int passed=0,calls=0;
        void Check(bool ok,string label){if(!ok)throw new Exception("FAIL: "+label);passed++;Console.WriteLine("PASS: "+label);}
        var settings=CustomHudSettings.CreateDefault();var profile=settings.Profiles.First(p=>p.BuiltInKey=="system.overview");
        Check(HudDataService.CanPrewarm(profile),"hardware overview permits local background sampling");
        Check(!HudDataService.CanPrewarm(settings.Profiles.First(p=>p.BuiltInKey=="time.day-progress")),"clock-only profile does not start hardware sampling");
        Check(!HudDataService.CanPrewarm(profile with { PrimaryTemplate="{http.private.value}" }),"mixed hardware and HTTP profiles cannot silently prefetch network data");
        Check(!HudDataService.CanPrewarm(settings.Profiles.First(p=>p.BuiltInKey=="network.ping")),"network probe profile cannot run hidden background probes");
        var held=new TaskCompletionSource<Dictionary<string,object?>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service=new HudDataService((_,_,_)=>{Interlocked.Increment(ref calls);return held.Task;});
        var timer=Stopwatch.StartNew();var cold=service.Read(settings,profile);
        Check(timer.ElapsedMilliseconds<100&&calls==0,"cold display reads never start or wait for a collector");
        Check(cold.Metrics!.All(x=>x.Usage is null),"unavailable cold metrics have no fabricated zero measurements");
        var first=service.RefreshAsync(settings,profile);var second=service.RefreshAsync(settings,profile);
        await Task.Delay(25);Check(calls==1,"concurrent refreshes share one collector");
        using(var cancel=new CancellationTokenSource())
        {
            var waiter=service.RefreshAsync(settings,profile,cancel.Token);cancel.Cancel();
            try{await waiter;throw new Exception("waiter was not cancelled");}catch(OperationCanceledException){}
            Check(!first.IsCompleted,"cancelled display wait does not destroy shared sampling");
        }
        held.SetResult(new(){{"cpu.usage",37d},{"gpu.usage",21d},{"memory.usage",42d}});await Task.WhenAll(first,second);
        Check(service.Read(settings,profile).Metrics![0].Value=="37%","completed data replaces cold placeholders");
        await service.RefreshAsync(settings,profile);Check(calls==1,"immediate refresh reuses the recent sample");
        var other=settings with{HttpSources=new()};Check(service.Read(other,profile).Metrics![0].Usage is null,"different source configuration cannot reuse another configuration's data");
        using var disposing=new HudDataService((_,_,_)=>held.Task);disposing.Dispose();
        try{await disposing.RefreshAsync(settings,profile);throw new Exception("disposed service collected");}catch(ObjectDisposedException){}
        Check(true,"disposed service cannot schedule another collector");
        Console.WriteLine($"{passed}/{passed} PASS");
    }
}

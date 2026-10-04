using System.Diagnostics;
using System.Threading;

namespace EndfieldChargePlus.Customization;

/// <summary>One bounded, serialized collector. Display reads never wait for hardware or a network probe.</summary>
public sealed class HudDataService : IDisposable
{
    private sealed record Key(CustomHudSettings Settings,string Variables,string Gpu,string Ping,string Protocol,int Port,AppLanguage Language);
    private sealed class Entry
    {
        public Dictionary<string,object?> Values=new(StringComparer.OrdinalIgnoreCase);
        public long SampledAt;
        public Task? Pending;
    }
    private readonly object _gate=new();
    private readonly Dictionary<Key,Entry> _entries=new();
    private readonly SemaphoreSlim _collector=new(1,1);
    private readonly CancellationTokenSource _lifetime=new();
    private readonly VariableHub? _variables;
    private readonly Func<CustomHudSettings,HudProfile,CancellationToken,Task<Dictionary<string,object?>>> _sample;
    private bool _disposed;
    public static bool CanPrewarm(HudProfile profile)
    {
        var keys = HudProfileRenderer.GetRequiredVariables(profile);
        static bool Hardware(string key) => key.StartsWith("cpu.", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("gpu.", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("memory.", StringComparison.OrdinalIgnoreCase);
        return keys.Any(Hardware) && keys.All(key => Hardware(key) || key.StartsWith("time.", StringComparison.OrdinalIgnoreCase));
    }
    public HudDataService(Func<CustomHudSettings,HudProfile,CancellationToken,Task<Dictionary<string,object?>>>? sample=null)
    {
        if(sample is null)
        {
            _variables=new VariableHub();
            _sample=(settings,profile,ct)=>_variables.SnapshotAsync(settings,HudProfileRenderer.GetRequiredVariables(profile),profile.GpuAdapterId,profile.PingTarget,profile.ProbeProtocol,profile.ProbePort,ct);
        }
        else _sample=sample;
    }
    private static Key Scope(CustomHudSettings settings,HudProfile profile)=>new(settings,
        string.Join('|',HudProfileRenderer.GetRequiredVariables(profile).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase)),
        profile.GpuAdapterId??"",profile.PingTarget??"",profile.ProbeProtocol??"",profile.ProbePort,LocalizationManager.Current);
    public HudRenderData Read(CustomHudSettings settings,HudProfile profile)
    {
        Dictionary<string,object?> values;
        lock(_gate)
        {
            values=_entries.TryGetValue(Scope(settings,profile),out var entry)&&entry.SampledAt!=0&&Stopwatch.GetElapsedTime(entry.SampledAt).TotalSeconds<=8
                ?new(entry.Values,StringComparer.OrdinalIgnoreCase):new(StringComparer.OrdinalIgnoreCase);
        }
        VariableHub.AddImmediateValues(values,HudProfileRenderer.GetRequiredVariables(profile));
        return HudProfileRenderer.Render(profile,values);
    }
    public Task RefreshAsync(CustomHudSettings settings,HudProfile profile,CancellationToken ct=default)
    {
        Task pending;
        lock(_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed,this);
            var key=Scope(settings,profile);
            if(!_entries.TryGetValue(key,out var entry))
            {
                if(_entries.Count>=8)
                {
                    var oldest=_entries.Where(x=>x.Value.Pending is null||x.Value.Pending.IsCompleted).OrderBy(x=>x.Value.SampledAt).FirstOrDefault();
                    if(oldest.Key is not null)_entries.Remove(oldest.Key);
                    else return Task.CompletedTask; // Do not accumulate queued collectors during rapid settings changes.
                }
                _entries[key]=entry=new Entry();
            }
            if(entry.Pending is {IsCompleted:false})pending=entry.Pending;
            else if(entry.SampledAt!=0&&Stopwatch.GetElapsedTime(entry.SampledAt).TotalMilliseconds<750)return Task.CompletedTask;
            else pending=entry.Pending=Task.Run(async()=>
            {
                await _collector.WaitAsync(_lifetime.Token).ConfigureAwait(false);
                try
                {
                    var fresh=await _sample(settings,profile,_lifetime.Token).ConfigureAwait(false);
                    lock(_gate)if(!_disposed&&key.Language==LocalizationManager.Current){entry.Values=new(fresh,StringComparer.OrdinalIgnoreCase);entry.SampledAt=Stopwatch.GetTimestamp();}
                }
                catch(OperationCanceledException){}
                catch{} // Unavailable collectors leave missing/stale fields instead of blocking the interface.
                finally{_collector.Release();}
            });
        }
        return ct.CanBeCanceled?pending.WaitAsync(ct):pending;
    }
    public void Dispose()
    {
        Task[] pending;
        lock(_gate){if(_disposed)return;_disposed=true;_lifetime.Cancel();pending=_entries.Values.Select(x=>x.Pending).OfType<Task>().ToArray();_entries.Clear();}
        // WMI itself is not always cancellable. Release its resources after it settles, never on the UI thread.
        _=Task.WhenAll(pending).ContinueWith(_=>{_variables?.Dispose();_lifetime.Dispose();_collector.Dispose();},TaskScheduler.Default);
    }
}

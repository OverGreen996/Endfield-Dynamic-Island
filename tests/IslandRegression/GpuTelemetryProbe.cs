using EndfieldChargePlus.Customization;

public static class GpuTelemetryProbe
{
    public static void Unit()
    {
        int passed=0;
        void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);passed++;}
        const string a="luid_0x00000000_0x00000001",b="luid_0x00000000_0x00000002";
        string Name(string luid,int pid,int engine,string type)=>$"pid_{pid}_{luid}_phys_0_eng_{engine}_engtype_{type}";
        GpuEngineSample Sample(params GpuEngineCounter[] rows)=>GpuEngineSample.Aggregate(rows,new[]{a},0);
        Check(double.IsNaN(Sample().Usage),"no counters are unavailable, not idle");
        Check(Sample(new GpuEngineCounter(Name(a,1,0,"3D"),0)).Usage==0,"measured idle remains real zero");
        Check(Sample(new GpuEngineCounter(Name(a,1,0,"3D"),17),new GpuEngineCounter(Name(a,2,0,"3D"),12)).Usage==29,"processes sharing an engine are summed");
        Check(Sample(new GpuEngineCounter(Name(a,1,0,"3D"),17),new GpuEngineCounter(Name(a,1,1,"Copy"),40)).Usage==40,"overall value uses busiest engine");
        Check(Sample(new GpuEngineCounter(Name(a,1,0,"3D"),17),new GpuEngineCounter(Name(b,1,0,"3D"),99)).Usage==17,"another adapter's phys_0 cannot contaminate chosen GPU");
        Check(double.IsNaN(Sample(new GpuEngineCounter(Name(b,1,0,"3D"),99)).Usage),"wrong LUID never fabricates a valid selected sample");
        Check(Sample(new GpuEngineCounter(Name(a,1,1,"Compute_1"),51)).Compute==51,"compute engine type keeps underscore suffix");
        Check(Sample(new GpuEngineCounter(Name(a,1,2,"Video_Decode"),31)).Decode==31,"video decode with underscore is recognized");
        Check(Sample(new GpuEngineCounter(Name(a,1,3,"Video_Encode"),24)).Encode==24,"video encode with underscore is recognized");
        Check(Sample(new GpuEngineCounter(Name(a,1,0,"3D"),90),new GpuEngineCounter(Name(a,2,0,"3D"),25)).Usage==100,"overlapping process counters cap at 100 percent");
        Check(double.IsNaN(Sample(new GpuEngineCounter(Name(a,1,0,"3D"),double.NaN),new GpuEngineCounter(Name(a,2,0,"3D"),-1)).Usage),"invalid values cannot masquerade as zero");
        var alias=GpuEngineSample.Aggregate(new[]{new GpuEngineCounter(Name(a,1,0,"3D"),21),new GpuEngineCounter(Name(b,1,0,"3D"),21)},new[]{a,b},0);
        Check(alias.Usage==21,"logical views of one physical GPU are not double counted");
        Check(GpuEngineSample.Matches("phys_2_eng_0_engtype_3D",new[]{a},2),"legacy counters without LUID retain index fallback");
        Check(!GpuEngineSample.Matches("phys_2_eng_0_engtype_3D",Array.Empty<string>(),-1),"unknown physical index cannot match arbitrary GPU");
        Check(!GpuEngineSample.Matches(Name(b,1,0,"3D"),new[]{a},0),"memory and engine counters use identical adapter isolation");
        var settings=CustomHudSettings.CreateDefault();var overview=settings.Profiles.First(x=>x.BuiltInKey=="system.overview");
        var missing=HudProfileRenderer.Render(overview,new Dictionary<string,object?>{{"gpu.usage",double.NaN}}).Metrics![1];
        Check(missing.Usage is null && missing.Value!="0%","HUD renders unavailable GPU without zero progress");
        var idle=HudProfileRenderer.Render(overview,new Dictionary<string,object?>{{"gpu.usage",0d}}).Metrics![1];
        Check(idle.Value=="0%" && idle.Usage==0,"HUD preserves genuine idle reading");
        Console.WriteLine($"{passed}/{passed} PASS");
    }
    public static async Task LiveAsync()
    {
        foreach(var adapter in GpuAdapterCatalog.GetAdapters(true))
            Console.WriteLine($"Adapter: {adapter.Name}; phys={adapter.PhysicalIndex}; aliases={string.Join(',',adapter.PerfLuidTokens)}; VRAM={adapter.DedicatedMemoryBytes/1073741824d:0.00} GB");
        using var hub=new VariableHub();
        for(int i=0;i<5;i++)
        {
            var values=await hub.SnapshotAsync(CustomHudSettings.CreateDefault(),new[]{"gpu.usage","gpu.dedicated_used_bytes"});
            Console.WriteLine($"Sample {i}: GPU={values["gpu.name"]}; usage={values["gpu.usage"]}; VRAM={Convert.ToDouble(values["gpu.dedicated_used_bytes"])/1073741824d:0.00} GB");
            await Task.Delay(1000);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace EndfieldChargePlus.Customization;

public sealed record GpuEngineCounter(string Name, double Utilization);
public sealed record GpuEngineSample(double Usage, double ThreeD, double Compute, double Copy, double Decode, double Encode)
{
    public static GpuEngineSample Unavailable { get; } = new(double.NaN,double.NaN,double.NaN,double.NaN,double.NaN,double.NaN);

    public static bool Matches(string name, IReadOnlyList<string> tokens, int physicalIndex)
    {
        var luid=Regex.Match(name,@"luid_0x[0-9a-f]+_0x[0-9a-f]+",RegexOptions.IgnoreCase);
        // phys_0 is local to each adapter LUID, not a machine-wide GPU number.
        // Never use it to accept a different adapter when an exact LUID is available.
        if(luid.Success && tokens.Count>0)
            return tokens.Any(token=>string.Equals(token,luid.Value,StringComparison.OrdinalIgnoreCase));
        return physicalIndex>=0 && Regex.IsMatch(name,$@"(?:^|_)phys_{physicalIndex}(?:_|$)",RegexOptions.IgnoreCase);
    }

    public static GpuEngineSample Aggregate(IEnumerable<GpuEngineCounter> rows, IReadOnlyList<string> tokens, int physicalIndex)
    {
        var engines=new Dictionary<string,(string Type,double Value)>(StringComparer.OrdinalIgnoreCase);
        foreach(var row in rows)
        {
            if(!double.IsFinite(row.Utilization)||row.Utilization<0||!Matches(row.Name,tokens,physicalIndex))continue;
            var engine=Regex.Match(row.Name,@"(?:^|_)eng_(?<id>\d+)_engtype_(?<type>.+)$",RegexOptions.IgnoreCase);
            if(!engine.Success)continue;
            var luid=Regex.Match(row.Name,@"luid_0x[0-9a-f]+_0x[0-9a-f]+",RegexOptions.IgnoreCase);
            string type=engine.Groups["type"].Value;
            string key=$"{luid.Value}|{engine.Groups["id"].Value}|{type}";
            engines[key]=(type,engines.TryGetValue(key,out var old)?old.Value+row.Utilization:row.Utilization);
        }
        if(engines.Count==0)return Unavailable;
        double Max(Func<string,bool> match)=>Math.Clamp(engines.Values.Where(x=>match(x.Type)).Select(x=>x.Value).DefaultIfEmpty(0).Max(),0,100);
        bool Is(string type,string wanted)=>type.Equals(wanted,StringComparison.OrdinalIgnoreCase);
        return new(Max(_=>true),Max(t=>Is(t,"3D")),Max(t=>t.StartsWith("Compute",StringComparison.OrdinalIgnoreCase)),
            Max(t=>Is(t,"Copy")),Max(t=>Is(t,"VideoDecode")||Is(t,"Video_Decode")),Max(t=>Is(t,"VideoEncode")||Is(t,"Video_Encode")));
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace EndfieldChargePlus.Customization;

public static class HudProfileRenderer
{
    public static HudRenderData Render(HudProfile profile, IReadOnlyDictionary<string, object?> vars)
    {
        profile = BuiltInProfileLocalization.ForCurrentLanguage(profile);
        var effective = BuildEffectiveVariables(profile, vars);

        double raw = TemplateEngine.EvaluateNumber(profile.ProgressVariable, effective, profile.ProgressMin);
        double span = profile.ProgressMax - profile.ProgressMin;
        double progress = span <= 0 ? 0 : (raw - profile.ProgressMin) / span;
        progress = Math.Clamp(progress, 0, 1);

        string accent = ResolveAccent(profile, effective);
        return new HudRenderData(
            Tagline: TemplateEngine.Render(profile.TaglineTemplate, effective),
            Title: TemplateEngine.Render(profile.TitleTemplate, effective),
            PrimaryText: TemplateEngine.Render(profile.PrimaryTemplate, effective),
            SecondaryText: TemplateEngine.Render(profile.SecondaryTemplate, effective),
            RightText: TemplateEngine.Render(profile.RightTemplate, effective),
            RightSuffix: TemplateEngine.Render(profile.RightSuffix, effective),
            Progress: progress,
            LeftIcon: profile.LeftIcon,
            RightIcon: profile.RightIcon,
            AccentColor: accent,
            SimpleAnimation: !string.Equals(profile.AnimationMode, "Full", StringComparison.OrdinalIgnoreCase),
            Metrics: IsOverview(profile)?BuildOverview(effective):null);
    }

    public static IReadOnlyCollection<string> GetRequiredVariables(HudProfile profile)
    {
        profile = BuiltInProfileLocalization.ForCurrentLanguage(profile);
        var keys = new HashSet<string>(TemplateEngine.ExtractKeys(
            profile.TaglineTemplate,
            profile.TitleTemplate,
            profile.PrimaryTemplate,
            profile.SecondaryTemplate,
            profile.RightTemplate,
            profile.RightSuffix), StringComparer.OrdinalIgnoreCase);
        if(IsOverview(profile))keys.UnionWith(new[]{"cpu.usage","cpu.frequency_ghz","gpu.usage","gpu.name","gpu.dedicated_used_bytes","gpu.vram_bytes","gpu.uses_unified_memory","memory.usage","memory.used_bytes","memory.total_bytes"});

        if (!string.IsNullOrWhiteSpace(profile.ProgressVariable))
        {
            foreach (var key in TemplateEngine.ExtractExpressionKeys(profile.ProgressVariable))
                keys.Add(key);
        }

        foreach (var rule in profile.ColorRules)
        {
            if (!string.IsNullOrWhiteSpace(rule.Variable))
                keys.Add(rule.Variable.Trim());
        }

        // time.display.* / time.target.* 是按方案設定派生的；要求底層提供即時本地時鐘。
        if (IsTimeProfile(profile) || keys.Any(k => k.StartsWith("time.", StringComparison.OrdinalIgnoreCase)))
            keys.Add("time.current");

        // Network display strings and the configured percentage are profile-derived.
        // The raw system-wide download/upload rates are therefore always requested.
        if (string.Equals(profile.BuiltInKey, "system.network", StringComparison.OrdinalIgnoreCase)
            || keys.Any(k => k.StartsWith("network.", StringComparison.OrdinalIgnoreCase)))
        {
            keys.Add("network.download_bps");
            keys.Add("network.upload_bps");
        }

        return keys;
    }

    private static bool IsOverview(HudProfile profile)=>profile.BuiltInKey=="system.overview"
        || string.Equals(profile.PresentationLayout,"SystemOverview",StringComparison.OrdinalIgnoreCase);
    private static IReadOnlyList<HudMetric> BuildOverview(IReadOnlyDictionary<string,object?> values)
    {
        double? Number(string key){double n=TemplateEngine.Number(key,values,double.NaN);return double.IsFinite(n)?n:null;}
        string Percent(double? number)=>number is {}n?$"{Math.Clamp(n,0,100):0}%":"—";
        string Capacity(double? used,double? total)=>used is {}u&&total is >0?$"{u/1073741824d:0.0}/{total/1073741824d:0.0} GB":"—";
        var cpu=Number("cpu.usage");var gpu=Number("gpu.usage");var ram=Number("memory.usage");
        var used=Number("gpu.dedicated_used_bytes");var total=Number("gpu.vram_bytes");
        bool unified=values.TryGetValue("gpu.uses_unified_memory",out var unifiedValue)&&unifiedValue is true;
        return new[]{
            new HudMetric("CPU",Percent(cpu),Number("cpu.frequency_ghz") is {}ghz?$"{ghz:0.00} GHz":"—",cpu),
            new HudMetric("GPU",Percent(gpu),"Utilization",gpu),
            new HudMetric("VRAM",unified?"Shared":used is {}bytes&&total is >0?$"{bytes/1073741824d:0.0} GB":"—",unified?"Unified GPU":Capacity(used,total),!unified&&total is >0&&used is {}u?Math.Clamp(u/total.Value*100,0,100):null),
            new HudMetric("RAM",Percent(ram),Capacity(Number("memory.used_bytes"),Number("memory.total_bytes")),ram)
        };
    }

    public static bool NeedsSecondAccurateClock(HudProfile profile) =>
        IsTimeProfile(profile)
        || GetRequiredVariables(profile).Any(k =>
            string.Equals(k, "system.time", StringComparison.OrdinalIgnoreCase)
            || k.StartsWith("time.", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyDictionary<string, object?> BuildEffectiveVariables(
        HudProfile profile,
        IReadOnlyDictionary<string, object?> vars)
    {
        var required = GetRequiredVariables(profile);
        bool needsTime = IsTimeProfile(profile)
                         || required.Any(k => k.StartsWith("time.", StringComparison.OrdinalIgnoreCase));
        bool needsNetwork = IsNetworkProfile(profile)
                            || required.Any(k => k.StartsWith("network.", StringComparison.OrdinalIgnoreCase));

        if (!needsTime && !needsNetwork)
            return vars;

        var result = new Dictionary<string, object?>(vars, StringComparer.OrdinalIgnoreCase);

        if (needsTime)
        {
            var now = DateTime.Now;
            var dayProgress = Math.Clamp(now.TimeOfDay.TotalSeconds / TimeSpan.FromDays(1).TotalSeconds * 100d, 0d, 100d);

            result["time.current"] = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            result["time.day.progress"] = dayProgress;
            result["time.day.elapsed_seconds"] = now.TimeOfDay.TotalSeconds;
            result["time.day.remaining_seconds"] = Math.Max(0d, TimeSpan.FromDays(1).TotalSeconds - now.TimeOfDay.TotalSeconds);

            var target = ParseTime(profile.TimeTarget, new TimeSpan(10, 0, 0));
            var nextTarget = now.Date + target;
            if (nextTarget <= now)
                nextTarget = nextTarget.AddDays(1);

            var remainingSeconds = Math.Max(0d, (nextTarget - now).TotalSeconds);
            var remainingPercent = Math.Clamp(remainingSeconds / TimeSpan.FromDays(1).TotalSeconds * 100d, 0d, 100d);
            var targetProgress = 100d - remainingPercent;

            result["time.target.value"] = target.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
            result["time.target.remaining_seconds"] = remainingSeconds;
            result["time.target.remaining_percent"] = remainingPercent;
            result["time.target.progress"] = targetProgress;
            result["time.target.remaining_text"] = LocalizationManager.Text(
                $"剩餘{Math.Round(remainingPercent, MidpointRounding.AwayFromZero):0}%",
                $"Left {Math.Round(remainingPercent, MidpointRounding.AwayFromZero):0}%");

            if (profile.TimeTargetEnabled)
            {
                result["time.display.progress"] = remainingPercent;
                result["time.display.status_text"] = LocalizationManager.Text(
                    $"剩餘{Math.Round(remainingPercent, MidpointRounding.AwayFromZero):0}%",
                    $"Left {Math.Round(remainingPercent, MidpointRounding.AwayFromZero):0}%");
            }
            else
            {
                result["time.display.progress"] = dayProgress;
                result["time.display.status_text"] = $"{Math.Round(dayProgress, MidpointRounding.AwayFromZero):0}%";
            }
        }

        if (needsNetwork)
        {
            double downloadBps = Math.Max(0d, TemplateEngine.Number("network.download_bps", result, 0d));
            double uploadBps = Math.Max(0d, TemplateEngine.Number("network.upload_bps", result, 0d));
            double totalBps = downloadBps + uploadBps;

            result["network.total_bps"] = totalBps;
            result["network.display_download"] = FormatNetworkSpeed(downloadBps, profile.NetworkDisplayUnit);
            result["network.display_upload"] = FormatNetworkSpeed(uploadBps, profile.NetworkDisplayUnit);

            string percentMode = NormalizeNetworkPercentMode(profile.NetworkPercentMode);
            double measuredBps;
            string prefix;
            switch (percentMode)
            {
                case "Download":
                    measuredBps = downloadBps;
                    prefix = "↓ ";
                    break;
                case "Upload":
                    measuredBps = uploadBps;
                    prefix = "↑ ";
                    break;
                case "Max":
                    measuredBps = Math.Max(downloadBps, uploadBps);
                    // "較大值"模式只決定參與百分比計算的速率，不顯示方向箭頭。
                    prefix = "";
                    break;
                default:
                    measuredBps = totalBps;
                    prefix = "";
                    break;
            }

            // The configured reference can be entered as Mbps, KB/s or MB/s. Both the
            // reference and the measured rate are converted to Byte/s before comparison.
            double referenceBps = ToBytesPerSecond(profile.NetworkReferenceValue, profile.NetworkReferenceUnit);
            double configuredPercent = referenceBps <= 0d
                ? 0d
                : Math.Clamp(measuredBps / referenceBps * 100d, 0d, 100d);

            result["network.profile_percent"] = configuredPercent;
            result["network.profile_percent_bps"] = measuredBps;
            result["network.profile_percent_mode"] = percentMode;
            result["network.profile_percent_text"] = $"{prefix}{Math.Round(configuredPercent, MidpointRounding.AwayFromZero):0}%";
        }

        return result;
    }

    private static bool IsTimeProfile(HudProfile profile) =>
        string.Equals(profile.BuiltInKey, "time.day-progress", StringComparison.OrdinalIgnoreCase)
        || string.Equals(profile.Category, "時間", StringComparison.OrdinalIgnoreCase)
        || string.Equals(profile.Category, "Time", StringComparison.OrdinalIgnoreCase);

    private static bool IsNetworkProfile(HudProfile profile) =>
        string.Equals(profile.BuiltInKey, "system.network", StringComparison.OrdinalIgnoreCase)
        || GetRequiredVariables(profile).Any(k => k.StartsWith("network.", StringComparison.OrdinalIgnoreCase));

    private static string NormalizeNetworkPercentMode(string? mode) =>
        mode?.Trim().ToLowerInvariant() switch
        {
            "download" => "Download",
            "upload" => "Upload",
            "max" => "Max",
            _ => "Total",
        };

    private static string FormatNetworkSpeed(double bytesPerSecond, string? mode)
    {
        bytesPerSecond = Math.Max(0d, bytesPerSecond);
        if (string.Equals(mode, "Mbps", StringComparison.OrdinalIgnoreCase))
            return $"{bytesPerSecond * 8d / 1_000_000d:0.0} Mbps";

        // Network display uses decimal SI units consistently: 1 KB/s = 1000 B/s,
        // 1 MB/s = 1,000,000 B/s. Values below 1 MB/s use KB/s.
        if (bytesPerSecond < 1_000_000d)
            return $"{bytesPerSecond / 1_000d:0.0} KB/s";
        return $"{bytesPerSecond / 1_000_000d:0.0} MB/s";
    }

    private static double ToBytesPerSecond(double value, string? unit)
    {
        value = Math.Max(0d, value);
        return unit?.Trim() switch
        {
            "Mbps" => value * 1_000_000d / 8d,
            "KB/s" => value * 1_000d,
            "MB/s" => value * 1_000_000d,
            _ => value * 1_000_000d,
        };
    }

    private static TimeSpan ParseTime(string? text, TimeSpan fallback)
    {
        if (TimeSpan.TryParseExact(text?.Trim(), @"hh\:mm\:ss", CultureInfo.InvariantCulture, out var precise)
            && precise >= TimeSpan.Zero
            && precise < TimeSpan.FromDays(1))
            return precise;

        if (TimeSpan.TryParse(text?.Trim(), CultureInfo.InvariantCulture, out var parsed)
            && parsed >= TimeSpan.Zero
            && parsed < TimeSpan.FromDays(1))
            return parsed;

        return fallback;
    }

    private static string ResolveAccent(HudProfile p, IReadOnlyDictionary<string, object?> vars)
    {
        foreach (var r in p.ColorRules)
        {
            double actual = TemplateEngine.Number(r.Variable, vars, double.NaN);
            if (double.IsNaN(actual)) continue;
            bool match = r.Operator switch
            {
                ">" => actual > r.Value,
                ">=" => actual >= r.Value,
                "<" => actual < r.Value,
                "<=" => actual <= r.Value,
                "==" => Math.Abs(actual - r.Value) < 0.000001,
                "!=" => Math.Abs(actual - r.Value) >= 0.000001,
                _ => false,
            };
            if (match) return r.Color;
        }
        return p.AccentColor;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace EndfieldChargePlus.Customization;

public static class VariableLocalization
{
    private static readonly Dictionary<string, string> CategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["電池"] = "Battery",
        ["記憶體"] = "Memory",
        ["磁碟"] = "Disk",
        ["系統"] = "System",
        ["網路"] = "Network",
        ["網路連線測試"] = "Network Probe",
        ["網路連線測試"] = "Packet Probe",
        ["Ping"] = "Ping",
        ["Ping（相容）"] = "Ping (Compatibility)",
        ["時間"] = "Time",
        ["處理程序"] = "Processes",
        ["應用程式"] = "App Process",
        ["ECP 應用"] = "ECP App",
        ["螢幕"] = "Display",
        ["剪貼簿"] = "Clipboard",
        ["USB / 周邊裝置"] = "USB / Devices",
        ["開發者工具"] = "Developer",
        ["安全"] = "Security",
        ["自訂資料"] = "Custom Data",
        ["CPU"] = "CPU",
        ["GPU"] = "GPU",
    };

    private static readonly Dictionary<string, string> TokenMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cpu"] = "CPU", ["gpu"] = "GPU", ["vram"] = "VRAM", ["ram"] = "RAM",
        ["api"] = "API", ["dns"] = "DNS", ["tcp"] = "TCP", ["udp"] = "UDP",
        ["icmp"] = "ICMP", ["vpn"] = "VPN", ["wifi"] = "Wi-Fi", ["wlan"] = "WLAN",
        ["ipv4"] = "IPv4", ["ipv6"] = "IPv6", ["ip"] = "IP", ["mac"] = "MAC",
        ["ssid"] = "SSID", ["bssid"] = "BSSID", ["wsl"] = "WSL", ["tpm"] = "TPM",
        ["uac"] = "UAC", ["bios"] = "BIOS", ["smbios"] = "SMBIOS", ["pcie"] = "PCIe",
        ["pid"] = "PID", ["llm"] = "LLM", ["fps"] = "FPS", ["dpc"] = "DPC",
        ["io"] = "I/O", ["url"] = "URL", ["json"] = "JSON", ["http"] = "HTTP",
        ["https"] = "HTTPS", ["mhz"] = "MHz", ["ghz"] = "GHz", ["mwh"] = "mWh",
        ["wh"] = "Wh", ["kb"] = "KB", ["mb"] = "MB", ["gb"] = "GB", ["bps"] = "B/s",
        ["dbm"] = "dBm", ["rpm"] = "RPM", ["id"] = "ID", ["os"] = "OS",
        ["ac"] = "AC", ["ui"] = "UI", ["usb"] = "USB", ["dll"] = "DLL",
        ["exe"] = "EXE", ["msix"] = "MSIX", ["utc"] = "UTC", ["cny"] = "CNY",
    };

    public static VariableDefinition Localize(VariableDefinition item)
    {
        if (!LocalizationManager.IsEnglish)
            return item;

        string name = HumanizeKey(item.Key);
        string category = CategoryMap.TryGetValue(item.Category, out var mappedCategory) ? mappedCategory : HumanizeWords(item.Category);
        string type = item.ValueType switch
        {
            "文字" => "Text",
            "布林" => "Boolean",
            "整數" => "Integer",
            "動態" => "Dynamic",
            "數值" => "Number",
            _ => item.ValueType,
        };
        string unit = LocalizeUnit(item.Unit);
        string use = LocalizeUse(item.RecommendedUse);
        string formats = string.IsNullOrWhiteSpace(item.RecommendedFormats)
            ? "None"
            : item.RecommendedFormats.Replace("無需格式化", "None", StringComparison.Ordinal)
                                     .Replace(" 或 ", " or ", StringComparison.Ordinal)
                                     .Replace(" 等", " etc.", StringComparison.Ordinal);

        string description = BuildDescription(item.Key, name, type, unit);
        return item with
        {
            Name = name,
            Category = category,
            Description = description,
            ValueType = type,
            Unit = unit,
            RecommendedUse = use,
            RecommendedFormats = formats,
        };
    }

    private static string BuildDescription(string key, string name, string type, string unit)
    {
        string lower = key.ToLowerInvariant();
        if (lower.StartsWith("custom.", StringComparison.Ordinal))
            return "Custom value mapped from an HTTP / JSON data source.";
        if (lower.Contains("latency") || lower.Contains("ping"))
            return string.IsNullOrWhiteSpace(unit) ? $"Measured {name}." : $"Measured {name} ({unit}).";
        if (lower.EndsWith(".status") || lower.EndsWith("_status") || lower.Contains("status_text"))
            return $"Current {name}.";
        if (lower.EndsWith(".name") || lower.EndsWith("_name"))
            return $"Name reported for {name.Replace(" Name", "", StringComparison.OrdinalIgnoreCase)}.";
        if (lower.Contains("count"))
            return $"Current {name}.";
        if (type == "Boolean")
            return $"Whether {name.ToLowerInvariant()} is active or true.";
        if (!string.IsNullOrWhiteSpace(unit))
            return $"Current {name}, reported in {unit}.";
        return $"Current {name}.";
    }

    private static string HumanizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "Variable";
        string[] segments = key.Split('.', StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<string> useful = segments.Length > 1 ? segments.Skip(1) : segments;
        string text = string.Join(" ", useful.SelectMany(s => s.Split('_', StringSplitOptions.RemoveEmptyEntries)));
        return HumanizeWords(text);
    }

    private static string HumanizeWords(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        string clean = Regex.Replace(input, @"[_\-]+", " ").Trim();
        var words = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(word =>
        {
            if (TokenMap.TryGetValue(word, out var mapped)) return mapped;
            if (word.Length == 1) return word.ToUpperInvariant();
            return char.ToUpperInvariant(word[0]) + word[1..];
        }));
    }

    private static string LocalizeUnit(string unit) => unit switch
    {
        "秒" => "s",
        "分鐘" => "min",
        "小時" => "h",
        "天" => "days",
        "個月" => "months",
        "年" => "years",
        "次" => "count",
        "個" => "count",
        "條" => "count",
        "項" => "items",
        "核" => "cores",
        "執行緒" => "threads",
        "次/s" => "/s",
        _ => unit,
    };

    private static string LocalizeUse(string use)
    {
        if (string.IsNullOrWhiteSpace(use)) return "As needed";
        return use
            .Replace("左側主要資訊", "Left main info", StringComparison.Ordinal)
            .Replace("左側主要數值", "Primary value", StringComparison.Ordinal)
            .Replace("左側次要數值", "Secondary value", StringComparison.Ordinal)
            .Replace("左側資訊", "Left info", StringComparison.Ordinal)
            .Replace("右側狀態", "Right status", StringComparison.Ordinal)
            .Replace("圓環", "Ring", StringComparison.Ordinal)
            .Replace("標題", "Title", StringComparison.Ordinal)
            .Replace("條件", "Condition", StringComparison.Ordinal)
            .Replace("狀態計算", "Status calculation", StringComparison.Ordinal)
            .Replace("狀態", "Status", StringComparison.Ordinal)
            .Replace("除錯", "Debug", StringComparison.Ordinal)
            .Replace("自訂", "Custom", StringComparison.Ordinal)
            .Replace("依資料內容決定", "As appropriate for the value", StringComparison.Ordinal);
    }
}

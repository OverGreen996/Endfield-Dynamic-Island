using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace EndfieldChargePlus.Assistant;

/// <summary>Read-only API status and a link to the independent manager. No search or updater implementation.</summary>
public sealed class XngPluginClient : IDisposable
{
    private readonly AssistantHubClient _hub = new();
    private static string Root(string? installation) => installation switch
    {
        "standalone" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ChatGPT", "XNG"),
        "setup" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XNG"),
        _ => throw new InvalidDataException("尚未找到 XNG，請先安裝並啟動共用搜尋插件。")
    };
    public async Task<string> StatusAsync(CancellationToken token)
    {
        var state = await _hub.XngStatusAsync(token);
        if (!state.connected) return "XNG 尚未啟動 · 右鍵管理搜尋插件";
        string Version(string? value) => value is not null && Regex.IsMatch(value, @"^\d[A-Za-z0-9._-]{0,63}$") ? value : "內建";
        return $"XNG 核心 {Version(state.core_version)} · 規則 {Version(state.rules_version)} · {state.endpoint}";
    }
    public async Task OpenManagerAsync(CancellationToken token)
    {
        var state = await _hub.XngStatusAsync(token);
        var root = Root(state.installation);
        var script = Path.Combine(root, "Manage-XNGPlugin.ps1");
        if (!File.Exists(script)) throw new FileNotFoundException("找不到獨立 XNG 管理器，請先安裝 XNG 插件工具。", script);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-ExecutionPolicy"); start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File"); start.ArgumentList.Add(script);
        Process.Start(start);
    }
    public void Dispose() => _hub.Dispose();
}

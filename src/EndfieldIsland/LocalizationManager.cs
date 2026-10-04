using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Automation;

namespace EndfieldChargePlus;

public enum AppLanguage
{
    TraditionalChinese,
    English,
}

/// <summary>
/// Runtime UI/HUD language state. "Auto" follows the Windows UI culture; all zh-* cultures
/// resolve to Traditional Chinese (Taiwan), everything else resolves to English.
/// </summary>
public static partial class LocalizationManager
{
    private static readonly Dictionary<string, string> ZhToEn = new(StringComparer.Ordinal)
    {
        // Settings shell
        ["終末地 靈動島 設定"] = "Endfield Dynamic Island Settings",
        ["終末地 靈動島"] = "Endfield Dynamic Island",
        ["設定資料夾"] = "Config Folder",
        ["儲存並套用"] = "Save & Apply",
        ["顯示與位置"] = "Display & Position",
        ["HUD 內容與資料"] = "HUD Content & Data",
        ["關於"] = "About",
        ["HUD 總開關"] = "HUD Master Switch",
        ["控制 HUD 的全部顯示功能。"] = "Controls all HUD display functions.",
        ["開機啟動"] = "Start with Windows",
        ["登入 Windows 後自動啟動 終末地 靈動島。"] = "Start Endfield Dynamic Island after Windows sign-in.",
        ["顯示方式"] = "Display Mode",
        ["一直顯示"] = "Always Visible",
        ["開啟：HUD 持續顯示。\n關閉：電源插拔顯示電池；滑鼠移到目標螢幕頂部中央可喚出目前方案。"] = "On: keep the HUD visible.\nOff: power changes show Battery; move the pointer to the top-center of the target display to summon the active profile.",
        ["常駐狀態"] = "Persistent",
        ["顯示層級"] = "Layer",
        ["HUD 不透明度"] = "HUD Opacity",
        ["100% 為完全不透明。"] = "100% is fully opaque.",
        ["時鐘"] = "Clock",
        ["顯示時鐘"] = "Show Clock",
        ["在 HUD 右側顯示目前時間；日期可另外開啟。"] = "Show the current time on the right side of the HUD; date is optional.",
        ["24 小時制"] = "24-hour",
        ["顯示日期"] = "Show Date",
        ["位置"] = "Position",
        ["目標螢幕"] = "Target Display",
        ["定位方式"] = "Positioning",
        ["預設位置"] = "Preset",
        ["X 微調 / px"] = "X Offset / px",
        ["Y 微調 / px"] = "Y Offset / px",
        ["X 向右為正，Y 向下為正。"] = "+X right, +Y down.",
        ["X 座標 / px"] = "X / px",
        ["Y 座標 / px"] = "Y / px",
        ["尺寸與動畫"] = "Size & Animation",
        ["還原預設"] = "Reset",
        ["HUD 縮放"] = "HUD Scale",
        ["顯示時間 / 秒"] = "Duration / s",
        ["回彈強度"] = "Bounce",
        ["波紋強度"] = "Ripple Strength",
        ["波紋幅度"] = "Ripple Spread",
        ["終末地風格狀態列 HUD"] = "Endfield-style Status HUD",
        ["建置日期  2026.10.04"] = "Build  2026.10.04",
        ["檢查更新"] = "Check Updates",
        ["目前版本：v0.22.0"] = "Current: v0.22.0",
        ["最新版本：尚未取得"] = "Latest: not checked",
        ["狀態：尚未檢查"] = "Status: not checked",
        ["專案與授權"] = "Project & License",
        ["開源授權"] = "License",
        ["本專案 GitHub"] = "Project GitHub",
        ["原專案 GitHub"] = "Upstream GitHub",
        ["專案網站"] = "Website",
        ["開啟"] = "Open",
        ["設定與維護"] = "Config & Maintenance",
        ["匯出設定"] = "Export",
        ["匯入設定"] = "Import",
        ["備份目前設定"] = "Backup",
        ["紀錄資料夾"] = "Logs",
        ["還原全部預設"] = "Reset All",

        // HUD customizer - profile tab
        ["HUD 設定"] = "HUD Profiles",
        ["方案管理"] = "Profile Management",
        ["選擇目前顯示方案。內建方案修改後會另存為新方案，原預設保持不變。"] = "Select the active HUD profile. Editing a built-in profile creates a copy; the original stays unchanged.",
        ["預覽目前方案"] = "Preview",
        ["目前方案"] = "Active Profile",
        ["建立"] = "New",
        ["儲存"] = "Save",
        ["刪除"] = "Delete",
        ["目標時間模式"] = "Target-time Mode",
        ["關閉時顯示當天已過進度；開啟後顯示距離下一個每日目標時間的剩餘比例。"] = "Off: show today's elapsed progress. On: show the remaining share until the next daily target time.",
        ["每日目標時間"] = "Daily Target",
        ["GPU 裝置"] = "GPU Device",
        ["偵測到多個 GPU 時，選擇此方案要顯示的圖形處理器。"] = "Choose the GPU used by this profile when multiple adapters are detected.",
        ["網路顯示單位"] = "Network Unit",
        ["Mbps：按兆位元每秒顯示；KB/s / MB/s：根據速度大小自動切換。"] = "Mbps uses megabits/s; KB/s / MB/s switches automatically by rate.",
        ["百分比計算方式"] = "Percent Basis",
        ["使用系統全部有效網路介面的流量。可按總傳輸速率、僅下載、僅上傳或上下載較大值計算右側百分比。"] = "Uses all active interfaces. Calculate the right-side percentage from total traffic, download, upload, or the larger direction.",
        ["100% 對應網路速度"] = "100% Reference Speed",
        ["目前所選百分比模式的速度達到這裡設定的值時顯示 100%；計算前統一換算為 Byte/s。"] = "The selected rate reaches 100% at this value. Rates are normalized to Byte/s before calculation.",
        ["數值"] = "Value",
        ["單位"] = "Unit",
        ["測試位址"] = "Probe Target",
        ["支援 IPv4、IPv6 或網域名稱。"] = "IPv4, IPv6, or hostname.",
        ["例如：1.1.1.1、2606:4700:4700::1111 或 example.com"] = "e.g. 1.1.1.1, 2606:4700:4700::1111, or example.com",
        ["測試協定"] = "Protocol",
        ["ICMP 使用標準 Ping；TCP 測量連接埠連線延遲；UDP 會傳送探測資料並等待目標服務回應。"] = "ICMP uses Ping; TCP measures connect latency; UDP sends probe data and waits for a service response.",
        ["偵測連接埠"] = "Port",
        ["TCP / UDP 使用該連接埠；ICMP 不使用連接埠。"] = "Used by TCP / UDP; ignored by ICMP.",
        ["方案名稱"] = "Profile Name",
        ["例如：我的伺服器 - 線上狀態"] = "e.g. My Server - Online",
        ["動畫模式"] = "Animation",
        ["簡潔：直接展開最終 HUD，適合經常查看。"] = "Simple: quickly reveal the final HUD.",
        ["完整：包含標題、波紋與外觀切換，再進入最終 HUD。"] = "Full: title, ripple, and shape transition before the final HUD.",
        ["下拉選單中的方案即目前使用方案；始終保持一個方案被選中。"] = "The selected profile is the active profile; one profile is always selected.",
        ["自動輪播"] = "Auto Cycle",
        ["開啟後按照下方輪播清單的順序迴圈切換方案。"] = "Cycle profiles in the queue below.",
        ["輪播清單"] = "Cycle Queue",
        ["從上到下依次切換，到達末尾後回到第一項。佇列只引用目前存在的方案。"] = "Cycles top to bottom, then returns to the first item. The queue only references existing profiles.",
        ["輪播間隔 / 秒"] = "Interval / s",
        ["輪播動畫"] = "Cycle Animation",
        ["開啟自動輪播後，以這裡的動畫為準，忽略各方案自身的動畫模式。"] = "When auto cycle is enabled, this animation overrides each profile's own animation setting.",
        ["簡潔：收回上一項後，快速喚出下一項。"] = "Simple: retract the previous item, then quickly reveal the next.",
        ["完整：收回上一項後，以完整標題、波紋與形態動畫喚出下一項。"] = "Full: retract the previous item, then reveal the next with the full title/ripple sequence.",
        ["新增"] = "Add",
        ["上移"] = "Up",
        ["下移"] = "Down",
        ["顯示內容"] = "Display Content",
        ["左側顯示主要資訊，右側顯示百分比、進度或狀態類資料。"] = "Main information appears on the left; percentage, progress, or status appears on the right.",
        ["標題階段"] = "Title Stage",
        ["上方標籤"] = "Tagline",
        ["主標題"] = "Title",
        ["系統狀態"] = "System Status",
        ["左側主要資訊"] = "Left Main Info",
        ["容量、目前頻率、速率、餘額、目前時間等主要資訊。"] = "Capacity, frequency, rate, balance, time, and other primary values.",
        ["主要數值"] = "Primary",
        ["次要數值 / 補充文字"] = "Secondary / Note",
        ["右側狀態數值"] = "Right Status",
        ["使用率、剩餘比例、已過進度等狀態資訊，並可與圓環同步更新。"] = "Usage, remaining share, elapsed progress, and other status values; can drive the progress ring.",
        ["狀態數值"] = "Status",
        ["結尾文字"] = "Suffix",
        ["{}範本支援 {變數|格式}；進階運算式使用 {= 運算式 | 格式}，支援 + - * / % ^、比較、&& / || / !、?:、??、if()、min/max/avg/sum/clamp/round 等；進度變數也可填寫 = 運算式。例：{= if(memory.usage >= 80, '高', '正常')}。"] = "Templates use {variable|format}. Advanced expressions use {= expression | format}; supported operators include + - * / % ^, comparisons, && / || / !, ?:, ??, and functions such as if(), min/max/avg/sum/clamp/round. Progress may also be an = expression. Example: {= if(memory.usage >= 80, 'High', 'OK')}.",
        ["圖示與進度環"] = "Icons & Progress Ring",
        ["進度變數"] = "Progress Value",
        ["cpu.usage 或 = (cpu.usage + gpu.usage) / 2"] = "cpu.usage or = (cpu.usage + gpu.usage) / 2",
        ["最小值"] = "Minimum",
        ["最大值"] = "Maximum",
        ["左側圖示"] = "Left Icon",
        ["右側圖示"] = "Right Icon",
        ["顏色"] = "Color",
        ["預設強調色"] = "Accent Color",
        ["條件變色 · 每行：變數 運算子 數值 => 顏色"] = "Color rules · one per line: variable operator value => color",

        // Variable library
        ["變數庫"] = "Variables",
        ["按名稱、變數名稱或分類查詢，選擇變數後檢視完整說明。"] = "Search by name, key, or category; select a variable for details.",
        ["按功能分類；分類內優先顯示常用狀態與即時指標，再顯示詳細資訊。支援按名稱、變數名稱或分類查詢。"] = "Grouped by function; common status and live metrics come first, followed by detailed values. Search by name, key, or category.",
        ["搜尋：CPU 使用率 / cpu.usage"] = "Search: CPU usage / cpu.usage",
        ["選擇一個變數"] = "Select a variable",
        ["從左側清單選擇變數後，這裡會顯示變數意義和使用建議。"] = "Select a variable on the left to view its meaning and usage guidance.",
        ["變數名稱"] = "Variable Key",
        ["複製變數名稱"] = "Copy Key",
        ["範本寫法"] = "Template",
        ["複製範本"] = "Copy Template",
        ["資料屬性"] = "Data Properties",
        ["型別"] = "Type",
        ["使用建議"] = "Recommended Use",
        ["常用格式"] = "Formats",

        // Data sources
        ["資料來源"] = "Data Sources",
        ["將 GET JSON 欄位對應為 custom.source.variable；Header 可引用環境變數。"] = "Map GET JSON fields to custom.source.variable; headers may reference environment variables.",

        ["外部資料"] = "External Data",
        ["外部資料串接（HTTP / JSON）"] = "External Data (HTTP / JSON)",
        ["把網站或伺服器提供的資料顯示在 HUD，例如線上人數、服務狀態或其他數字。"] = "Display values provided by your website or server, such as online players, service status, or other metrics.",
        ["這是選用功能。CPU、GPU、記憶體、時間等內建資訊不需要設定這一頁。"] = "Optional: CPU, GPU, memory, time, and other built-in metrics do not require this page.",
        ["不是 AI 聊天或翻譯設定，也不是貼上一般網頁網址就能自動讀取內容。"] = "This is not an AI chat or translation setting, and ordinary web page URLs are not data APIs.",
        ["有 API 才需要設定：填入 API 網址 → 指定要讀的欄位 → 在 HUD 套用對應變數。"] = "For API users: enter an API URL, select a response field, then use the resulting variable in a HUD profile.",
        ["進階設定與範例（JSON）"] = "Advanced Settings and Example (JSON)",
        ["下方範例預設停用；以 // 開頭的行都是說明，不會連線。沒有需求就維持原樣。"] = "The example is disabled. Lines starting with // are comments and never connect to a server. Leave it unchanged when not needed.",

        // Tray
        ["預覽 HUD"] = "Preview HUD",
        ["設定"] = "Settings",
        ["結束程式"] = "Exit",
    };

    private static readonly Dictionary<string, string> EnToZh;
    static LocalizationManager()
    {
        AddFeatureTranslations(ZhToEn);
        EnToZh=ZhToEn.GroupBy(kv=>kv.Value,StringComparer.Ordinal).ToDictionary(group=>group.Key,group=>group.First().Key,StringComparer.Ordinal);
    }

    public static AppLanguage Current { get; private set; } = AppLanguage.English;
    public static bool IsEnglish => Current == AppLanguage.English;
    public static event Action? LanguageChanged;

    public static void Initialize(string? preference)
    {
        string normalized = NormalizePreference(preference);
        Current = normalized switch
        {
            "zh-TW" => AppLanguage.TraditionalChinese,
            "en-US" => AppLanguage.English,
            _ => CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? AppLanguage.TraditionalChinese
                : AppLanguage.English,
        };
    }

    public static string NormalizePreference(string? preference)
    {
        // Legacy Chinese preferences are migrated without resetting other settings.
        if (string.Equals(preference, "zh", StringComparison.OrdinalIgnoreCase)
            || (preference?.StartsWith("zh-", StringComparison.OrdinalIgnoreCase) ?? false)) return "zh-TW";
        if (string.Equals(preference, "en-US", StringComparison.OrdinalIgnoreCase)) return "en-US";
        return "Auto";
    }

    public static string PreferenceFor(AppLanguage language) => language == AppLanguage.English ? "en-US" : "zh-TW";

    public static void SetLanguage(AppLanguage language)
    {
        if (Current == language) return;
        Current = language;
        LanguageChanged?.Invoke();
    }

    public static string Text(string zh, string en) => IsEnglish ? en : zh;

    public static string TranslateLiteral(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        if (IsEnglish)
            return ZhToEn.TryGetValue(value, out var en) ? en : value;
        return EnToZh.TryGetValue(value, out var zh) ? zh : value;
    }

    public static void ApplyStaticText(Control root)
    {
        IEnumerable<object> nodes = root.GetLogicalDescendants().Cast<object>().Prepend(root);
        foreach (object node in nodes)
        {
            if(node is Control control)
            {
                if(ToolTip.GetTip(control) is string tip)ToolTip.SetTip(control,TranslateLiteral(tip));
                var name=AutomationProperties.GetName(control);if(!string.IsNullOrEmpty(name))AutomationProperties.SetName(control,TranslateLiteral(name));
            }
            switch (node)
            {
                case Window window:
                    window.Title = TranslateLiteral(window.Title);
                    break;
                case TabItem tab when tab.Header is string header:
                    tab.Header = TranslateLiteral(header);
                    break;
                case Expander expander when expander.Header is string expanderHeader:
                    expander.Header = TranslateLiteral(expanderHeader);
                    break;
                case MenuItem menu when menu.Header is string menuHeader:
                    menu.Header=TranslateLiteral(menuHeader);
                    break;
                case ToggleSwitch toggle:
                    if (toggle.OnContent is string on) toggle.OnContent = TranslateLiteral(on);
                    if (toggle.OffContent is string off) toggle.OffContent = TranslateLiteral(off);
                    break;
                case TextBox box:
                    if (box.Watermark is string watermark) box.Watermark = TranslateLiteral(watermark);
                    break;
                case TextBlock text when text.Text is not null:
                    text.Text = TranslateLiteral(text.Text);
                    break;
                case ContentControl content when content.Content is string s:
                    content.Content = TranslateLiteral(s);
                    break;
            }
        }
    }
}

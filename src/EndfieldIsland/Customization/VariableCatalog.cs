using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EndfieldChargePlus.Customization;

public sealed record VariableDefinition(
    string Key,
    string Name,
    string Category,
    string Description,
    string ValueType,
    string Unit,
    string RecommendedUse,
    string RecommendedFormats)
{
    public string TemplateToken => "{" + Key + "}";
    public override string ToString() => $"{Name}    {Key}";
}

public static class VariableCatalog
{
    private static VariableDefinition V(
        string key, string name, string category, string description,
        string valueType = "數值", string unit = "", string use = "依資料內容決定", string formats = "0 或 0.0") =>
        new(key, name, category, description, valueType, unit, use, formats);

    private static readonly IReadOnlyList<VariableDefinition> StaticBuiltIns = new List<VariableDefinition>
    {
        // ===== 電池 =====
        V("battery.percent", "電池電量", "電池", "目前電池剩餘電量百分比。", unit: "%", use: "右側狀態 / 圓環"),
        V("battery.remaining_mwh", "目前電池容量", "電池", "目前剩餘電池容量。", unit: "mWh", use: "左側主要數值", formats: "0"),
        V("battery.full_mwh", "滿充容量", "電池", "電池目前可充滿的容量。", unit: "mWh", use: "左側次要數值", formats: "0"),
        V("battery.design_mwh", "設計容量", "電池", "電池出廠設計容量。", unit: "mWh", use: "左側資訊", formats: "0"),
        V("battery.remaining_wh", "目前電池容量（Wh）", "電池", "目前剩餘電池容量，換算為 Wh。", unit: "Wh", use: "左側主要數值", formats: "0.0 / 0.00"),
        V("battery.full_wh", "滿充容量（Wh）", "電池", "滿充容量，換算為 Wh。", unit: "Wh", use: "左側次要數值", formats: "0.0 / 0.00"),
        V("battery.design_wh", "設計容量（Wh）", "電池", "設計容量，換算為 Wh。", unit: "Wh", use: "左側資訊", formats: "0.0 / 0.00"),
        V("battery.health_percent", "電池健康度", "電池", "滿充容量相對設計容量的比例。", unit: "%", use: "右側狀態 / 圓環"),
        V("battery.ac_online", "外接電源狀態", "電池", "目前是否接入外部電源。", "布林", use: "標題 / 條件", formats: "無需格式化"),
        V("battery.charging", "正在充電", "電池", "目前是否正在充電。", "布林", use: "標題 / 條件", formats: "無需格式化"),
        V("battery.discharging", "正在放電", "電池", "目前是否正在使用電池放電。", "布林", use: "標題 / 條件", formats: "無需格式化"),
        V("battery.rate_watts", "電池即時功率", "電池", "目前充電或放電功率的絕對值。不同機型可能不提供。", unit: "W", use: "左側資訊", formats: "0.0 / 0.00"),
        V("battery.charge_rate_watts", "充電功率", "電池", "目前充電功率；未充電時通常為 0。", unit: "W", use: "左側資訊", formats: "0.0 / 0.00"),
        V("battery.discharge_rate_watts", "放電功率", "電池", "目前放電功率；未放電時通常為 0。", unit: "W", use: "左側資訊", formats: "0.0 / 0.00"),
        V("battery.voltage_mv", "電池電壓（mV）", "電池", "Windows 電池介面報告的即時電壓。不同機型可能不提供。", unit: "mV", use: "左側資訊", formats: "0"),
        V("battery.voltage_v", "電池電壓（V）", "電池", "電池即時電壓，換算為伏特。", unit: "V", use: "左側資訊", formats: "0.00"),
        V("battery.time_remaining_seconds", "預計剩餘使用時間", "電池", "Windows 估算的剩餘電池使用時間；無法估算時為 0。", unit: "秒", use: "左側資訊", formats: "duration"),
        V("battery.time_remaining_text", "預計剩餘使用時間文字", "電池", "預計剩餘使用時間，已格式化為 HH:MM:SS；無法估算時顯示“未知”。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("battery.full_life_seconds", "預計滿電續航", "電池", "Windows 報告的滿電預計續航時間；不可用時為 0。", unit: "秒", use: "左側資訊", formats: "duration"),
        V("battery.saver_on", "節電模式狀態", "電池", "Windows 電池節電模式是否開啟。", "布林", use: "標題 / 條件", formats: "無需格式化"),
        V("battery.status_text", "電池狀態文字", "電池", "根據電源狀態生成“充電中 / 使用電池 / 已接通電源 / 未知”等文字。", "文字", use: "標題 / 右側狀態", formats: "無需格式化"),
        V("battery.power_source", "目前電源來源", "電池", "目前電源來源：“交流電源”或“電池”。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),
        V("battery.cycle_count", "電池迴圈次數", "電池", "韌體/驅動提供的電池迴圈次數；部分裝置可能回傳 0。", "整數", "次", "左側資訊", "0"),

        // ===== CPU =====
        V("cpu.usage", "CPU 使用率", "CPU", "目前整機 CPU 總使用率。", unit: "%", use: "右側狀態 / 圓環"),
        V("cpu.user_usage", "CPU 使用者態使用率", "CPU", "CPU 時間中用於使用者態程式碼的比例。", unit: "%", use: "右側狀態 / 圓環"),
        V("cpu.kernel_usage", "CPU 核心態使用率", "CPU", "CPU 時間中用於核心態且不含空閒時間的比例。", unit: "%", use: "右側狀態 / 圓環"),
        V("cpu.idle_percent", "CPU 空閒率", "CPU", "CPU 目前空閒時間比例。", unit: "%", use: "右側狀態 / 圓環"),
        V("cpu.frequency_ghz", "CPU 目前頻率", "CPU", "Windows 效能資料包告的目前即時有效頻率。", unit: "GHz", use: "左側主要數值", formats: "0.00"),
        V("cpu.frequency_mhz", "CPU 目前頻率（MHz）", "CPU", "目前即時有效頻率，單位 MHz。", unit: "MHz", use: "左側主要數值", formats: "0"),
        V("cpu.max_frequency_ghz", "CPU 最大頻率", "CPU", "Win32_Processor 報告的最大時脈頻率。", unit: "GHz", use: "左側次要數值", formats: "0.00"),
        V("cpu.max_frequency_mhz", "CPU 最大頻率（MHz）", "CPU", "最大時脈頻率，單位 MHz。", unit: "MHz", use: "左側次要數值", formats: "0"),
        V("cpu.frequency_percent", "CPU 頻率比例", "CPU", "目前有效頻率相對最大頻率的百分比，允許睿頻時超過 100%。", unit: "%", use: "右側狀態 / 圓環"),
        V("cpu.name", "CPU 名稱", "CPU", "處理器完整型號名稱。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),
        V("cpu.manufacturer", "CPU 製造商", "CPU", "處理器製造商。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),
        V("cpu.architecture", "CPU 架構", "CPU", "處理器架構，如 x64 / ARM64。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),
        V("cpu.physical_cores", "CPU 物理核心數", "CPU", "所有處理器插槽的物理核心總數。", "整數", "核", "左側資訊", "0"),
        V("cpu.logical_processors", "CPU 邏輯處理器數", "CPU", "系統可見的邏輯處理器總數。", "整數", "執行緒", "左側資訊", "0"),
        V("cpu.socket_count", "CPU 插槽數", "CPU", "系統中處理器插槽數量。", "整數", "個", "左側資訊", "0"),
        V("cpu.virtualization_enabled", "CPU 韌體虛擬化", "CPU", "韌體虛擬化功能是否啟用；依賴 WMI 支援。", "布林", use: "標題 / 條件", formats: "無需格式化"),
        V("cpu.l2_cache_kb", "CPU L2 快取", "CPU", "處理器報告的 L2 快取容量。", unit: "KB", use: "左側資訊", formats: "0"),
        V("cpu.l3_cache_kb", "CPU L3 快取", "CPU", "處理器報告的 L3 快取容量。", unit: "KB", use: "左側資訊", formats: "0"),

        // ===== 記憶體 =====
        V("memory.used_bytes", "已用實體記憶體", "記憶體", "目前已使用的實體記憶體。", unit: "Byte", use: "左側主要數值", formats: "gb:1 / gb:2 / bytes"),
        V("memory.available_bytes", "可用實體記憶體", "記憶體", "目前可用實體記憶體。", unit: "Byte", use: "左側資訊", formats: "gb:1 / bytes"),
        V("memory.total_bytes", "實體記憶體總量", "記憶體", "實體記憶體總容量。", unit: "Byte", use: "左側次要數值", formats: "gb:1 / bytes"),
        V("memory.usage", "記憶體使用率", "記憶體", "已用實體記憶體佔總實體記憶體的比例。", unit: "%", use: "右側狀態 / 圓環"),
        V("memory.free_percent", "記憶體空閒率", "記憶體", "可用實體記憶體佔總實體記憶體的比例。", unit: "%", use: "右側狀態 / 圓環"),
        V("memory.commit_used_bytes", "已提交記憶體", "記憶體", "系統已提交記憶體量。", unit: "Byte", use: "左側主要數值", formats: "gb:1 / bytes"),
        V("memory.commit_available_bytes", "剩餘提交額度", "記憶體", "提交限制中尚可使用的額度。", unit: "Byte", use: "左側資訊", formats: "gb:1 / bytes"),
        V("memory.commit_limit_bytes", "提交限制", "記憶體", "Windows 目前提交上限，通常受實體記憶體與頁面檔案共同影響。", unit: "Byte", use: "左側次要數值", formats: "gb:1 / bytes"),
        V("memory.commit_usage", "提交使用率", "記憶體", "已提交記憶體佔提交限制的比例。", unit: "%", use: "右側狀態 / 圓環"),
        V("memory.virtual_used_bytes", "已用虛擬記憶體", "記憶體", "目前系統虛擬地址空間已使用量。", unit: "Byte", use: "左側主要數值", formats: "gb:1 / bytes"),
        V("memory.virtual_available_bytes", "可用虛擬記憶體", "記憶體", "目前系統可用虛擬地址空間。", unit: "Byte", use: "左側資訊", formats: "gb:1 / bytes"),
        V("memory.virtual_total_bytes", "虛擬記憶體總量", "記憶體", "系統報告的虛擬地址空間總量。", unit: "Byte", use: "左側次要數值", formats: "gb:1 / bytes"),
        V("memory.virtual_usage", "虛擬記憶體使用率", "記憶體", "已用虛擬記憶體佔虛擬記憶體總量的比例。", unit: "%", use: "右側狀態 / 圓環"),
        V("memory.cache_bytes", "系統快取記憶體", "記憶體", "Windows 效能計數器報告的系統快取位元組數。", unit: "Byte", use: "左側資訊", formats: "gb:1 / mb:0 / bytes"),
        V("memory.paged_pool_bytes", "分頁池", "記憶體", "Windows 核心分頁池記憶體。", unit: "Byte", use: "左側資訊", formats: "mb:0 / bytes"),
        V("memory.nonpaged_pool_bytes", "非分頁池", "記憶體", "Windows 核心非分頁池記憶體。", unit: "Byte", use: "左側資訊", formats: "mb:0 / bytes"),

        // ===== GPU =====
        V("gpu.usage", "GPU 總使用率", "GPU", "目前所選 GPU 的即時利用率，取最忙 GPU 引擎。", unit: "%", use: "右側狀態 / 圓環"),
        V("gpu.usage_3d", "GPU 3D 使用率", "GPU", "目前所選 GPU 的 3D 引擎最高即時利用率。", unit: "%", use: "右側狀態 / 圓環"),
        V("gpu.usage_compute", "GPU Compute 使用率", "GPU", "目前所選 GPU 的計算引擎最高即時利用率。", unit: "%", use: "右側狀態 / 圓環"),
        V("gpu.usage_copy", "GPU Copy 使用率", "GPU", "目前所選 GPU 的複製引擎最高即時利用率。", unit: "%", use: "右側狀態 / 圓環"),
        V("gpu.usage_video_decode", "GPU 影片解碼使用率", "GPU", "目前所選 GPU 的影片解碼引擎最高即時利用率。", unit: "%", use: "右側狀態 / 圓環"),
        V("gpu.usage_video_encode", "GPU 影片編碼使用率", "GPU", "目前所選 GPU 的影片編碼引擎最高即時利用率。", unit: "%", use: "右側狀態 / 圓環"),
        V("gpu.name", "GPU 名稱", "GPU", "目前方案選擇的顯示介面卡名稱。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),
        V("gpu.adapter_id", "GPU 裝置 ID", "GPU", "目前所選 GPU 的內部穩定標識。", "文字", use: "除錯 / 自訂", formats: "無需格式化"),
        V("gpu.physical_index", "GPU 物理索引", "GPU", "Windows GPU 效能計數器對應的物理 GPU 索引。", "整數", use: "除錯 / 左側資訊", formats: "0"),
        V("gpu.count", "GPU 數量", "GPU", "目前系統偵測到的物理 GPU 數量。", "整數", "個", "左側資訊", "0"),
        V("gpu.vram_bytes", "GPU 專用視訊記憶體總量", "GPU", "目前所選 GPU 的專用視訊記憶體總容量。", unit: "Byte", use: "左側次要數值", formats: "gb:1 / bytes"),
        V("gpu.dedicated_total_bytes", "專用視訊記憶體總量", "GPU", "目前所選 GPU 的專用視訊記憶體容量。", unit: "Byte", use: "左側次要數值", formats: "gb:1 / bytes"),
        V("gpu.dedicated_used_bytes", "已用專用視訊記憶體", "GPU", "目前所選 GPU 的專用視訊記憶體即時使用量。", unit: "Byte", use: "左側主要數值", formats: "gb:1 / bytes"),
        V("gpu.dedicated_usage", "專用視訊記憶體使用率", "GPU", "已用專用視訊記憶體佔專用視訊記憶體總量的比例。", unit: "%", use: "右側狀態 / 圓環"),
        V("gpu.shared_limit_bytes", "共享 GPU 記憶體上限", "GPU", "目前 GPU 可使用的共享系統記憶體上限。", unit: "Byte", use: "左側次要數值", formats: "gb:1 / bytes"),
        V("gpu.shared_used_bytes", "已用共享 GPU 記憶體", "GPU", "目前 GPU 使用的共享系統記憶體。", unit: "Byte", use: "左側主要數值", formats: "gb:1 / bytes"),
        V("gpu.shared_usage", "共享 GPU 記憶體使用率", "GPU", "已用共享 GPU 記憶體佔共享上限的比例。", unit: "%", use: "右側狀態 / 圓環"),
        V("gpu.memory_used_bytes", "GPU 目前視訊記憶體佔用", "GPU", "預設 HUD 使用的專用視訊記憶體佔用。", unit: "Byte", use: "左側主要數值", formats: "gb:1 / bytes"),
        V("gpu.memory_total_bytes", "GPU 視訊記憶體總量", "GPU", "預設 HUD 使用的專用視訊記憶體總量。", unit: "Byte", use: "左側次要數值", formats: "gb:1 / bytes"),
        V("gpu.total_memory_used_bytes", "GPU 總記憶體佔用", "GPU", "專用視訊記憶體佔用與共享 GPU 記憶體佔用之和。", unit: "Byte", use: "左側主要數值", formats: "gb:1 / bytes"),
        V("gpu.total_memory_limit_bytes", "GPU 總可用記憶體上限", "GPU", "專用視訊記憶體總量與共享記憶體上限之和。", unit: "Byte", use: "左側次要數值", formats: "gb:1 / bytes"),
        V("gpu.total_memory_usage", "GPU 總記憶體使用率", "GPU", "GPU 總記憶體佔用佔總可用記憶體上限的比例。", unit: "%", use: "右側狀態 / 圓環"),
        V("gpu.uses_unified_memory", "GPU 是否主要使用共享記憶體", "GPU", "用於區分典型核顯/統一記憶體裝置與獨立顯示卡的啟發式標記。", "布林", use: "標題 / 條件", formats: "無需格式化"),

        // ===== 網路 =====
        V("network.download_bps", "系統總下載速度", "網路", "所有已連線、非迴環網路介面合計的即時下載速度。", unit: "Byte/s", use: "左側主要數值", formats: "speed"),
        V("network.upload_bps", "系統總上傳速度", "網路", "所有已連線、非迴環網路介面合計的即時上傳速度。", unit: "Byte/s", use: "左側次要數值", formats: "speed"),
        V("network.total_bps", "系統總傳輸速率", "網路", "系統總下載速度與總上傳速度之和。", unit: "Byte/s", use: "狀態計算", formats: "speed"),
        V("network.download_mbps", "系統總下載速度（Mbps）", "網路", "系統總下載速度，換算為 Mbps。", unit: "Mbps", use: "左側主要數值", formats: "0.0 / 0.00"),
        V("network.upload_mbps", "系統總上傳速度（Mbps）", "網路", "系統總上傳速度，換算為 Mbps。", unit: "Mbps", use: "左側次要數值", formats: "0.0 / 0.00"),
        V("network.total_mbps", "系統總傳輸速率（Mbps）", "網路", "下載與上傳合計，換算為 Mbps。", unit: "Mbps", use: "左側資訊", formats: "0.0 / 0.00"),
        V("network.display_download", "方案下載速度文字", "網路", "按目前網路方案選擇的單位生成的下載速度文字。", "文字", use: "左側主要數值", formats: "無需格式化"),
        V("network.display_upload", "方案上傳速度文字", "網路", "按目前網路方案選擇的單位生成的上傳速度文字。", "文字", use: "左側次要數值", formats: "無需格式化"),
        V("network.profile_percent", "方案網路百分比", "網路", "按方案選擇的百分比模式與“100% 對應速度”計算。", unit: "%", use: "右側狀態 / 圓環"),
        V("network.profile_percent_text", "方案網路百分比文字", "網路", "根據模式生成“50% / ↓ 50% / ↑ 50%”等文字；較大值模式不顯示箭頭。", "文字", use: "右側狀態", formats: "無需格式化"),
        V("network.profile_percent_bps", "百分比計算速度", "網路", "目前網路方案實際用於計算百分比的速度。", unit: "Byte/s", use: "狀態計算", formats: "speed"),
        V("network.profile_percent_mode", "網路百分比模式", "網路", "目前方案百分比模式：Total / Download / Upload / Max。", "文字", use: "標題 / 條件", formats: "無需格式化"),
        V("network.link_speed_bps", "總鏈路速率", "網路", "所有活動網路介面報告的鏈路速率之和。", unit: "bit/s", use: "左側資訊", formats: "0"),
        V("network.max_link_speed_bps", "最高單介面鏈路速率", "網路", "所有活動網路介面中最高的單個鏈路速率。", unit: "bit/s", use: "左側資訊", formats: "0"),
        V("network.utilization_percent", "網路鏈路利用率", "網路", "系統總傳輸速率與活動介面總鏈路速率的近似比例。", unit: "%", use: "右側狀態 / 圓環"),
        V("network.total_received_bytes", "累計接收流量", "網路", "目前開機期間所有活動網路介面累計接收位元組數之和。", unit: "Byte", use: "左側資訊", formats: "bytes / gb:1"),
        V("network.total_sent_bytes", "累計傳送流量", "網路", "目前開機期間所有活動網路介面累計傳送位元組數之和。", unit: "Byte", use: "左側資訊", formats: "bytes / gb:1"),
        V("network.total_transferred_bytes", "累計總流量", "網路", "累計接收與傳送流量之和。", unit: "Byte", use: "左側資訊", formats: "bytes / gb:1"),
        V("network.active_interface_count", "活動網路介面數量", "網路", "目前處於 Up 狀態且非迴環的網路介面數量。", "整數", "個", "左側資訊", "0"),
        V("network.interface_names", "活動網路介面名稱", "網路", "所有活動網路介面名稱，以逗號分隔。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),
        V("network.interface_types", "活動網路介面型別", "網路", "活動網路介面型別清單，如 Wireless80211 / Ethernet。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),
        V("network.ipv4_addresses", "本機 IPv4 地址", "網路", "活動網路介面上的 IPv4 單播地址清單。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("network.ipv6_addresses", "本機 IPv6 地址", "網路", "活動網路介面上的 IPv6 單播地址清單。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("network.default_gateways", "預設閘道器", "網路", "活動網路介面設定的閘道器地址清單。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("network.dns_servers", "DNS 伺服器", "網路", "活動網路介面設定的 DNS 伺服器地址清單。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("network.available", "網路可用狀態", "網路", "Windows 是否偵測到至少一個可用網路連線；不代表一定可訪問網際網路。", "布林", use: "標題 / 條件", formats: "無需格式化"),
        V("network.packets_received", "累計接收資料包", "網路", "活動網路介面累計接收單播資料包數量。", "數值", "包", "左側資訊", "0"),
        V("network.packets_sent", "累計傳送資料包", "網路", "活動網路介面累計傳送單播資料包數量。", "數值", "包", "左側資訊", "0"),
        V("network.receive_errors", "接收錯誤", "網路", "活動網路介面累計接收錯誤數量。", "數值", "個", "狀態 / 除錯", "0"),
        V("network.send_errors", "傳送錯誤", "網路", "活動網路介面累計傳送錯誤數量。", "數值", "個", "狀態 / 除錯", "0"),

        // ===== Ping / 即時連通性 =====
        V("probe.target", "探測地址", "網路連線測試", "目前網路連線測試使用的 IPv4、IPv6 或網域名稱。", "文字", use: "左側資訊 / 標題", formats: "無需格式化"),
        V("probe.address", "實際回應地址", "網路連線測試", "目標解析後實際參與偵測或回傳回應的 IP 地址。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("probe.protocol", "測試協定", "網路連線測試", "目前使用的測試協定：ICMP、TCP 或 UDP。", "文字", use: "左側資訊 / 標題", formats: "無需格式化"),
        V("probe.port", "偵測連接埠", "網路連線測試", "TCP / UDP 偵測連接埠；ICMP 模式回傳 0。", "整數", use: "左側資訊", formats: "0"),
        V("probe.endpoint", "探測端點", "網路連線測試", "ICMP 顯示目標地址；TCP / UDP 顯示“地址:連接埠”。", "文字", use: "左側資訊 / 標題", formats: "無需格式化"),
        V("probe.online", "探測是否成功", "網路連線測試", "最近一次網路連線測試是否成功。", "布林", use: "狀態 / 條件", formats: "無需格式化"),
        V("probe.status_text", "探測狀態文字", "網路連線測試", "最近一次探測狀態，例如“線上”“逾時”“連接埠不可達”。", "文字", use: "右側狀態 / 標題", formats: "無需格式化"),
        V("probe.reply_status", "探測原始狀態", "網路連線測試", "協定探測回傳的底層狀態，例如 Success、Connected、Response、TimedOut。", "文字", use: "狀態 / 除錯", formats: "無需格式化"),
        V("probe.latency_ms", "探測延遲", "網路連線測試", "最近一次成功探測的往返/連線延遲；失敗時按 999 ms 處理。", unit: "ms", use: "左側主要資訊", formats: "0 / 0.0"),
        V("probe.latency_text", "探測延遲文字", "網路連線測試", "成功時顯示“20ms”；失敗時顯示逾時或錯誤狀態。", "文字", use: "左側主要資訊", formats: "無需格式化"),
        V("probe.latency_progress", "延遲進度", "網路連線測試", "0 ms 為 0%，999 ms 及以上為 100%；適合用作延遲圓環。", unit: "%", use: "圓環"),
        V("probe.full_scale_ms", "延遲 100% 對應值", "網路連線測試", "延遲圓環 100% 對應 999 ms。", unit: "ms", use: "狀態計算", formats: "0"),
        V("probe.timeout_ms", "單次探測逾時", "網路連線測試", "單次 ICMP/TCP/UDP 探測等待回應的逾時時間。", unit: "ms", use: "狀態計算", formats: "0"),
        V("probe.ttl", "ICMP TTL", "網路連線測試", "最近一次 ICMP 成功回應的 TTL；TCP/UDP 為 0。", "整數", use: "左側資訊", formats: "0"),
        V("probe.sent", "探測樣本數", "網路連線測試", "目前目標最近滾動統計視窗內的探測樣本數量。", "整數", "次", "左側資訊", "0"),
        V("probe.received", "成功樣本數", "網路連線測試", "目前目標最近滾動統計視窗內成功的探測次數。", "整數", "次", "左側資訊", "0"),
        V("probe.lost", "失敗樣本數", "網路連線測試", "目前目標最近滾動統計視窗內失敗/逾時次數。", "整數", "次", "左側資訊", "0"),
        V("probe.loss_percent", "封包遺失率", "網路連線測試", "目前地址、協定和連接埠組合最近 20 次探測的失敗/逾時比例。", unit: "%", use: "右側狀態 / 圓環"),
        V("probe.avg_latency_ms", "平均延遲", "網路連線測試", "目前探測設定最近 20 次成功樣本的平均延遲。", unit: "ms", use: "左側資訊", formats: "0.0"),
        V("probe.min_latency_ms", "最低延遲", "網路連線測試", "目前探測設定最近 20 次成功樣本中的最低延遲。", unit: "ms", use: "左側資訊", formats: "0"),
        V("probe.max_latency_ms", "最高延遲", "網路連線測試", "目前探測設定最近 20 次成功樣本中的最高延遲。", unit: "ms", use: "左側資訊", formats: "0"),
        V("probe.jitter_ms", "延遲抖動", "網路連線測試", "相鄰成功樣本延遲差的平均絕對值，用於近似表示抖動。", unit: "ms", use: "左側資訊", formats: "0.0"),
        V("probe.last_success", "最近成功時間", "網路連線測試", "目前探測設定最近一次成功的本地時間。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("probe.error", "探測錯誤", "網路連線測試", "最近一次偵測失敗時的底層狀態或錯誤。", "文字", use: "狀態 / 除錯", formats: "無需格式化"),

        V("ping.target", "Ping 目標", "Ping（相容）", "目前方案正在偵測的 IP 地址或網域名稱。", "文字", use: "左側主要數值 / 標題", formats: "無需格式化"),
        V("ping.address", "Ping 實際地址", "Ping（相容）", "Ping 回應回傳的實際 IP 地址；網域名稱目標解析成功後可檢視最終地址。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("ping.protocol", "Ping/探測協定", "Ping（相容）", "相容變數：目前網路連線測試協定 ICMP、TCP 或 UDP。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("ping.port", "Ping/探測連接埠", "Ping（相容）", "相容變數：TCP / UDP 連接埠；ICMP 為 0。", "整數", use: "左側資訊", formats: "0"),
        V("ping.endpoint", "Ping/探測端點", "Ping（相容）", "相容變數：目前地址和連接埠組合。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("ping.online", "Ping 是否線上", "Ping（相容）", "最近一次 ICMP Ping 是否成功。", "布林", use: "狀態 / 條件", formats: "無需格式化"),
        V("ping.status_text", "Ping 狀態文字", "Ping（相容）", "最近一次偵測狀態，例如“線上”“逾時”“不可達”。", "文字", use: "右側狀態 / 標題", formats: "無需格式化"),
        V("ping.reply_status", "Ping 原始狀態", "Ping（相容）", "System.Net.NetworkInformation.IPStatus 回傳的原始狀態名稱。", "文字", use: "狀態 / 除錯", formats: "無需格式化"),
        V("ping.latency_ms", "Ping 延遲", "Ping（相容）", "最近一次成功 Ping 的往返延遲；失敗時按 999 ms 計入進度。", unit: "ms", use: "右側狀態", formats: "0 / 0.0"),
        V("ping.latency_text", "Ping 延遲文字", "Ping（相容）", "成功時顯示“20ms”一類文字；逾時或不可達時顯示對應狀態。", "文字", use: "右側狀態", formats: "無需格式化"),
        V("ping.progress", "Ping 延遲進度", "Ping（相容）", "延遲換算後的圓環進度：0 ms 為 0%，999 ms 及以上為 100%；逾時/失敗為 100%。", unit: "%", use: "右側狀態 / 圓環"),
        V("ping.full_scale_ms", "Ping 100% 對應延遲", "Ping（相容）", "Ping 圓環 100% 對應的延遲，固定為 999 ms。", unit: "ms", use: "狀態計算", formats: "0"),
        V("ping.timeout_ms", "Ping 逾時時間", "Ping（相容）", "單次 Ping 等待回應的逾時時間。", unit: "ms", use: "狀態計算", formats: "0"),
        V("ping.ttl", "Ping TTL", "Ping（相容）", "最近一次成功回應回傳的 TTL。", "整數", use: "左側資訊", formats: "0"),
        V("ping.sent", "Ping 統計傳送次數", "Ping（相容）", "目前目標最近滾動統計視窗內的 Ping 樣本數量。", "整數", "次", "左側資訊", "0"),
        V("ping.received", "Ping 統計成功次數", "Ping（相容）", "目前目標最近滾動統計視窗內成功的 Ping 樣本數量。", "整數", "次", "左側資訊", "0"),
        V("ping.lost", "Ping 封包遺失次數", "Ping（相容）", "目前目標最近滾動統計視窗內失敗/逾時的 Ping 樣本數量。", "整數", "次", "左側資訊", "0"),
        V("ping.loss_percent", "Ping 封包遺失率", "Ping（相容）", "目前目標最近 20 次偵測的封包遺失率。", unit: "%", use: "右側狀態 / 圓環"),
        V("ping.avg_latency_ms", "Ping 平均延遲", "Ping（相容）", "目前目標最近 20 次成功偵測的平均延遲。", unit: "ms", use: "左側資訊", formats: "0.0"),
        V("ping.min_latency_ms", "Ping 最低延遲", "Ping（相容）", "目前目標最近 20 次成功偵測中的最低延遲。", unit: "ms", use: "左側資訊", formats: "0"),
        V("ping.max_latency_ms", "Ping 最高延遲", "Ping（相容）", "目前目標最近 20 次成功偵測中的最高延遲。", unit: "ms", use: "左側資訊", formats: "0"),
        V("ping.jitter_ms", "Ping 抖動", "Ping（相容）", "目前目標最近成功樣本相鄰延遲差的平均絕對值，用於近似表示網路抖動。", unit: "ms", use: "左側資訊", formats: "0.0"),
        V("ping.last_success", "Ping 最近成功時間", "Ping（相容）", "目前目標最近一次成功回應的本地時間。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("ping.error", "Ping 錯誤資訊", "Ping（相容）", "最近一次偵測失敗時的錯誤或狀態說明。", "文字", use: "狀態 / 除錯", formats: "無需格式化"),

        // ===== 磁碟 =====
        V("disk.system.root", "系統磁碟磁碟機代號", "磁碟", "Windows 系統目錄所在驅動器根路徑，如 C:\\。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),
        V("disk.system.label", "系統磁碟卷標", "磁碟", "系統磁碟卷標。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),
        V("disk.system.filesystem", "系統磁碟檔案系統", "磁碟", "系統磁碟檔案系統，如 NTFS。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),
        V("disk.system.used_bytes", "系統磁碟已用空間", "磁碟", "Windows 系統磁碟已使用空間。", unit: "Byte", use: "左側主要數值", formats: "gb:1 / bytes"),
        V("disk.system.free_bytes", "系統磁碟可用空間", "磁碟", "Windows 系統磁碟可用空間。", unit: "Byte", use: "左側資訊", formats: "gb:1 / bytes"),
        V("disk.system.total_bytes", "系統磁碟總容量", "磁碟", "Windows 系統磁碟總容量。", unit: "Byte", use: "左側次要數值", formats: "gb:1 / bytes"),
        V("disk.system.usage", "系統磁碟使用率", "磁碟", "系統磁碟已用空間佔總容量的比例。", unit: "%", use: "右側狀態 / 圓環"),
        V("disk.system.free_percent", "系統磁碟空閒率", "磁碟", "系統磁碟可用空間佔總容量的比例。", unit: "%", use: "右側狀態 / 圓環"),
        V("disk.system.read_bps", "系統磁碟讀取速度", "磁碟", "Windows 邏輯磁碟效能計數器報告的系統磁碟讀取速度。", unit: "Byte/s", use: "左側主要數值", formats: "speed"),
        V("disk.system.write_bps", "系統磁碟寫入速度", "磁碟", "Windows 邏輯磁碟效能計數器報告的系統磁碟寫入速度。", unit: "Byte/s", use: "左側次要數值", formats: "speed"),
        V("disk.system.io_bps", "系統磁碟總 IO 速度", "磁碟", "系統磁碟讀取速度與寫入速度之和。", unit: "Byte/s", use: "左側資訊", formats: "speed"),
        V("disk.system.active_percent", "系統磁碟活動時間", "磁碟", "系統磁碟效能計數器報告的磁碟活動時間百分比。", unit: "%", use: "右側狀態 / 圓環"),
        V("disk.system.queue_length", "系統磁碟佇列長度", "磁碟", "系統磁碟目前磁碟佇列長度。", unit: "項", use: "左側資訊", formats: "0.0"),
        V("disk.fixed.count", "固定磁碟數量", "磁碟", "目前已就緒的固定磁碟卷數量。", "整數", "個", "左側資訊", "0"),
        V("disk.fixed.used_bytes", "所有固定磁碟已用空間", "磁碟", "所有已就緒固定磁碟卷的已用空間總和。", unit: "Byte", use: "左側主要數值", formats: "gb:1 / bytes"),
        V("disk.fixed.free_bytes", "所有固定磁碟可用空間", "磁碟", "所有已就緒固定磁碟卷的可用空間總和。", unit: "Byte", use: "左側資訊", formats: "gb:1 / bytes"),
        V("disk.fixed.total_bytes", "所有固定磁碟總容量", "磁碟", "所有已就緒固定磁碟卷的總容量。", unit: "Byte", use: "左側次要數值", formats: "gb:1 / bytes"),
        V("disk.fixed.usage", "所有固定磁碟總體使用率", "磁碟", "所有固定磁碟已用空間佔總容量的比例。", unit: "%", use: "右側狀態 / 圓環"),
        V("disk.fixed.list", "固定磁碟清單", "磁碟", "已就緒固定磁碟磁碟機代號清單。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),

        // ===== 系統 =====
        V("system.time", "目前時間", "系統", "本機目前時間，精確到秒。", "文字", use: "左側資訊 / 標題", formats: "無需格式化"),
        V("system.date", "目前日期", "系統", "本機目前日期。", "文字", use: "左側資訊 / 標題", formats: "無需格式化"),
        V("system.datetime", "目前日期時間", "系統", "本機目前日期與時間。", "文字", use: "左側資訊 / 標題", formats: "無需格式化"),
        V("system.uptime_seconds", "系統執行時間", "系統", "目前 Windows 自開機以來執行的秒數。", unit: "秒", use: "左側資訊", formats: "duration"),
        V("system.uptime_text", "系統執行時間文字", "系統", "系統執行時間的可讀文字。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("system.boot_time", "系統啟動時間", "系統", "根據系統執行時間估算的本次 Windows 啟動時間。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("system.machine_name", "計算機名稱", "系統", "Windows 計算機名稱。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),
        V("system.host_name", "主機名", "系統", "DNS 主機名。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),
        V("system.user_name", "目前使用者名稱", "系統", "目前 Windows 使用者名稱。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),
        V("system.user_domain", "目前使用者域", "系統", "目前使用者所屬 Windows 域/計算機名。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),
        V("system.os_description", "作業系統名稱", "系統", ".NET RuntimeInformation 提供的 Windows 作業系統描述。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),
        V("system.os_version", "作業系統版本", "系統", "Windows 作業系統版本編號。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("system.os_build", "Windows Build", "系統", "目前 Windows Build 號。", "整數", use: "左側資訊", formats: "0"),
        V("system.os_architecture", "作業系統架構", "系統", "目前 Windows 架構，如 X64 / Arm64。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("system.process_architecture", "應用程式架構", "系統", "終末地 靈動島 目前處理程序架構。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("system.framework_version", ".NET 版本", "系統", "目前處理程序使用的 .NET FrameworkDescription。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("system.processor_count", "系統邏輯處理器數", "系統", "Environment.ProcessorCount。", "整數", "個", "左側資訊", "0"),
        V("system.timezone_id", "時區 ID", "系統", "目前本地時區 ID。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),
        V("system.timezone_name", "時區名稱", "系統", "目前本地時區顯示名稱。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),
        V("system.utc_offset_hours", "UTC 時差", "系統", "目前時區相對 UTC 的小時偏移。", unit: "小時", use: "左側資訊", formats: "0.0"),
        V("system.culture", "系統區域語言", "系統", "目前處理程序文化區網域名稱稱，例如 zh-TW。", "文字", use: "左側資訊", formats: "無需格式化"),

        // ===== 應用程式 =====
        V("app.name", "應用程式名稱", "ECP 應用", "目前 終末地 靈動島 處理程序名稱。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),
        V("app.version", "應用版本", "ECP 應用", "終末地 靈動島 產品版本。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),
        V("app.pid", "應用 PID", "ECP 應用", "目前應用程式 ID。", "整數", use: "左側資訊", formats: "0"),
        V("app.start_time", "應用啟動時間", "ECP 應用", "目前應用程式啟動時間。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("app.uptime_seconds", "應用執行時間", "ECP 應用", "目前應用程式已執行的秒數。", unit: "秒", use: "左側資訊", formats: "duration"),
        V("app.uptime_text", "應用執行時間文字", "ECP 應用", "目前應用執行時間的可讀文字。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("app.working_set_bytes", "應用工作集", "ECP 應用", "終末地 靈動島 目前工作集記憶體。", unit: "Byte", use: "左側主要數值", formats: "mb:1 / bytes"),
        V("app.private_memory_bytes", "應用專用記憶體", "ECP 應用", "目前應用程式專用記憶體。", unit: "Byte", use: "左側資訊", formats: "mb:1 / bytes"),
        V("app.virtual_memory_bytes", "應用虛擬記憶體", "ECP 應用", "目前應用程式虛擬記憶體大小。", unit: "Byte", use: "左側資訊", formats: "mb:1 / bytes"),
        V("app.thread_count", "應用執行緒數", "ECP 應用", "目前應用程式執行緒數量。", "整數", "個", "左側資訊", "0"),
        V("app.handle_count", "應用控制代碼數", "ECP 應用", "目前應用程式開啟的 Windows 控制代碼數量。", "整數", "個", "左側資訊", "0"),
        V("app.cpu_time_seconds", "應用累計 CPU 時間", "ECP 應用", "目前應用程式累計消耗的處理器時間。", unit: "秒", use: "左側資訊", formats: "duration"),

        // ===== 螢幕 =====
        V("display.primary_width_px", "主螢幕寬度", "螢幕", "Windows 主螢幕寬度。", "整數", "px", "左側資訊", "0"),
        V("display.primary_height_px", "主螢幕高度", "螢幕", "Windows 主螢幕高度。", "整數", "px", "左側資訊", "0"),
        V("display.virtual_x_px", "虛擬桌面 X 起點", "螢幕", "多螢幕虛擬桌面的左邊界座標。", "整數", "px", "左側資訊", "0"),
        V("display.virtual_y_px", "虛擬桌面 Y 起點", "螢幕", "多螢幕虛擬桌面的上邊界座標。", "整數", "px", "左側資訊", "0"),
        V("display.virtual_width_px", "虛擬桌面寬度", "螢幕", "多螢幕虛擬桌面總寬度。", "整數", "px", "左側資訊", "0"),
        V("display.virtual_height_px", "虛擬桌面高度", "螢幕", "多螢幕虛擬桌面總高度。", "整數", "px", "左側資訊", "0"),
        V("display.monitor_count", "螢幕數量", "螢幕", "Windows 目前偵測到的螢幕數量。", "整數", "臺", "左側資訊", "0"),
        V("display.system_dpi", "系統 DPI", "螢幕", "Windows 系統 DPI。", "整數", "DPI", "左側資訊", "0"),
        V("display.scale_percent", "系統縮放比例", "螢幕", "根據系統 DPI 換算的顯示縮放百分比。", unit: "%", use: "右側狀態 / 圓環"),

        // ===== 時間 =====
        V("time.current", "目前時間（24 小時）", "時間", "本地目前時間，格式 HH:mm:ss。", "文字", use: "左側主要數值", formats: "無需格式化"),
        V("time.current_12h", "目前時間（12 小時）", "時間", "本地目前時間，12 小時制幷包含 AM/PM。", "文字", use: "左側主要數值", formats: "無需格式化"),
        V("time.date", "目前日期", "時間", "本地目前日期，格式 yyyy-MM-dd。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),
        V("time.datetime", "目前日期時間", "時間", "本地目前日期時間。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),
        V("time.iso", "ISO 日期時間", "時間", "目前本地時間的 ISO 8601 表示。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("time.year", "年份", "時間", "目前年份。", "整數", "年", "左側資訊", "0"),
        V("time.month", "月份", "時間", "目前月份數字。", "整數", "月", "左側資訊", "0"),
        V("time.month_name", "月份名稱", "時間", "目前文化區域下的月份名稱。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),
        V("time.day", "日期（日）", "時間", "目前月中的日期。", "整數", "日", "左側資訊", "0"),
        V("time.day_of_week", "星期（中文）", "時間", "目前星期的中文名稱。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),
        V("time.day_of_week_en", "星期（英文）", "時間", "目前星期英文名稱。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),
        V("time.day_of_year", "一年中的第幾天", "時間", "目前日期是一年中的第幾天。", "整數", "天", "左側資訊", "0"),
        V("time.week_of_year", "周序號", "時間", "ISO 風格的目前周序號。", "整數", "周", "左側資訊", "0"),
        V("time.hour", "小時", "時間", "目前小時（0-23）。", "整數", "時", "左側資訊", "0"),
        V("time.minute", "分鐘", "時間", "目前分鐘。", "整數", "分", "左側資訊", "0"),
        V("time.second", "秒", "時間", "目前秒。", "整數", "秒", "左側資訊", "0"),
        V("time.millisecond", "毫秒", "時間", "目前毫秒。", "整數", "ms", "左側資訊", "0"),
        V("time.is_weekend", "是否週末", "時間", "目前日期是否為週六或週日。", "布林", use: "標題 / 條件", formats: "無需格式化"),
        V("time.unix_seconds", "Unix 時間戳（秒）", "時間", "目前 Unix 時間戳，單位秒。", "數值", "秒", "左側資訊", "0"),
        V("time.unix_milliseconds", "Unix 時間戳（毫秒）", "時間", "目前 Unix 時間戳，單位毫秒。", "數值", "ms", "左側資訊", "0"),
        V("time.day.progress", "當日進度", "時間", "從當天 00:00:00 到次日 00:00:00 已經過的百分比。", unit: "%", use: "右側狀態 / 圓環"),
        V("time.day.elapsed_seconds", "當天已過時間", "時間", "當天已經過的秒數。", unit: "秒", use: "左側資訊", formats: "duration"),
        V("time.day.remaining_seconds", "當天剩餘時間", "時間", "距離次日 00:00:00 的剩餘秒數。", unit: "秒", use: "左側資訊", formats: "duration"),
        V("time.week.progress", "本週進度", "時間", "從本週週一 00:00 到下週週一 00:00 的已過百分比。", unit: "%", use: "右側狀態 / 圓環"),
        V("time.week.elapsed_seconds", "本週已過時間", "時間", "從本週週一開始已經過的秒數。", unit: "秒", use: "左側資訊", formats: "duration"),
        V("time.week.remaining_seconds", "本週剩餘時間", "時間", "距離下週週一的剩餘秒數。", unit: "秒", use: "左側資訊", formats: "duration"),
        V("time.month.progress", "本月進度", "時間", "從本月 1 日 00:00 到下月 1 日 00:00 的已過百分比。", unit: "%", use: "右側狀態 / 圓環"),
        V("time.month.elapsed_seconds", "本月已過時間", "時間", "本月已經過的秒數。", unit: "秒", use: "左側資訊", formats: "duration"),
        V("time.month.remaining_seconds", "本月剩餘時間", "時間", "距離下月 1 日的剩餘秒數。", unit: "秒", use: "左側資訊", formats: "duration"),
        V("time.year.progress", "本年進度", "時間", "從今年 1 月 1 日到明年 1 月 1 日的已過百分比。", unit: "%", use: "右側狀態 / 圓環"),
        V("time.year.elapsed_seconds", "本年已過時間", "時間", "今年已經過的秒數。", unit: "秒", use: "左側資訊", formats: "duration"),
        V("time.year.remaining_seconds", "本年剩餘時間", "時間", "距離明年 1 月 1 日的剩餘秒數。", unit: "秒", use: "左側資訊", formats: "duration"),
        V("time.display.progress", "時間方案顯示進度", "時間", "時間方案用於圓環的進度；目標時間模式下為剩餘比例。", unit: "%", use: "右側狀態 / 圓環"),
        V("time.display.status_text", "時間方案狀態文字", "時間", "普通模式顯示當日進度；目標時間模式顯示“剩餘??%”。", "文字", use: "右側狀態", formats: "無需格式化"),
        V("time.target.value", "目標時間", "時間", "目前時間方案設定的每日目標時間。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),
        V("time.target.remaining_seconds", "距離目標時間", "時間", "距離下一次每日目標時間的剩餘秒數。", unit: "秒", use: "左側資訊", formats: "duration"),
        V("time.target.remaining_percent", "目標時間剩餘比例", "時間", "距離下一次目標時間的剩餘時間長度佔 24 小時的百分比。", unit: "%", use: "右側狀態 / 圓環"),
        V("time.target.progress", "目標時間已過比例", "時間", "每日目標時間週期中已經過的比例。", unit: "%", use: "右側狀態 / 圓環"),
        V("time.target.remaining_text", "目標時間剩餘文字", "時間", "按“剩餘??%”生成的目標時間狀態文字。", "文字", use: "右側狀態", formats: "無需格式化"),
    };

    private static readonly IReadOnlyList<VariableDefinition> AdvancedBuiltIns = new List<VariableDefinition>
    {
        // ===== 電池進階 =====
        V("battery.estimated_time_to_empty", "預計耗盡剩餘時間", "電池", "Windows 電源管理估算的距離電池耗盡剩餘秒數；系統無法估算時變數不可用。", unit: "秒", use: "左側資訊", formats: "duration / duration-long"),
        V("battery.estimated_time_to_full", "預計充滿剩餘時間", "電池", "根據目前剩餘容量、滿充容量和即時充電功率計算的預計充滿時間；僅充電功率可用時提供。", unit: "秒", use: "左側資訊", formats: "duration / duration-long"),
        V("battery.design_vs_current_health", "設計容量健康度", "電池", "目前滿充容量相對設計容量的比例，與電池健康度一致。", unit: "%", use: "右側狀態 / 圓環"),
        V("battery.temperature", "電池溫度", "電池", "透過 Windows BatteryTemperature 介面讀取；僅驅動/韌體提供時可用。", unit: "°C", use: "左側資訊", formats: "0.0"),
        V("battery.chemistry", "電池化學型別", "電池", "Win32_Battery 報告的電池化學體系，如鋰離子/鋰聚合物。", "文字", use: "左側資訊", formats: "無需格式化"),

        // ===== CPU 進階 =====
        V("cpu.usage_avg_1m", "CPU 1 分鐘平均使用率", "CPU", "該變數啟用後滾動統計最近 1 分鐘 CPU 使用率。", unit: "%", use: "右側狀態 / 圓環"),
        V("cpu.usage_avg_5m", "CPU 5 分鐘平均使用率", "CPU", "該變數啟用後滾動統計最近 5 分鐘 CPU 使用率。", unit: "%", use: "右側狀態 / 圓環"),
        V("cpu.usage_avg_15m", "CPU 15 分鐘平均使用率", "CPU", "該變數啟用後滾動統計最近 15 分鐘 CPU 使用率。", unit: "%", use: "右側狀態 / 圓環"),
        V("cpu.usage_max", "CPU 近期最高使用率", "CPU", "最近最多 15 分鐘取樣視窗內觀察到的 CPU 最高使用率。", unit: "%", use: "右側狀態 / 圓環"),
        V("cpu.temperature_max", "CPU 最高溫度", "CPU", "LibreHardwareMonitor 讀取的 CPU 目前最高溫度；硬體/驅動不支援時不可用。", unit: "°C", use: "右側狀態 / 圓環", formats: "0.0"),
        V("cpu.power_max", "CPU 功耗", "CPU", "LibreHardwareMonitor 目前 CPU 功率感測器的最高有效值。", unit: "W", use: "左側資訊", formats: "0.0"),
        V("cpu.core.temperature_avg", "CPU 核心平均溫度", "CPU", "可用核心溫度感測器的即時平均值。", unit: "°C", use: "左側資訊", formats: "0.0"),
        V("cpu.core.voltage", "CPU 核心電壓", "CPU", "硬體監控感測器報告的 CPU 核心/最高有效電壓。", unit: "V", use: "左側資訊", formats: "0.000"),
        V("cpu.bus_speed", "CPU 匯流排/BCLK", "CPU", "硬體監控感測器報告的 Bus/BCLK 頻率。", unit: "MHz", use: "左側資訊", formats: "0.0"),
        V("cpu.instructions_per_second", "CPU 每秒退休指令", "CPU", "Windows Processor Information 效能計數器 InstructionsRetiredPersec；僅系統提供該計數器時可用。", unit: "instr/s", use: "左側資訊", formats: "auto:1"),
        V("cpu.context_switches", "上下文切換速率", "CPU", "Windows System 效能計數器 Context Switches/sec。", unit: "次/s", use: "左側資訊", formats: "0"),
        V("cpu.interrupts", "硬體中斷速率", "CPU", "Windows Processor Information 效能計數器 Interrupts/sec。", unit: "次/s", use: "左側資訊", formats: "0"),
        V("cpu.dpc_time", "DPC 時間比例", "CPU", "Windows Processor Information 的 % DPC Time。", unit: "%", use: "右側狀態 / 圓環", formats: "0.0"),
        V("cpu.system_calls", "系統呼叫速率", "CPU", "Windows System 效能計數器 System Calls/sec。", unit: "次/s", use: "左側資訊", formats: "0"),

        // ===== 記憶體進階 =====
        V("memory.standby_bytes", "Standby 快取", "記憶體", "Windows Memory 效能計數器中 Standby Cache Core/Normal/Reserve 的合計。", unit: "Byte", use: "左側資訊", formats: "auto:1 / gb:1"),
        V("memory.modified_bytes", "Modified 頁面", "記憶體", "Windows Modified Page List Bytes。", unit: "Byte", use: "左側資訊", formats: "auto:1 / mb:0"),
        V("memory.hardware_reserved_bytes", "硬體保留記憶體", "記憶體", "實體記憶體條安裝容量與 Windows 可用實體記憶體總量之差。", unit: "Byte", use: "左側資訊", formats: "auto:1 / mb:0"),
        V("memory.speed_mhz", "記憶體工作頻率", "記憶體", "Win32_PhysicalMemory 報告的最高 ConfiguredClockSpeed/Speed。", unit: "MHz", use: "左側資訊", formats: "0"),
        V("memory.slot_count", "記憶體插槽總數", "記憶體", "Win32_PhysicalMemoryArray 報告的記憶體裝置插槽總數。", "整數", "個", "左側資訊", "0"),
        V("memory.slot_used", "已使用記憶體插槽", "記憶體", "目前列舉到的實體記憶體模組數量。", "整數", "個", "左側資訊", "0"),
        V("memory.form_factor", "記憶體形態", "記憶體", "記憶體模組 FormFactor，例如 DIMM / SODIMM。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("memory.type", "記憶體型別", "記憶體", "SMBIOS 報告的記憶體型別，例如 DDR4 / DDR5 / LPDDR5。", "文字", use: "左側資訊", formats: "無需格式化"),

        // ===== GPU 進階 =====
        V("gpu.temperature", "GPU 核心溫度", "GPU", "硬體監控感測器報告的 GPU Core 溫度；硬體或驅動不提供時變數不可用。", unit: "°C", use: "左側資訊", formats: "0.0"),
        V("gpu.hotspot_temperature", "GPU 熱點溫度", "GPU", "硬體監控感測器報告的 GPU Hot Spot/核心熱點溫度。", unit: "°C", use: "右側狀態 / 圓環", formats: "0.0"),
        V("gpu.memory_junction_temperature", "GPU 視訊記憶體結溫", "GPU", "硬體監控感測器報告的視訊記憶體/Memory Junction 溫度。", unit: "°C", use: "左側資訊", formats: "0.0"),
        V("gpu.power_w", "GPU 目前功率", "GPU", "LibreHardwareMonitor 即時功率感測器值；僅硬體/驅動提供時可用。", unit: "W", use: "左側資訊", formats: "0.0"),
        V("gpu.power_limit_w", "GPU 功率限制", "GPU", "NVIDIA GPU 透過 nvidia-smi power.limit 讀取；其他 GPU 若沒有可靠功率限制介面則不提供。", unit: "W", use: "左側資訊", formats: "0.0"),
        V("gpu.voltage_v", "GPU 核心電壓", "GPU", "硬體監控感測器報告的 GPU 核心電壓。", unit: "V", use: "左側資訊", formats: "0.000"),
        V("gpu.core_clock_mhz", "GPU 核心頻率", "GPU", "目前 GPU 核心即時頻率。", unit: "MHz", use: "左側資訊", formats: "0"),
        V("gpu.memory_clock_mhz", "GPU 視訊記憶體頻率", "GPU", "目前 GPU 視訊記憶體即時頻率。", unit: "MHz", use: "左側資訊", formats: "0"),
        V("gpu.pcie_gen", "GPU PCIe 代際", "GPU", "nvidia-smi 提供的目前 PCIe Link Generation；NVIDIA 且 nvidia-smi 可用時提供。", "數值", "Gen", "左側資訊", "0"),
        V("gpu.pcie_lanes", "GPU PCIe 通道數", "GPU", "nvidia-smi 提供的目前 PCIe Link Width。", "數值", "lane", "左側資訊", "0"),
        V("gpu.driver_version", "GPU 驅動版本", "GPU", "Win32_VideoController 報告的所選 GPU 驅動版本。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("gpu.driver_date", "GPU 驅動日期", "GPU", "Win32_VideoController 報告的驅動日期。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("gpu.bios_version", "GPU VBIOS 版本", "GPU", "nvidia-smi 報告的 VBIOS 版本；支援時可用。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("gpu.nvidia_smi_available", "nvidia-smi 可用", "GPU", "系統是否存在可呼叫的 nvidia-smi.exe。", "布林", use: "條件", formats: "無需格式化"),
        V("gpu.amd_adrenalin_available", "AMD Adrenalin 可用", "GPU", "偵測 AMD Radeon Software 處理程序或預設安裝路徑。", "布林", use: "條件", formats: "無需格式化"),

        // ===== 網路進階 =====
        V("network.signal_dbm", "Wi-Fi 訊號強度", "網路", "根據 Windows WLAN 訊號質量換算的近似 dBm；有 Wi-Fi 連線時可用。", unit: "dBm", use: "左側資訊", formats: "0"),
        V("network.public_ipv4", "公網 IPv4", "網路", "透過 api.ipify.org 查詢的公網 IPv4，5 分鐘快取；僅使用該變數時才發起請求。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("network.public_ipv6", "公網 IPv6", "網路", "透過 api6.ipify.org 查詢的公網 IPv6，5 分鐘快取；沒有 IPv6 時變數不可用。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("network.vpn_status", "VPN 狀態", "網路", "根據活動 Tunnel/PPP/WireGuard/Wintun 等網路介面偵測 VPN 是否活動。", "布林", use: "狀態 / 條件", formats: "無需格式化"),
        V("network.vpn_name", "VPN 名稱", "網路", "目前偵測到的活動 VPN 介面名稱。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("network.proxy_status", "系統代理狀態", "網路", "目前使用者 Internet Settings 的 ProxyEnable 狀態。", "布林", use: "狀態 / 條件", formats: "無需格式化"),
        V("network.proxy_address", "系統代理地址", "網路", "目前使用者設定的 ProxyServer。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("network.dns_latency_ms", "系統 DNS 解析耗時", "網路", "本機系統解析 one.one.one.one 的耗時；可能受 DNS 快取影響。", unit: "ms", use: "右側狀態 / 圓環", formats: "0.0"),
        V("network.tcp_connections", "活動 TCP 連線數", "網路", "IPGlobalProperties 回傳的活動 TCP 連線數量。", "整數", "條", "左側資訊", "0"),
        V("network.udp_connections", "UDP 監聽端點數", "網路", "IPGlobalProperties 回傳的活動 UDP 監聽端點數量。", "整數", "個", "左側資訊", "0"),
        V("network.wifi_channel", "Wi-Fi 通道", "網路", "目前 WLAN 介面的無線通道。", "數值", use: "左側資訊", formats: "0"),
        V("network.wifi_band", "Wi-Fi 頻段", "網路", "目前 WLAN 介面的頻段；新系統直接讀取 Band，舊系統在可推斷時回退。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("network.wifi_standard", "Wi-Fi 標準", "網路", "目前 WLAN Radio type，例如 802.11ax。", "文字", use: "左側資訊", formats: "無需格式化"),

        // ===== 系統進階 =====
        V("system.power_plan", "電源計劃 GUID", "系統", "powercfg 目前活動電源方案 GUID。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("system.power_plan_name", "電源計劃名稱", "系統", "目前 Windows 活動電源計劃名稱。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("system.bios_version", "BIOS 版本", "系統", "Win32_BIOS SMBIOSBIOSVersion。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("system.bios_date", "BIOS 日期", "系統", "Win32_BIOS ReleaseDate。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("system.motherboard_manufacturer", "主機板製造商", "系統", "Win32_BaseBoard Manufacturer。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("system.motherboard_model", "主機板型號", "系統", "Win32_BaseBoard Product。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("system.motherboard_temperature", "主機板最高溫度", "系統", "LibreHardwareMonitor 可讀取的主機板溫度感測器最高值。", unit: "°C", use: "左側資訊", formats: "0.0"),
        V("system.fan_speed", "風扇最高轉速", "系統", "LibreHardwareMonitor 目前可見風扇的最高 RPM。", unit: "RPM", use: "左側資訊", formats: "0"),
        V("system.fan_speed_percent", "風扇控制百分比", "系統", "硬體監控 Control 型別感測器的最高百分比。", unit: "%", use: "右側狀態 / 圓環", formats: "0"),
        V("system.update_pending", "Windows 更新待處理", "系統", "偵測可用 Windows 更新以及 Windows Update / Component Based Servicing 的待重啟狀態。", "布林", use: "狀態 / 條件", formats: "無需格式化"),
        V("system.update_last_installed", "最近安裝更新日期", "系統", "Win32_QuickFixEngineering 中最近的 InstalledOn。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("system.defender_status", "Microsoft Defender 狀態", "系統", "Windows Defender WMI 報告的啟用/即時保護狀態。", "文字", use: "狀態", formats: "無需格式化"),
        V("system.firewall_status", "Windows 防火牆狀態", "系統", "Windows Firewall Policy 目前活動設定狀態。", "文字", use: "狀態", formats: "無需格式化"),
        V("system.bitlocker_status", "系統磁碟 BitLocker 狀態", "系統", "Windows Volume Encryption 介面報告的系統磁碟保護狀態。", "文字", use: "狀態", formats: "無需格式化"),
        V("system.hyper_v_status", "Hyper-V 狀態", "系統", "Windows OptionalFeature 中 Microsoft-Hyper-V-All 是否安裝啟用。", "布林", use: "狀態", formats: "無需格式化"),
        V("system.wsl_status", "WSL 狀態", "系統", "偵測 WSL 可執行檔案/已註冊發行版。", "布林", use: "狀態", formats: "無需格式化"),
        V("system.wsl_distro_count", "WSL 發行版數量", "系統", "目前使用者已註冊的 WSL 發行版數量。", "整數", "個", "左側資訊", "0"),

        // ===== 處理程序進階 =====
        V("process.background.count", "後臺處理程序數量", "處理程序", "目前沒有主視窗控制代碼的處理程序數量。", "整數", "個", "左側資訊", "0"),
        V("process.top_cpu.name", "CPU 佔用最高處理程序", "處理程序", "最近一次取樣中 CPU 使用率最高的處理程序名。", "文字", use: "左側主要數值", formats: "無需格式化"),
        V("process.top_cpu.pid", "CPU 最高處理程序 PID", "處理程序", "CPU 使用率最高處理程序的 PID。", "整數", use: "左側資訊", formats: "0"),
        V("process.top_cpu.usage", "最高處理程序 CPU 使用率", "處理程序", "按處理程序 TotalProcessorTime 差值計算的 CPU 使用率。", unit: "%", use: "右側狀態 / 圓環", formats: "0.0"),
        V("process.top_memory.name", "記憶體佔用最高處理程序", "處理程序", "工作集最大的處理程序名。", "文字", use: "左側主要數值", formats: "無需格式化"),
        V("process.top_memory.pid", "記憶體最高處理程序 PID", "處理程序", "工作集最大的處理程序 PID。", "整數", use: "左側資訊", formats: "0"),
        V("process.top_memory.usage", "最高處理程序記憶體佔用", "處理程序", "工作集最大的處理程序目前 Working Set。", unit: "Byte", use: "左側資訊", formats: "auto:1 / mb:1"),
        V("process.top_disk.name", "磁碟 I/O 最高處理程序", "處理程序", "Windows PerfProc IODataBytesPersec 最大的處理程序名。", "文字", use: "左側主要數值", formats: "無需格式化"),
        V("process.top_disk.pid", "磁碟 I/O 最高處理程序 PID", "處理程序", "磁碟 I/O 最高處理程序 PID。", "整數", use: "左側資訊", formats: "0"),
        V("process.top_disk.usage", "最高處理程序磁碟 I/O", "處理程序", "該處理程序目前總 I/O 位元組速率。", unit: "Byte/s", use: "左側資訊", formats: "auto:1 / speed"),
        V("process.gpu.top.name", "GPU 佔用最高處理程序", "處理程序", "GPU Engine 效能計數器中 GPU 使用率最高的處理程序名。", "文字", use: "左側主要數值", formats: "無需格式化"),
        V("process.gpu.top.usage", "最高處理程序 GPU 使用率", "處理程序", "GPU Engine 效能計數器彙總後的處理程序 GPU 使用率。", unit: "%", use: "右側狀態 / 圓環", formats: "0.0"),

        // ===== 軟體自身進階 =====
        V("app.theme", "應用主題", "ECP 應用", "目前 終末地 靈動島 使用的主題。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("app.preset_name", "目前方案名稱", "ECP 應用", "目前選中的 HUD 方案顯示名稱。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"),
        V("app.active_profile", "目前方案 ID", "ECP 應用", "目前 HUD 方案內部 ID。", "文字", use: "除錯 / 條件", formats: "無需格式化"),
        V("app.gpu_usage", "本程式 GPU 使用率", "ECP 應用", "GPU Engine 效能計數器中目前 終末地 靈動島 處理程序的利用率。", unit: "%", use: "右側狀態 / 圓環", formats: "0.0"),

        // ===== 螢幕進階 =====
        V("display.primary.name", "主顯示裝置名稱", "螢幕", "Windows 目前活動顯示裝置名稱。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("display.primary.color_depth", "主螢幕色深", "螢幕", "EnumDisplaySettings 回傳的 BitsPerPel。", "整數", "bit", "左側資訊", "0"),
        V("display.primary.refresh_rate", "主螢幕更新率", "螢幕", "目前顯示模式更新率。", "整數", "Hz", "左側資訊", "0"),
        V("display.secondary.name", "第二螢幕名稱", "螢幕", "偵測到第二個活動顯示裝置時提供。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("display.secondary.resolution", "第二螢幕解析度", "螢幕", "第二螢幕目前解析度。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("display.secondary.refresh_rate", "第二螢幕更新率", "螢幕", "第二螢幕目前更新率。", "整數", "Hz", "左側資訊", "0"),

        // ===== 世界時間 =====
        V("time.world.nyc", "紐約時間", "時間", "按 Windows Eastern Standard Time 即時換算。", "文字", use: "左側主要數值", formats: "無需格式化"),
        V("time.world.london", "倫敦時間", "時間", "按 Windows GMT Standard Time 即時換算。", "文字", use: "左側主要數值", formats: "無需格式化"),
        V("time.world.tokyo", "東京時間", "時間", "按 Windows Tokyo Standard Time 即時換算。", "文字", use: "左側主要數值", formats: "無需格式化"),
        V("time.world.beijing", "北京時間", "時間", "按 Windows China Standard Time 即時換算。", "文字", use: "左側主要數值", formats: "無需格式化"),

        // ===== 網路連線測試進階 =====
        V("probe.dns_resolve_time", "DNS 解析耗時", "網路連線測試", "當測試位址為網域名稱時，目前探測週期的 DNS 解析耗時；直接 IP 時為 0。", unit: "ms", use: "左側資訊", formats: "0.0"),
        V("probe.tcp_connect_time", "TCP 建連耗時", "網路連線測試", "TCP 模式下從發起連線到連線成功的耗時。", unit: "ms", use: "左側資訊", formats: "0.0"),

        // ===== 剪貼簿 =====
        V("clipboard.has_text", "剪貼簿含文字", "剪貼簿", "目前 Windows 剪貼簿是否包含 Unicode 文字。", "布林", use: "狀態 / 條件", formats: "無需格式化"),
        V("clipboard.text_length", "剪貼簿文字長度", "剪貼簿", "目前剪貼簿文字字元數。", "整數", "字元", "左側資訊", "0"),
        V("clipboard.preview", "剪貼簿文字預覽", "剪貼簿", "目前剪貼簿文字去除多餘空白後的前 80 個字元。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("clipboard.has_image", "剪貼簿含影像", "剪貼簿", "目前剪貼簿是否包含 Bitmap/DIB 影像格式。", "布林", use: "狀態 / 條件", formats: "無需格式化"),
        V("clipboard.image_size", "剪貼簿影像尺寸", "剪貼簿", "DIB/DIBV5 影像可解析時顯示寬×高。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("clipboard.file_count", "剪貼簿檔案數量", "剪貼簿", "CF_HDROP 檔案複製清單中的檔案數量。", "整數", "個", "左側資訊", "0"),
        V("clipboard.last_updated", "剪貼簿最後變化時間", "剪貼簿", "應用觀察到 Clipboard Sequence Number 變化的本地時間。", "文字", use: "左側資訊", formats: "time:HH:mm:ss"),

        // ===== USB / 周邊裝置 =====
        V("usb.device.count", "USB 裝置數量", "USB / 周邊裝置", "Win32_PnPEntity 中 USB PNP 裝置數量。", "整數", "個", "左側資訊", "0"),
        V("usb.device.list", "USB 裝置清單", "USB / 周邊裝置", "目前列舉到的 USB PNP 裝置名稱清單。", "文字", use: "左側資訊", formats: "sub:0:80"),
        V("usb.storage.count", "可移動儲存數量", "USB / 周邊裝置", "目前已就緒 DriveType.Removable 卷數量。", "整數", "個", "左側資訊", "0"),
        V("usb.storage.list", "可移動儲存清單", "USB / 周邊裝置", "目前可移動儲存磁碟機代號清單。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("peripheral.mouse.name", "滑鼠名稱", "USB / 周邊裝置", "Win32_PointingDevice 報告的首個指標裝置名稱。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("peripheral.keyboard.name", "鍵盤名稱", "USB / 周邊裝置", "Win32_Keyboard 報告的首個鍵盤裝置名稱。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("peripheral.gamepad.count", "XInput 手柄數量", "USB / 周邊裝置", "XInput 0-3 控制器槽位中已連線的手柄數量。", "整數", "個", "左側資訊", "0"),
        V("peripheral.gamepad.name", "手柄名稱", "USB / 周邊裝置", "目前偵測到的 XInput 控制器槽位名稱。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("peripheral.gamepad.battery", "手柄電量", "USB / 周邊裝置", "XInput 電池等級換算的近似百分比。", unit: "%", use: "右側狀態 / 圓環", formats: "0"),

        // ===== 開發者工具 =====
        V("dev.docker.running", "Docker 是否執行", "開發者工具", "偵測 Docker Desktop / dockerd 後臺處理程序。", "布林", use: "狀態", formats: "無需格式化"),
        V("dev.docker.containers", "Docker 執行容器數", "開發者工具", "docker ps -q 回傳的執行中容器數量；Docker CLI 可用時提供。", "整數", "個", "左側資訊", "0"),
        V("dev.docker.images", "Docker 映象數", "開發者工具", "docker images -q 的唯一映象 ID 數量。", "整數", "個", "左側資訊", "0"),
        V("dev.wsl.running", "WSL 是否執行", "開發者工具", "偵測 wsl/wslhost/vmmemWSL 相關處理程序。", "布林", use: "狀態", formats: "無需格式化"),
        V("dev.wsl.distro", "WSL 發行版清單", "開發者工具", "wsl -l -q 回傳的發行版清單。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("dev.wsl.memory_usage", "WSL 記憶體佔用", "開發者工具", "vmmemWSL/vmmem 工作集總量。", unit: "Byte", use: "左側資訊", formats: "auto:1 / gb:1"),
        V("dev.git.branch", "Git 目前分支", "開發者工具", "應用目前工作目錄為 Git 儲存庫時回傳分支。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("dev.git.status", "Git 工作區狀態", "開發者工具", "目前工作目錄 Git 狀態，clean 或變更數量。", "文字", use: "狀態", formats: "無需格式化"),
        V("dev.git.last_commit", "Git 最近提交", "開發者工具", "目前工作目錄最近一次提交的短雜湊和標題。", "文字", use: "左側資訊", formats: "sub:0:80"),
        V("dev.node.version", "Node.js 版本", "開發者工具", "PATH 中 node --version 的結果。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("dev.python.version", "Python 版本", "開發者工具", "PATH 中 python/py --version 的結果。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("dev.java.version", "Java 版本", "開發者工具", "PATH 中 java -version 的首行結果。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("dev.golang.version", "Go 版本", "開發者工具", "PATH 中 go version 的結果。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("dev.rust.version", "Rust 版本", "開發者工具", "PATH 中 rustc --version 的結果。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("dev.vscode.running", "VS Code 是否執行", "開發者工具", "偵測 Code / Code - Insiders 處理程序。", "布林", use: "狀態", formats: "無需格式化"),
        V("dev.terminal.running", "終端是否執行", "開發者工具", "偵測 Windows Terminal / PowerShell / cmd 處理程序。", "布林", use: "狀態", formats: "無需格式化"),
        V("dev.ide.running", "IDE 是否執行", "開發者工具", "偵測 VS Code、Visual Studio、Rider、IntelliJ/PyCharm 等常見 IDE 處理程序。", "布林", use: "狀態", formats: "無需格式化"),
        V("dev.llm.local_status", "本地 LLM 狀態", "開發者工具", "偵測 Ollama、LM Studio、llama-server 等常見本地模型處理程序。", "文字", use: "狀態", formats: "無需格式化"),

        // ===== 安全 =====
        V("security.defender.status", "Defender 狀態", "安全", "Windows Defender WMI 即時保護狀態。", "文字", use: "狀態", formats: "無需格式化"),
        V("security.defender.last_scan", "Defender 最近掃描", "安全", "最近一次 Quick/Full Scan 完成時間。", "文字", use: "左側資訊", formats: "time:relative"),
        V("security.defender.threats", "Defender 目前威脅數", "安全", "MSFT_MpThreat 目前列舉到的威脅數量。", "整數", "項", "右側狀態", "0"),
        V("security.firewall.status", "防火牆狀態", "安全", "Windows Firewall 目前活動設定是否啟用。", "文字", use: "狀態", formats: "無需格式化"),
        V("security.firewall.profile", "防火牆設定檔案", "安全", "目前活動防火牆設定檔案：域/專用/公用。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("security.bitlocker.status", "BitLocker 狀態", "安全", "系統卷 BitLocker ProtectionStatus。", "文字", use: "狀態", formats: "無需格式化"),
        V("security.bitlocker.encryption_percent", "BitLocker 加密進度", "安全", "系統卷 EncryptionPercentage。", unit: "%", use: "右側狀態 / 圓環", formats: "0"),
        V("security.secure_boot", "Secure Boot", "安全", "UEFISecureBootEnabled 登錄檔狀態。", "布林", use: "狀態 / 條件", formats: "無需格式化"),
        V("security.tpm.present", "TPM 存在", "安全", "Win32_Tpm 是否可列舉。", "布林", use: "狀態 / 條件", formats: "無需格式化"),
        V("security.tpm.version", "TPM 版本", "安全", "Win32_Tpm SpecVersion。", "文字", use: "左側資訊", formats: "無需格式化"),
        V("security.uac_status", "UAC 狀態", "安全", "EnableLUA 登錄檔狀態。", "布林", use: "狀態 / 條件", formats: "無需格式化"),
        V("security.smartscreen_status", "SmartScreen 狀態", "安全", "Windows Explorer SmartScreenEnabled 設定。", "文字", use: "狀態", formats: "無需格式化"),
        V("security.windows_update.status", "Windows Update 狀態", "安全", "綜合待重啟標誌和可用更新搜尋生成的狀態。", "文字", use: "狀態", formats: "無需格式化"),
        V("security.windows_update.pending_count", "待安裝更新數量", "安全", "Microsoft.Update.Session 搜尋到的未安裝且未隱藏更新數量。", "整數", "項", "右側狀態", "0"),
        V("security.vpn.active", "VPN 活動狀態", "安全", "與 network.vpn_status 同源的活動 VPN 偵測。", "布林", use: "狀態 / 條件", formats: "無需格式化"),
        V("security.proxy.enabled", "代理啟用狀態", "安全", "與 network.proxy_status 同源的系統代理狀態。", "布林", use: "狀態 / 條件", formats: "無需格式化"),
    };

    private static readonly HashSet<string> AdvancedKeySet = new(AdvancedBuiltIns.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);

    internal static bool IsAdvancedKey(string key) => AdvancedKeySet.Contains(key);

    private static readonly Lazy<IReadOnlyList<VariableDefinition>> DynamicDriveBuiltIns = new(() =>
    {
        var list = new List<VariableDefinition>();
        AddDynamicDriveDefinitions(list);
        return list;
    });

    // Variable Library display order is intentionally semantic rather than alphabetical.
    // This keeps Chinese and English UI ordering identical and stable across localization.
    private static readonly IReadOnlyDictionary<string, int> CategoryDisplayRank =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["CPU"] = 0,
            ["GPU"] = 1,
            ["記憶體"] = 2,
            ["磁碟"] = 3,
            ["電池"] = 4,
            ["網路"] = 5,
            ["網路連線測試"] = 6,
            ["Ping（相容）"] = 7,
            ["系統"] = 8,
            ["螢幕"] = 9,
            ["時間"] = 10,
            ["處理程序"] = 11,
            ["ECP 應用"] = 12,
            ["安全"] = 14,
            ["USB / 周邊裝置"] = 15,
            ["剪貼簿"] = 16,
            ["開發者工具"] = 17,
            ["自訂資料"] = 18,
        };

    // Within a category, common live/status values are shown before detailed/static values.
    // Prefixes are only display hints; variable keys and runtime behavior are not changed.
    private static readonly IReadOnlyDictionary<string, string[]> VariableDisplayPrefixes =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["CPU"] =
            [
                "cpu.usage", "cpu.idle", "cpu.frequency",
                "cpu.temperature", "cpu.power", "cpu.core", "cpu.bus",
                "cpu.name", "cpu.manufacturer", "cpu.architecture",
                "cpu.physical", "cpu.logical", "cpu.socket", "cpu.virtualization",
                "cpu.l2", "cpu.l3",
                "cpu.instructions", "cpu.context", "cpu.interrupts", "cpu.dpc", "cpu.system_calls"
            ],
            ["GPU"] =
            [
                "gpu.usage",
                "gpu.temperature", "gpu.hotspot", "gpu.memory_junction",
                "gpu.power", "gpu.voltage", "gpu.core_clock", "gpu.memory_clock",
                "gpu.dedicated_used", "gpu.dedicated_total", "gpu.dedicated_usage",
                "gpu.shared_used", "gpu.shared_limit", "gpu.shared_usage",
                "gpu.memory_used", "gpu.memory_total",
                "gpu.total_memory_used", "gpu.total_memory_limit", "gpu.total_memory_usage",
                "gpu.vram", "gpu.uses_unified_memory",
                "gpu.name", "gpu.count", "gpu.physical_index", "gpu.adapter_id",
                "gpu.pcie", "gpu.driver", "gpu.bios",
                "gpu.nvidia_smi", "gpu.amd_adrenalin"
            ],
            ["記憶體"] =
            [
                "memory.usage", "memory.used", "memory.available", "memory.total", "memory.free_percent",
                "memory.commit",
                "memory.virtual",
                "memory.cache", "memory.standby", "memory.modified",
                "memory.paged_pool", "memory.nonpaged_pool",
                "memory.hardware_reserved",
                "memory.speed", "memory.slot", "memory.form_factor", "memory.type"
            ],
            ["磁碟"] =
            [
                "disk.system.usage", "disk.system.used", "disk.system.free", "disk.system.total",
                "disk.system.read", "disk.system.write", "disk.system.io",
                "disk.system.active", "disk.system.queue",
                "disk.system.root", "disk.system.label", "disk.system.filesystem",
                "disk.fixed.usage", "disk.fixed.used", "disk.fixed.free", "disk.fixed.total",
                "disk.fixed.count", "disk.fixed.list"
            ],
            ["電池"] =
            [
                "battery.status_text", "battery.percent", "battery.power_source",
                "battery.ac_online", "battery.charging", "battery.discharging", "battery.saver_on",
                "battery.rate_watts", "battery.charge_rate_watts", "battery.discharge_rate_watts",
                "battery.remaining_wh", "battery.full_wh", "battery.design_wh",
                "battery.remaining_mwh", "battery.full_mwh", "battery.design_mwh",
                "battery.health_percent", "battery.design_vs_current_health",
                "battery.time_remaining", "battery.estimated_time_to_empty", "battery.estimated_time_to_full",
                "battery.full_life",
                "battery.voltage", "battery.temperature", "battery.cycle_count", "battery.chemistry"
            ],
            ["網路"] =
            [
                "network.available",
                "network.download", "network.upload", "network.total_bps",
                "network.download_mbps", "network.upload_mbps", "network.total_mbps",
                "network.display", "network.profile",
                "network.link_speed", "network.max_link_speed", "network.utilization",
                "network.active_interface", "network.interface",
                "network.ipv4", "network.ipv6", "network.default_gateways", "network.dns_servers",
                "network.public",
                "network.signal_dbm", "network.wifi",
                "network.vpn", "network.proxy",
                "network.dns_latency", "network.tcp_connections", "network.udp_connections",
                "network.total_received", "network.total_sent", "network.total_transferred",
                "network.packets", "network.receive_errors", "network.send_errors"
            ],
            ["網路連線測試"] =
            [
                "probe.target", "probe.endpoint", "probe.protocol", "probe.port", "probe.address",
                "probe.online", "probe.status_text", "probe.reply_status",
                "probe.latency_ms", "probe.latency_text", "probe.latency_progress",
                "probe.avg_latency", "probe.min_latency", "probe.max_latency", "probe.jitter",
                "probe.loss_percent", "probe.sent", "probe.received", "probe.lost",
                "probe.full_scale", "probe.timeout", "probe.ttl",
                "probe.dns_resolve_time", "probe.tcp_connect_time",
                "probe.last_success", "probe.error"
            ],
            ["Ping（相容）"] =
            [
                "ping.target", "ping.endpoint", "ping.protocol", "ping.port", "ping.address",
                "ping.online", "ping.status_text", "ping.reply_status",
                "ping.latency_ms", "ping.latency_text", "ping.progress",
                "ping.avg_latency", "ping.min_latency", "ping.max_latency", "ping.jitter",
                "ping.loss_percent", "ping.sent", "ping.received", "ping.lost",
                "ping.full_scale", "ping.timeout", "ping.ttl",
                "ping.last_success", "ping.error"
            ],
            ["系統"] =
            [
                "system.os_description", "system.os_version", "system.os_build",
                "system.os_architecture", "system.process_architecture", "system.framework_version",
                "system.machine_name", "system.host_name", "system.user_name", "system.user_domain",
                "system.uptime", "system.boot_time",
                "system.timezone", "system.utc_offset", "system.culture",
                "system.processor_count",
                "system.power_plan_name", "system.power_plan",
                "system.bios", "system.motherboard", "system.fan",
                "system.update", "system.defender", "system.firewall", "system.bitlocker",
                "system.hyper_v", "system.wsl",
                "system.time", "system.date", "system.datetime"
            ],
            ["螢幕"] =
            [
                "display.monitor_count",
                "display.primary.name", "display.primary_width", "display.primary_height",
                "display.primary.refresh_rate", "display.primary.color_depth",
                "display.system_dpi", "display.scale_percent",
                "display.virtual",
                "display.secondary"
            ],
            ["時間"] =
            [
                "time.current", "time.current_12h", "time.date", "time.datetime", "time.iso",
                "time.world",
                "time.year", "time.month", "time.month_name", "time.day",
                "time.day_of_week", "time.day_of_year", "time.week_of_year",
                "time.hour", "time.minute", "time.second", "time.millisecond",
                "time.is_weekend", "time.unix",
                "time.day.progress", "time.day.elapsed", "time.day.remaining",
                "time.week.progress", "time.week.elapsed", "time.week.remaining",
                "time.month.progress", "time.month.elapsed", "time.month.remaining",
                "time.year.progress", "time.year.elapsed", "time.year.remaining",
                "time.display",
                "time.target"
            ],
            ["處理程序"] =
            [
                "process.top_cpu", "process.top_memory", "process.top_disk", "process.gpu.top",
                "process.background"
            ],
            ["ECP 應用"] =
            [
                "app.name", "app.version", "app.theme", "app.preset_name", "app.active_profile",
                "app.pid", "app.start_time", "app.uptime",
                "app.cpu_time", "app.gpu_usage",
                "app.working_set", "app.private_memory", "app.virtual_memory",
                "app.thread_count", "app.handle_count"
            ],
            ["安全"] =
            [
                "security.secure_boot", "security.tpm", "security.uac", "security.smartscreen",
                "security.defender", "security.firewall", "security.bitlocker",
                "security.windows_update", "security.vpn", "security.proxy"
            ],
            ["USB / 周邊裝置"] =
            [
                "usb.device", "usb.storage",
                "peripheral.mouse", "peripheral.keyboard", "peripheral.gamepad"
            ],
            ["剪貼簿"] =
            [
                "clipboard.has_text", "clipboard.preview", "clipboard.text_length",
                "clipboard.has_image", "clipboard.image_size",
                "clipboard.file_count", "clipboard.last_updated"
            ],
            ["開發者工具"] =
            [
                "dev.ide", "dev.vscode", "dev.terminal",
                "dev.git",
                "dev.node", "dev.python", "dev.java", "dev.golang", "dev.rust",
                "dev.docker", "dev.wsl", "dev.llm"
            ],
        };

    private static int GetCategoryDisplayRank(string category) =>
        CategoryDisplayRank.TryGetValue(category, out int rank) ? rank : int.MaxValue;

    private static int GetVariablePrefixRank(VariableDefinition item)
    {
        if (!VariableDisplayPrefixes.TryGetValue(item.Category, out var prefixes))
            return int.MaxValue;

        for (int i = 0; i < prefixes.Length; i++)
        {
            if (item.Key.StartsWith(prefixes[i], StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return int.MaxValue;
    }

    public static IReadOnlyList<VariableDefinition> AllBuiltIns { get; } =
        StaticBuiltIns.Concat(AdvancedBuiltIns).Concat(DynamicDriveBuiltIns.Value)
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select((item, sourceIndex) => new { Item = item, SourceIndex = sourceIndex })
            .OrderBy(x => GetCategoryDisplayRank(x.Item.Category))
            .ThenBy(x => GetVariablePrefixRank(x.Item))
            .ThenBy(x => x.SourceIndex)
            .Select(x => x.Item)
            .ToList();

    public static VariableDefinition? Find(string key) =>
        AllBuiltIns.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));

    public static VariableDefinition CreateCustom(string key) => new(
        key,
        key.Split('.').LastOrDefault() ?? key,
        "自訂資料",
        "來自 HTTP / JSON 資料來源的自訂變數。實際意義由資料來源對應決定。",
        "動態",
        "",
        "依資料內容決定",
        "0 / 0.0 / gb:1 / speed / duration 等");

    private static void AddDynamicDriveDefinitions(List<VariableDefinition> list)
    {
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                string letter = drive.Name.TrimEnd('\\', '/').TrimEnd(':').ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(letter)) continue;
                string title = drive.Name.TrimEnd('\\', '/');
                list.Add(V($"disk.{letter}.used_bytes", $"{title} 已用空間", "磁碟", $"{title} 驅動器已使用空間。", unit: "Byte", use: "左側主要數值", formats: "gb:1 / bytes"));
                list.Add(V($"disk.{letter}.free_bytes", $"{title} 可用空間", "磁碟", $"{title} 驅動器可用空間。", unit: "Byte", use: "左側資訊", formats: "gb:1 / bytes"));
                list.Add(V($"disk.{letter}.total_bytes", $"{title} 總容量", "磁碟", $"{title} 驅動器總容量。", unit: "Byte", use: "左側次要數值", formats: "gb:1 / bytes"));
                list.Add(V($"disk.{letter}.usage", $"{title} 使用率", "磁碟", $"{title} 驅動器空間使用率。", unit: "%", use: "右側狀態 / 圓環"));
                list.Add(V($"disk.{letter}.label", $"{title} 卷標", "磁碟", $"{title} 驅動器卷標。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"));
                list.Add(V($"disk.{letter}.filesystem", $"{title} 檔案系統", "磁碟", $"{title} 驅動器檔案系統。", "文字", use: "標題 / 左側資訊", formats: "無需格式化"));
                list.Add(V($"disk.{letter}.read_bps", $"{title} 即時讀取速度", "磁碟", $"{title} 邏輯卷 Windows 效能計數器即時讀取速度。", unit: "Byte/s", use: "左側資訊", formats: "auto:1 / speed"));
                list.Add(V($"disk.{letter}.write_bps", $"{title} 即時寫入速度", "磁碟", $"{title} 邏輯卷 Windows 效能計數器即時寫入速度。", unit: "Byte/s", use: "左側資訊", formats: "auto:1 / speed"));
                list.Add(V($"disk.{letter}.io_bps", $"{title} 即時總 I/O", "磁碟", $"{title} 目前讀取+寫入總速率。", unit: "Byte/s", use: "左側資訊", formats: "auto:1 / speed"));
                list.Add(V($"disk.{letter}.active_percent", $"{title} 活動時間", "磁碟", $"{title} PercentDiskTime。", unit: "%", use: "右側狀態 / 圓環", formats: "0.0"));
                list.Add(V($"disk.{letter}.queue_length", $"{title} 佇列長度", "磁碟", $"{title} CurrentDiskQueueLength。", unit: "項", use: "左側資訊", formats: "0.0"));
                list.Add(V($"disk.{letter}.health", $"{title} 磁碟健康狀態", "磁碟", $"{title} 所在物理磁碟的 Windows Storage HealthStatus；Storage 介面支援時可用。", "文字", use: "狀態", formats: "無需格式化"));
                list.Add(V($"disk.{letter}.temperature", $"{title} 磁碟溫度", "磁碟", $"{title} 所在磁碟的 Storage Reliability Counter 溫度；裝置支援時可用。", unit: "°C", use: "左側資訊", formats: "0"));
                list.Add(V($"disk.{letter}.power_on_hours", $"{title} 通電時間", "磁碟", $"{title} 所在磁碟的 PowerOnHours；裝置支援時可用。", unit: "小時", use: "左側資訊", formats: "0"));
                list.Add(V($"disk.{letter}.trim_status", $"{title} TRIM 狀態", "磁碟", $"{title} 檔案系統對應的 Windows DisableDeleteNotify 狀態。", "文字", use: "狀態", formats: "無需格式化"));
                list.Add(V($"disk.{letter}.smart_status", $"{title} 儲存執行狀態", "磁碟", $"{title} 所在物理磁碟的 Windows Storage OperationalStatus。", "文字", use: "狀態", formats: "無需格式化"));
                list.Add(V($"disk.{letter}.partition_count", $"{title} 關聯分割槽數量", "磁碟", $"{title} 邏輯卷透過 Win32_LogicalDiskToPartition 關聯的分割槽數量。", "整數", "個", "左側資訊", "0"));
            }
        }
        catch { }
    }
}

<p align="center"><img src="docs/assets/hero.png" alt="終末地 靈動島 · 石墨灰、青藍與黃色的 Windows 桌面工具" width="100%"></p>

<h1 align="center">終末地 靈動島</h1>
<p align="center">ENDFIELD DYNAMIC ISLAND<br>把對話、音樂、通知與效能資訊，收進桌面上的一座島。</p>
<p align="center">
  <a href="https://github.com/OverGreen996/Endfield-Dynamic-Island/releases/latest">下載 Windows 安裝版</a> ·
  <a href="docs/INSTALL.zh-TW.md">安裝教學</a> ·
  <a href="README.en.md">English</a>
</p>
<p align="center">
  <img alt="Windows 10 / 11" src="https://img.shields.io/badge/Windows-10%20%2F%2011-13C8EB?style=flat-square">
  <img alt=".NET 8" src="https://img.shields.io/badge/.NET-8-323736?style=flat-square">
  <img alt="source v0.28.14" src="https://img.shields.io/badge/source-v0.28.14-E6E744?style=flat-square">
  <img alt="MIT" src="https://img.shields.io/badge/license-MIT-323736?style=flat-square">
</p>

以終末地的工業介面為靈感，搭配石墨灰面板、青藍資訊與黃色操作重點。視窗切換使用銜接動畫；一次呈現一種內容，通知結束後回到原來的頁面。

設定視窗採用：深色索引導覽、銀灰面板、章節字階，以及融入程式的標題列。中英介面與視窗操作都保留。[設計與驗證](docs/SETTINGS-DESIGN.md)。

![設定視窗新版介面](docs/assets/settings-v026.png)

v0.25.0 將 HUD 顯示與採樣分離。硬體方案收起時約每 5 秒背景預採樣，顯示時約每秒更新；喚出直接讀快取，AI／音樂／通知不等待硬體暖機。採樣與設定預覽共用同一份服務，完整電源演出保留。[採樣架構與限制](docs/ARCHITECTURE.md#v0250-顯示與採樣分離)。

## 一眼看懂，一鍵喚出

| 模組 | 使用方式 |
| --- | --- |
| **AI 助理** | `Alt+A` 直接開始輸入。你的訊息在右側，AI 在左側；自然問答、快速逐字回覆、上下文、右鍵新對話與頂部收合動畫。 |
| **音樂島** | `Alt+M` 喚出。封面、時間、進度、上一首／下一首、隨機與重播整合在橫向膠囊中。 |
| **Windows 通知** | 新通知優先顯示，結束後返回原頁。支援滑鼠停留、點擊固定、隱藏內容與逾時收起。 |
| **效能 HUD** | 同一列看 CPU、GPU、RAM、VRAM，包含百分比與容量。沒有讀到的數據顯示 `—`，不產生假讀值。 |
| **提醒與記憶** | 「明天九點提醒我開會」建立本機提醒。記憶依身分、偏好、個人事項與不希望 AI 做的事情分類，可逐筆刪除。 |
| **桌面控制中心** | 繁體中文／English、顯示器與位置、縮放、動畫、頭像、通知權限與設定管理。 |

## 先下載，後設定

1. 到 [Releases](https://github.com/OverGreen996/Endfield-Dynamic-Island/releases/latest) 下載 `Endfield-Dynamic-Island-Setup-v<版本>-win-x64.exe`。
2. 從系統匣退出舊版，執行安裝程式。預設安裝到目前使用者，不需要管理員權限。
3. 從桌面或開始功能表開啟「終末地 靈動島」。可在 Windows「已安裝的應用程式」解除安裝。
4. 在設定選擇顯示位置；音樂貼上自己的 YouTube 清單，通知按「要求通知讀取權限」。

安裝版附帶 .NET 執行環境與播放器；升級及解除安裝保留使用者資料，不再配送便攜版。HUD、提醒、記憶管理與通知介面可獨立使用。YouTube 清單播放需要 Microsoft Edge WebView2 Runtime；跟隨瀏覽器模式使用 Windows 媒體介面，隨機／重播是否可用取決於瀏覽器的能力。

發佈頁提供的版本以該頁實際附件為準；原始碼版本不代表已發佈到 GitHub。

目前原始碼版本 v0.28.14，AI 與搜尋直接收進主程式：不需 Node、本機 HTTP 服務或 PowerShell 設定工具。安裝後直接設定金鑰即可使用；Daily 的金鑰、資料與額度完全獨立。既有對話、記憶、提醒與用量紀錄保留。

對話區塊重寫為自然問答，快速逐字顯示，不展示來源或查證報告。一般文字助理採 Gemini → Groq GPT-OSS 120B → Cloudflare Qwen3.8-27B，移除本機次數與累計 token 限額，依官方回應與恢復時間輪替。設定 → AI 助理填入備援憑證；圖片文字改用 Windows 本機 OCR；單純擷取文字不需模型金鑰。[備援設定教學](docs/AI-FALLBACK.md)。

搜尋設定新增可選「每輪搜尋換一家」，成功後依自訂順序循環分散請求；快取、取消與純聊天不推進，故障及封鎖自動跳過。預設保留優先順序，詳見 [操作說明](docs/USAGE.zh-TW.md)。

## 本機優先

- 搜尋使用 Exa Auto → Tavily Basic → Firecrawl Search。右鍵 → 搜尋 API 與輪替，在程式內設定金鑰及順序。Exa、Tavily 無本機額度限制，API 搜尋失敗後停用至下個月 2 號台灣時間 00:05，再於下一次搜尋重試；期間自動切換備援。Firecrawl 無本機點數上限，以官方 API 核對餘額，帳單切換日與時間可手動設定。
- 自動模式先由 Gemini 理解一次問題；一般聊天以專門答覆步驟回答，需要查證才搜尋，再依資料整理。只搜尋模式不呼叫 Gemini。
- 同一輪模型回應判斷個人記憶，不另扣分類次數；玩笑、第三人資料、敏感資訊與推測不自動記憶。
- 金鑰、對話、提醒與記憶使用 Windows DPAPI；通知不會送給模型。模型用量採原有本機帳本，升級不重設額度。
- 提醒最多 300 筆，滿額移除最舊項目；程式必須運行才能提醒，不會喚醒睡眠或關機的電腦。
[隱私與資料位置](docs/PRIVACY.md) · [操作教學](docs/USAGE.zh-TW.md) · [設計規範](docs/DESIGN.md) · [變更紀錄](CHANGELOG.md)

## 從原始碼建置

需要 Windows 10 / 11、.NET 8 SDK 與 Inno Setup 7.1 或更新版本。公開原始碼以相對路徑建置，無須原開發電腦的目錄。

```powershell
git clone https://github.com/OverGreen996/Endfield-Dynamic-Island.git
cd Endfield-Dynamic-Island
dotnet build src/EndfieldIsland/EndfieldChargePlus.csproj -c Release
./scripts/Test.ps1
./scripts/Publish.ps1
```

安裝程式與 SHA256 輸出到 `artifacts/installer`；`artifacts/staging` 僅為建置暫存。`scripts/Start.ps1` 啟動預設安裝位置的程式。測試使用合成資料，不呼叫 Gemini。實體多螢幕、不同 Windows DPI、不同 YouTube 清單與通知來源仍需在各自環境驗證。

## 來源與授權

本專案延續 [GlacierGlimmer/zmd-charge-plus](https://github.com/GlacierGlimmer/zmd-charge-plus) 與 [QinAnze/zmd-charge](https://github.com/QinAnze/zmd-charge)，保留原作者 MIT 授權與署名。

介面向量圖標來自 [Yue-plus/endfield_icons](https://github.com/Yue-plus/endfield_icons)，播放器附帶 YouTube NonStop 的 MIT 授權元件。第三方元件詳見 [NOTICE.md](NOTICE.md)；相關原始碼、授權與來源隨包保留。

這是非官方社群衍生工具，與《明日方舟：終末地》開發方或發行方無隸屬或背書關係。頁首為品牌示意圖，數字用於版面示範。

### 人格與五組 Gemini 主力

設定 → AI 助理 → **管理 AI 人格**：分開填寫角色人格與互動規則，儲存後按「使用此人格」。可新增、複製或刪除，內附可編輯的莊方宜助理範例。切換從下一則訊息生效，原本聊天與記憶保留。

**設定 Gemini 五組主力輪換**：第 1 組沿用上方主金鑰，另填 2–5 組。程式持續使用第 1 組，受限才換 2、3、4、5，全部不可用才接 Groq → Cloudflare；恢復後回到較優先組。對話上下文與人格不因帳號接手而中斷。Google 配額按專案計算，多帳號使用仍須符合 [API 條款](https://developers.google.com/terms/)，不是無限免費額度。

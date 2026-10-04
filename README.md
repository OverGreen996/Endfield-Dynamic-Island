<p align="center"><img src="docs/assets/hero.png" alt="終末地 靈動島 · 石墨灰、青藍與黃色的 Windows 桌面工具" width="100%"></p>

<h1 align="center">終末地 靈動島</h1>
<p align="center">ENDFIELD DYNAMIC ISLAND<br>把對話、音樂、通知與效能資訊，收進桌面上的一座島。</p>
<p align="center">
  <a href="https://github.com/OverGreen996/Endfield-Dynamic-Island/releases/latest">下載 Windows 便攜版</a> ·
  <a href="docs/INSTALL.zh-TW.md">安裝教學</a> ·
  <a href="README.en.md">English</a>
</p>
<p align="center">
  <img alt="Windows 10 / 11" src="https://img.shields.io/badge/Windows-10%20%2F%2011-13C8EB?style=flat-square">
  <img alt=".NET 8" src="https://img.shields.io/badge/.NET-8-323736?style=flat-square">
  <img alt="v0.22.0" src="https://img.shields.io/badge/release-v0.22.0-E6E744?style=flat-square">
  <img alt="MIT" src="https://img.shields.io/badge/license-MIT-323736?style=flat-square">
</p>

以終末地的工業介面為靈感，搭配石墨灰面板、青藍資訊與黃色操作重點。視窗切換使用銜接動畫；一次呈現一種內容，通知結束後回到原來的頁面。

## 一眼看懂，一鍵喚出

| 模組 | 使用方式 |
| --- | --- |
| **AI 助理** | `Alt+A` 直接開始輸入。你的訊息在右側，AI 在左側；支援上下文、來源連結、右鍵新對話與頂部收合動畫。 |
| **音樂島** | `Alt+M` 喚出。封面、時間、進度、上一首／下一首、隨機與重播整合在橫向膠囊中。 |
| **Windows 通知** | 新通知優先顯示，結束後返回原頁。支援滑鼠停留、點擊固定、隱藏內容與逾時收起。 |
| **效能 HUD** | 同一列看 CPU、GPU、RAM、VRAM，包含百分比與容量。沒有讀到的數據顯示 `—`，不產生假讀值。 |
| **提醒與記憶** | 「明天九點提醒我開會」建立本機提醒。記憶依身分、偏好、個人事項與不希望 AI 做的事情分類，可逐筆刪除。 |
| **桌面控制中心** | 繁體中文／English、顯示器與位置、縮放、動畫、頭像、通知權限與設定管理。 |

## 先下載，後設定

1. 到 [Releases](https://github.com/OverGreen996/Endfield-Dynamic-Island/releases/latest) 下載 `Endfield-Dynamic-Island-v0.22.0-win-x64.zip`。
2. 解壓到固定資料夾，**保留所有檔案與 `MusicPlayerHost` 子資料夾**。
3. 執行 `EndfieldChargePlus.exe`。顯示名稱是「終末地 靈動島」；內部檔名沿用舊版以維持相容。
4. 在設定選擇顯示位置；音樂貼上自己的 YouTube 清單，通知按「要求通知讀取權限」。

便攜版附帶 .NET 執行環境。HUD、提醒、記憶管理與通知介面可獨立使用。YouTube 清單播放需要 Microsoft Edge WebView2 Runtime；跟隨瀏覽器模式使用 Windows 媒體介面，隨機／重播是否可用取決於瀏覽器的能力。

**AI 對話與搜尋還需要獨立的本機 Gemini Hub 服務。** 此儲存庫不配送 Gemini Hub、API Key 或私人設定。現有使用者可沿用原本服務；新電腦需先完成服務部署。XNG 是可獨立更新的共用搜尋核心，參閱 [XNG-Plugin](https://github.com/OverGreen996/XNG-Plugin)。只安裝 XNG 不會自動補齊 Gemini Hub。詳見 [服務與相容性](docs/ARCHITECTURE.md)。

## 本機優先

- XNG 提供可追溯的搜尋證據；AI 使用端整理回答。搜尋邏輯集中維護。
- 正常搜尋不自動呼叫 Gemini 或付費搜尋服務。Gemini 依共用 Hub 的用量限制與免費核對規則執行。
- 對話、提醒與記憶採 Windows 使用者加密；頭像只保存在本機。通知不會送給模型。
- 通知與提醒不同：通知短暫顯示；提醒最多 300 筆，滿額依建立順序移除最舊項目。
- 軟體必須運行才能提醒；睡眠或關機時不會喚醒電腦，恢復後處理逾期事項。

[隱私與資料位置](docs/PRIVACY.md) · [操作教學](docs/USAGE.zh-TW.md) · [設計規範](docs/DESIGN.md) · [變更紀錄](CHANGELOG.md)

## 從原始碼建置

需要 Windows 10 / 11 與 .NET 8 SDK。公開原始碼以相對路徑建置，無須原開發電腦的目錄。

```powershell
git clone https://github.com/OverGreen996/Endfield-Dynamic-Island.git
cd Endfield-Dynamic-Island
dotnet build src/EndfieldIsland/EndfieldChargePlus.csproj -c Release
./scripts/Test.ps1
./scripts/Publish.ps1
```

發佈到 `artifacts/Endfield-Dynamic-Island-v0.22.0-win-x64`；`Start.ps1` 可從便攜包啟動。測試使用合成資料，不呼叫 Gemini。實體多螢幕、不同 Windows DPI、不同 YouTube 清單與通知來源仍需在各自環境驗證。

## 來源與授權

本專案延續 [GlacierGlimmer/zmd-charge-plus](https://github.com/GlacierGlimmer/zmd-charge-plus) 與 [QinAnze/zmd-charge](https://github.com/QinAnze/zmd-charge)，保留原作者 MIT 授權與署名。

介面向量圖標來自 [Yue-plus/endfield_icons](https://github.com/Yue-plus/endfield_icons)，播放器附帶 YouTube NonStop 的 MIT 授權元件。第三方元件詳見 [NOTICE.md](NOTICE.md)；相關原始碼、授權與來源隨包保留。

這是非官方社群衍生工具，與《明日方舟：終末地》開發方或發行方無隸屬或背書關係。頁首為品牌示意圖，數字用於版面示範。

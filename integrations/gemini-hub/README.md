# Gemini Hub 0.4.0 · 共用 AI 理解層

獨立部署的本機服務，讓靈動島及其他使用端共用 Gemini 理解、XNG 搜尋證據與用量帳本。此目錄配送原始碼與管理工具；靈動島 v0.24.0 Setup 會安裝共用 Hub 與 Node 執行環境，但不配送政策、金鑰或私人資料。

## 使用安裝檔（建議）

安裝終末地靈動島 v0.24.0 後，到設定 → AI 助理貼上自己的 API Key，確認專案使用免費方案且未啟用付費，再按套用。初次安裝會自動建立本機認證與 SQLite 帳本，未完成設定前禁止模型呼叫；啟動與設定本身零模型請求。

同一已核對金鑰保留原限制與當日用量；首次設定或不同金鑰預設採本機 20 次／日、3 次／分鐘，`official_snapshot=null`、`provider_limits_verified=false`，不假裝知道 Google 的實際額度。變更金鑰沿用帳本的 project 身分，不能靠換 Key 重置用量。明確確認並套用後會重啟 Hub。

核心在文件 `ChatGPT/GeminiHub/core`，資料在 `data`，政策在 `policy.json`。移除靈動島會保留共用 Hub、Node 與資料；獨立 XNG 不受影響。已運行的共用 Hub 不會由安裝程式強制中斷，請明確重啟後使用新版。

## 對話流程

- `auto`：先 Gemini 理解問題與上下文。普通聊天同一輪回答及判斷記憶，通常一次生成；需要查證則規劃查詢 → XNG → Gemini 整理，通常兩次。
- `search`：先理解及規劃，再搜尋、整理；必要遊戲名稱不明時先釐清。
- `chat`：只聊天，通常一次生成。
- `web`：只取得 XNG 證據，零 Gemini 呼叫。
- 圖片沿用辨識 → XNG → 整理，通常兩次；不額外增加文字規劃轮，不以圖片建立長期記憶。

相對日期使用 Asia/Taipei 本機時鐘。遊戲名與查詢目標分開，避免「近期活動」被誤認為遊戲名。節日搜尋包含國際紀念日，與當地放假分開。未核實的活动起訖及版本不宣稱已確認。

Hub 不產生自己的搜尋算法、去重或來源排序；仍由獨立 XNG 共用核心負責。XNG 核心、8888／8889及 schema_version=1 未修改。

## 既有 Hub 更新

先退出正在送出中的對話，備份舊程式。把本目錄的 `core` 與具名管理腳本更新到自己的 `Documents/ChatGPT/GeminiHub`，執行 `Restart-GeminiHub.ps1`。

**不要覆蓋 `policy.json`、`data`、`runtime`、金鑰、token、帳本或使用者資料。** `policy.example.json` 只給新部署用；不要拿來取代既有政策。重啟腳本核對 PID 歸屬後只重啟 Hub。既有前端仍相容；更新後明確重啟 Hub 生效。

## 新電腦手動部署（Windows）

1. 準備 Node.js 24（含 SQLite）及自己的 [XNG-Plugin](https://github.com/OverGreen996/XNG-Plugin)。把本目錄複製到 Windows「文件」的 `ChatGPT/GeminiHub`，以便靈動島找到金鑰工具與認證檔。
2. 將官方 Windows Node 24 發行包解壓到 `runtime`，確認 `runtime/node.exe` 存在；不需要 npm install，核心沒有外部 npm 依賴。
3. 在 Hub 目錄執行以下初始化。只適用全新資料目錄；已有設定時不要重新產生認證或覆蓋政策。

```powershell
Copy-Item policy.example.json policy.json
New-Item -ItemType Directory data
& ./runtime/node.exe ./core/cli.js init
& ./runtime/node.exe -e "require('node:fs').writeFileSync('data/hub.token',require('node:crypto').randomBytes(32).toString('hex'),{flag:'wx'})"
```

4. 在 AI Studio 核對**自己的專案**仍是 Free、所需模型可用及目前限制。編輯 `policy.json`：`project` 填自己的專案 ID；`verification.keySuffix` 填自己金鑰最後4碼。將 `models.*.official` 改成自己確認的額度，`local` 與 `dailyRequests` 不得高於它；不要增加模型或開啟 `paidAllowed`／`toolsAllowed`。範例的 500 RPD 是開發環境快照，**不代表其他人的免費額度**。範例確認時間刻意過期，未完成自己的確認不能呼叫模型。
5. 執行 `Set-GeminiKey.ps1`，在本機隱藏欄位輸入自己的 API Key。確認自己專案是 Free 後，執行 `Confirm-GeminiFreeMode.ps1 -Confirmation Free`，再執行 `Start-GeminiHub.ps1`。金鑰以 Windows DPAPI 保存；不要貼到聊天、Git、網址或命令列參數。
6. 執行 `Query-GeminiUsage.ps1` 核對狀態，再在靈動島使用 `Alt+A`。XNG 可放在旁邊的 `ChatGPT/XNG`，或使用 `%LocalAppData%/XNG` 的一鍵部署。Hub 會驗證安裝標記、服務歸屬與接口，不連開發者的電腦。

附帶 `.cmd` 啟動器使用 Windows PowerShell，僅當次程序採 `RemoteSigned`，不更改永久政策；組織政策仍優先。從網路下載的腳本需在確認來源後依自己的系統規則處理。

標準使用者可使用靈動島 Setup，內含 Hub 與 Node；以上是進階手動部署路徑。重新安裝 Windows 後需以自己的金鑰及新帳本重新部署；Windows DPAPI 私人檔不保證跨重灌可解密。

## API 與額度

服務固定在 `127.0.0.1:8890`，每個入口都需要 `data/hub.token` 的本機 Bearer 認證，拒絕瀏覽器 Origin 與非本機 Host；沒有 LAN 或雲端共用接口。

| 接口 | 用途 |
| --- | --- |
| `GET /health` | 0.4.0、`gemini_first_planning` 與可信時鐘能力 |
| `GET /usage` | 經過 Hub 的共用用量，不是 Google 全專案即時剩餘 |
| `GET /xng/status` | XNG 安裝與連線狀態 |
| `POST /assistant` | `text/history/mode/search_mode`，可選 inline PNG |
| `POST /chat` | 保留純文字低階接口，不新增搜尋 |

一般聊天會計次；搜尋通常兩次。每次生成各自 countTokens、原子預扣，失敗也可能扣額度。原有每分鐘／每天／token／429硬鎖保留；規劃失敗可退回免費搜尋證據，不自動重試 Gemini 或改用付費服務。私人普通聊天不因規劃失敗自動外送搜尋。

本機 Free 確認綁定專案、完整金鑰雜湊、模型與預算；不是雲端即時計費保證。不能以金鑰直接查到完整專案的精確餘額。模型清單固定 `gemini-3.5-flash-lite`，不提供3.8或生圖備援。

回應保留既有欄位，新增 `planning/clock/planning_usage/gemini_completed_calls`；`usage` 仍是回答輪，第一輪 token 在 `planning_usage`。成功完成次數不代表失敗請求免費。

## 驗證

```powershell
./Test.ps1 -NodeExe node
```

Node 24 下91項合成測試，另有8項 Windows 設定與寫入失敗回復測試，使用臨時副本及合成政策，不接私人服務或模型、不讀真實帳本。已在開發環境實際核對聊天、遊戲活動、節日、Steam、名稱釐清及記憶判斷；發布包不含真實呼叫的私人紀錄。

搜尋涵蓋、官方來源分類及活動日期仍可能不完整，沒有零錯誤保證。其他 AI 程式要共用此理解層，請接 `/assistant`；直接接 XNG 仍是免費搜尋。

## English

Gemini Hub 0.4.0 remains an independent shared service. Island Setup v0.24.0 includes its code and verified Node runtime while preserving private policy, keys and usage. Auto text requests go through Gemini understanding first; ordinary chat uses one generation, research usually two with XNG evidence between them. Explicit chat/web modes remain available. Existing XNG APIs and algorithms are unchanged.

On Windows, deploy to the current user's Documents/ChatGPT/GeminiHub, supply an official Node 24 runtime at runtime/node.exe, initialize a fresh local policy/ledger/auth token, enter your own key through the hidden local prompt, verify your own Free project and limits, then start the service. The example verification is intentionally expired and contains no real credential binding. Preserve policy.json, data, runtime and usage history when updating an existing service. Never copy the developer's credentials or assume their quota applies to your project.

Run Test.ps1 with Node 24 for 91 isolated synthetic checks, plus Test-Setup.ps1 for 8 Windows DPAPI and rollback checks. Loopback-only authentication, quota guards, no automatic model retries and no paid fallback remain enforced. See the repository MIT license.

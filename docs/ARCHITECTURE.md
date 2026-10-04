# 模組與相容性

```mermaid
flowchart LR
    UI[終末地 靈動島] --> HUD[本機硬體資料]
    UI --> Media[MusicPlayerHost / Windows 媒體]
    UI --> Notice[Windows 通知]
    UI --> Personal[本機提醒與分類記憶]
    UI --> Hub[獨立 Gemini Hub · 8890]
    Hub --> XNG[獨立 XNG AI Search Hub]
    Hub --> Gemini[使用者的 Gemini 專案]
```

`src/EndfieldIsland` 是 Avalonia 前端；`src/MusicPlayerHost` 是背景 WebView2 原頁播放器。搜尋算法、證據排序與 Gemini 用量控制由共用後端維護，不複製到各個 UI。

AI 前端目前依賴 Gemini Hub 的 `/assistant`、`/usage`、`/xng/status` 及本機 Bearer 驗證。Hub 固定本機 8890；狀態辨識與 XNG 自訂安裝由 Hub 負責。此儲存庫只包含使用端，沒有配送 Hub。一般 HUD 與個人提醒不需要呼叫模型。

為維持升級相容，保留 `EndfieldChargePlus` 命名空間、EXE、互斥鎖、開機啟動識別與資料目錄。Windows Setup 使用固定 AppId，程式放在每使用者安裝目錄；升級及解除安裝不刪除個人資料。個人資料不會搬到 Git 工作目錄。

Ctrl+V 圖片僅在記憶體預覽；送出才交給 Hub 判讀，必要時由 Hub 接 XNG。對話只保存附圖佔位文字。Hub 可在既有回答提出個人原句分類；前端再核對分類、原句與本機保存規則，成功保存後才顯示確認。圖片內容不進入個人記憶。

通知輪詢一次處理完成才開始下一次；四項效能讀值共用資料提供者。視窗外緣依實際繪製 alpha 計算互動區域，縮放或尺寸變化才重算。

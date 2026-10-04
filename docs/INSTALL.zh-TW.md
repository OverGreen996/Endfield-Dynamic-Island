# 安裝「終末地 靈動島」

## 便攜版

1. 前往本專案 GitHub Releases，下載 Windows x64 ZIP。
2. 解壓到固定資料夾。不要只複製 EXE，播放器與執行環境也必須保留。
3. 雙擊 `EndfieldChargePlus.exe`，從系統匣開啟設定。可自行建立捷徑並命名「終末地 靈動島」。
4. 設定顯示器、位置與縮放，按「儲存並套用」。
5. 通知模組按「要求通知讀取權限」；Windows 的勿擾與來源設定會影響通知是否產生。
6. 音樂模組貼上自己的 YouTube 播放清單。專用清單播放需要 Microsoft Edge WebView2 Runtime。

## AI 與共用搜尋

這個下載包是靈動島前端，**不包含 Gemini Hub 後端與金鑰**。

已有本機 Gemini Hub / XNG 的使用者可直接沿用。AI 前端目前連到 `http://127.0.0.1:8890`，驗證檔與工具位於 Windows「文件」中的 `ChatGPT/GeminiHub`。若服務未啟動、驗證檔缺少或免費驗證到期，介面會顯示原因，不會默默改用付費 API。

XNG 安裝與更新請使用 [XNG-Plugin](https://github.com/OverGreen996/XNG-Plugin)。每位使用者自行架設自己的搜尋服務，公開配送站提供核心與規則；不使用原開發者的私人主機。只安裝 XNG 仍需另外部署符合接口的 Gemini Hub，AI 島才能透過它搜尋。

新使用者可先使用 HUD、音樂與通知。Gemini Hub 尚未由本儲存庫提供可一鍵安裝的公開包；不要把 AI 頁面存在視為後端已備妥。

## 更新

關閉舊版，下載新版並完整解壓，再啟動。使用者資料保留在 `%LocalAppData%/EndfieldChargePlus`；更新時不要刪除它。設定的「檢查更新」指向這個專案，僅檢查版本，沒有自動覆寫正在運行的程式。

## 重灌電腦

從本專案 Releases 重新下載；重新部署自己的 XNG 與 Gemini Hub。Windows 使用者加密的對話與記憶無法保證跨重灌解密。設定匯出可能包含私人服務設定，請自行保存且不要公開上傳。

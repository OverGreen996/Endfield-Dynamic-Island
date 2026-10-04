# 安裝「終末地 靈動島」

1. 到 [GitHub Releases](https://github.com/OverGreen996/Endfield-Dynamic-Island/releases/latest) 下載 `Endfield-Dynamic-Island-Setup-v0.23.1-win-x64.exe`。另有 SHA256 可核對完整性。
2. 從系統匣退出舊版，執行 Setup，選擇繁體中文或 English。
3. 預設安裝到 `%LocalAppData%\Programs\EndfieldDynamicIsland`，可更換位置。安裝至目前使用者，不需要管理員權限。
4. 選擇桌面捷徑，核對位置後安裝。從桌面或開始功能表開啟。
5. 設定顯示器、位置、縮放；通知權限由使用者在設定自行要求，安裝程式不會代為啟用。
6. 音樂貼上自己的 YouTube 清單。專用清單需要 Microsoft Edge WebView2 Runtime，Setup 不會自動下載它。

本程式未簽署商業憑證，SHA256 不代表 Authenticode 簽章。

## AI 與搜尋

安裝包包含前端、.NET runtime 及播放器，**不包含 XNG、Gemini Hub、金鑰或私人設定**。

AI 使用 `127.0.0.1:8890` 的本機 Gemini Hub，驗證檔及工具在 Windows「文件」的 `ChatGPT/GeminiHub`。服務缺少或用量鎖定時會顯示原因，沒有付費備援。建議 Hub 0.2.1 或更新版本，使用必填的結構化記憶判斷；貼圖仍相容 0.2.0。Hub 與靈動島需各自更新，安裝靈動島不會自動更新 Hub。

AI 輸入框按 `Ctrl+V` 貼圖，預覽不呼叫模型，送出才判讀。聊天模式通常一次 Gemini；辨識後再搜尋並整理通常兩次。失敗請求也可能扣額度，以 Hub 帳本為準。

[XNG-Plugin](https://github.com/OverGreen996/XNG-Plugin) 由每位使用者自行架設；公開站配送核心及規則，不使用開發者的私人主機。只安裝 XNG 不會補齊 Gemini Hub。本儲存庫已提供 [Hub 0.3.0 原始碼、更新及手動部署教學](../integrations/gemini-hub)，尚未配送 Hub 一鍵安裝包。自動模式先由 Gemini 理解，再交 XNG 找資料；一般聊天通常一次、搜尋通常兩次生成，均計次。只更新 Hub 即可沿用靈動島 v0.23.1。

## 更新及解除安裝

退出程式後執行新版 Setup，相同識別沿用安裝位置與捷徑選項。既有開機啟動若已啟用會更新到新位置；未啟用者不會被強制開啟。

Windows「已安裝的應用程式」可解除安裝。程式與安裝建立的捷徑會移除；`%LocalAppData%\EndfieldChargePlus` 中的設定、對話、提醒、記憶及播放器環境會保留。獨立 XNG／Gemini Hub 不會移除。

## 重灌

重新下載 Setup，重新部署自己的 XNG／Gemini Hub。DPAPI 加密內容不能保證跨重灌解密。設定匯出可能含私人服務設定，請自行保存，不要公開上傳。

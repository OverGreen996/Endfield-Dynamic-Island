# 安裝「終末地 靈動島」

1. 到 [GitHub Releases](https://github.com/OverGreen996/Endfield-Dynamic-Island/releases/latest) 下載 `Endfield-Dynamic-Island-Setup-v0.24.0-win-x64.exe`。另有 SHA256 可核對完整性。
2. 從系統匣退出舊版，執行 Setup，選擇繁體中文或 English。
3. 預設安裝到 `%LocalAppData%\Programs\EndfieldDynamicIsland`，可更換位置。安裝至目前使用者，不需要管理員權限。
4. 選擇桌面捷徑，核對位置後安裝。從桌面或開始功能表開啟。
5. 設定顯示器、位置、縮放；通知權限由使用者在設定自行要求，安裝程式不會代為啟用。
6. 音樂貼上自己的 YouTube 清單。專用清單需要 Microsoft Edge WebView2 Runtime，Setup 不會自動下載它。

本程式未簽署商業憑證，SHA256 不代表 Authenticode 簽章。

## AI 與搜尋

安裝包包含前端、.NET runtime、播放器、Gemini Hub 0.4.0 與已核對 SHA256 的 Node 24.21.0。**不包含 XNG、金鑰或任何私人設定。**

AI 使用 `127.0.0.1:8890` 的本機 Gemini Hub，服務程式、驗證檔及工具在 Windows「文件」的 `ChatGPT/GeminiHub`。安裝程式建立本機驗證與帳本；初次安裝預設停用模型呼叫。到設定 → AI 助理貼上自己的金鑰，勾選已確認 Free／未啟用付費後按「儲存並套用金鑰」，不需要另裝 Node 或手動啟動服務。設定與啟動不呼叫 Google。

既有同一金鑰的限制、帳本與私人檔案保留。新金鑰的預設本機上限為 20 次／日、3 次／分鐘，這不是 Google 實際配額；要提高限制必須先核對自己的專案。Google 全專案剩餘額度仍需在 AI Studio 查詢。已運行的 Hub 安裝時會保留；更新核心後執行 `Restart-GeminiHub.ps1`，或在 AI 設定重新套用同一把金鑰，明確重啟生效。服務異常會在安裝完成頁顯示，不接管占用 8890 的其他程式。

AI 輸入框按 `Ctrl+V` 貼圖，預覽不呼叫模型，送出才判讀。聊天模式通常一次 Gemini；辨識後再搜尋並整理通常兩次。失敗請求也可能扣額度，以 Hub 帳本為準。

[XNG-Plugin](https://github.com/OverGreen996/XNG-Plugin) 由每位使用者自行架設，與 Gemini Hub 分開管理。本儲存庫提供 [Hub 原始碼及獨立部署說明](../integrations/gemini-hub)。自動模式先由 Gemini 理解，再交 XNG 找資料；一般聊天通常一次、搜尋通常兩次生成，均計次。「只搜尋」不呼叫 Gemini。

## 更新及解除安裝

退出程式後執行新版 Setup，相同識別沿用安裝位置與捷徑選項。既有開機啟動若已啟用會更新到新位置；未啟用者不會被強制開啟。

Windows「已安裝的應用程式」可解除安裝。程式與安裝建立的捷徑會移除；`%LocalAppData%\EndfieldChargePlus` 中的設定、對話、提醒、記憶及播放器環境會保留。獨立 XNG／Gemini Hub 不會移除。

## 重灌

重新下載 Setup，即可重建 Gemini Hub；到 AI 設定輸入自己的金鑰並確認免費方案。XNG 仍使用其獨立的一鍵部署工具。DPAPI 加密內容不能保證跨重灌解密。設定匯出可能含私人服務設定，請自行保存，不要公開上傳。

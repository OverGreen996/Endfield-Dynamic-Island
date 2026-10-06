# 隱私與資料

使用者資料保留在 `%LocalAppData%/EndfieldChargePlus`，不放在程式原始碼目錄。

- 設定、音樂清單與頭像：本機檔案。
- 對話、提醒與記憶：Windows DPAPI 加密，綁定使用者環境。
- Windows 通知：只保留暫時佇列，不建立永久通知歷史，也不傳給任何 AI 或搜尋 API。
- Gemini、Groq、Cloudflare 與搜尋 API Key：由主程式使用 Windows DPAPI 加密保存，從不回顯已存金鑰。Cloudflare Account ID 與 Token 一起加密。
- 送出的聊天與必要上下文直接傳給當次使用的 Gemini / Groq / Cloudflare；備援時同一內容可能依序傳给不同供應商。公開查詢直接傳給搜尋 API，沒有本機 HTTP 中繼服務。請勿把本機加密誤認為送出的聊天永不傳送。
- Ctrl+V 貼圖先建立記憶體預覽，送出後由 Windows 本機 OCR 讀字，不上傳圖片。已設定 AI 時，只貼圖也會將辨識文字與必要上下文傳給當次文字模型，由 AI 決定是否查詢搜尋 API；另附文字時一起理解。沒有 AI 設定或只搜尋模式無附加文字時才直接本機擷取，不呼叫模型。圖片本身不保存至對話或記憶；OCR 回覆文字則與其他對話一樣加密保存在聊天紀錄，圖片中的個人敘述或指令不自動變成記憶／提醒。移除、新對話或關閉視窗會清除圖片暫存。
- 模型輔助記憶只接受本次使用者明確原句；可在記憶宮殿逐筆刪除。
- YouTube 背景播放器連到 YouTube 原頁，使用自己的獨立瀏覽環境。
- 啟動時的版本檢查會連到本專案 GitHub。

公開原始碼與發佈包不含金鑰、token、聊天、記憶、頭像、通知、使用紀錄與個人清單。匯出設定不會自動去除所有私人服務設定，請勿直接公開。

AI 政策、加密金鑰及帳本繼續保存於 Windows「文件」的 ChatGPT/GeminiHub，沿用此資料位置以免重設用量；此目錄已不代表獨立執行服務。搜尋資料位於其 data/search。Daily 使用自己的資料，不讀寫此目錄。

備援憑證保存於此目錄的 `data/assistant-backup-keys.dpapi`；累計請求/token 與冷卻原因保存於 `data/assistant-backup-state.json`，不包含聊天或金鑰。兩個檔案均不配送。關閉備援設定會清除輸入框；空白儲存保留原憑證，明確移除才清除。

人格庫：`data/assistant-personas.dpapi` 以目前 Windows 使用者加密，獨立於記憶宮殿。只將本輪已啟用的角色人格與互動規則送給當次模型，不傳送其他人格。人格保存與切換不發出 API 請求。

Gemini 額外主力金鑰：`data/gemini-pool-keys.dpapi` 加密保存第 2–5 組；原本第 1 組仍使用既有金鑰與帳本。`data/gemini-accounts/*.sqlite` 只記錄額外金鑰的本機用量、冷卻與恢復狀態，不記錄聊天或金鑰明文。輪替會將相同的必要上下文傳給接手的 Gemini 帳號；它們受各帳號的 Google 資料條款約束。這些私人資料均不配送。

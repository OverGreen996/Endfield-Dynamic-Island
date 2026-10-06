# 操作

三家模型共用直接回答、自然繁中與搜尋資料整理的規則。分享與偏好簡短承接，問題給實際答覆；人格可以影響措辭，但不必每句提及設定。一般回答若出現多餘問句或客服式邀請，最多再生成一次；不每輪固定潤稿，也不改動這輪已判斷的記憶與提醒。模型仍可能偶爾產生套話。

| 操作 | 行為 |
| --- | --- |
| Alt+A | 喚出 AI 並直接聚焦輸入 |
| Enter / Shift+Enter | 送出／換行；保留輸入法候選操作 |
| AI 島標頭／Esc | 向上收起，不清除對話 |
| AI 右鍵 → 新對話 | 清除目前對話上下文 |
| Alt+M | 顯示音樂島，保留目前模式 |
| 音樂右鍵 | 切換清單模式、跟隨瀏覽器或設定清單 |
| 一般 HUD / 通知本體 | Preview → Pinned → Hide |
| 透明區域 | 可點到底下視窗 |

## 音樂模式

清單模式控制專用背景播放器；跟隨瀏覽器模式使用 Windows 媒體工作階段。切到瀏覽器會關閉專用播放器，兩者不混用。清單隨機狀態會沿用到下一首；重播可循環切換清單／單曲／關閉。瀏覽器模式只啟用該媒體工作階段支援的控制。

## 提醒與記憶

提醒由 AI 理解時間與事項，例如「明早九點叫我去拿包裹」，不必包含「提醒我」。不明確時先釐清；本機驗證未來一年內的台灣時間、成功保存後才回報。到點通知不呼叫模型。可在記憶宮殿檢查結果並刪除。

新回覆到達後會快速逐字顯示；較長回答自動加快。通知切換或收起時暫停，回到 AI 後繼續。往上看舊訊息時不強制捲到底；舊對話直接顯示，不重播。完整答案會先加密保存，顯示效果不會增加模型請求。

聊天入口已移除口令、句型與關鍵字路由。AI 一次理解本次對話，獨立判斷是否搜尋、記憶及建立/取消提醒；同一句可同時搜尋並保存多筆個人資訊。無需說「記住」或重複強調。AI 自訂簡短中文分類，優先沿用合適既有名稱；分類與文字均可手動修改。玩笑、轉述、暫時情緒及不希望保存的內容由 AI 判讀為不保存，本機僅驗證格式、原文來源、識別碼與實際儲存結果。AI 仍可能判斷不準，可隨時檢視或刪除。

記憶只來自本次使用者的對話原文；搜尋後整理不再提取記憶，網頁及助理回答不當成你的個人資料。明確更正可更新同一筆，重複資訊不重建。只搜尋模式不呼叫 AI，也不建立記憶或提醒。

音樂、通知與效能總覽沿用原作者的細膠囊與黃綠圓形徽章。效能圓環顯示 GPU 使用率，音樂圓環顯示曲目進度，通知圓環顯示剩餘時間；通知入場完成後才開始閱讀倒數，固定後正文可捲動。

新通知顯示結束後返回先前頁面；若原先在 AI 頁，保留草稿與對話。滑鼠停留延長預覽，固定狀態要再點本體才收起。

## AI 與搜尋

設定 → AI 助理：貼上自己的 Gemini 金鑰並確認免費方案。儲存立即生效；本機用量不是 Google 全專案的即時剩餘額度。

設定 → AI 助理 → AI 模型備援：填入 Groq Key，以及 Cloudflare Account ID / API Token。文字助理依序 Gemini → Groq GPT-OSS 120B → Cloudflare Qwen3.8-27B。三家沒有本機次數與累計 token 上限，遇到官方限流或額度錯誤自動切換；不需額外服務。圖片文字改用 Windows 本機 OCR；單純擷取文字不需模型金鑰。

Gemini 日額度依太平洋午夜恢復，台灣夏令期間 15:00、冬令期間 16:00；短暫限流依官方重試時間。Cloudflare 日額度於台灣 08:00 恢復。Groq 依 `Retry-After`、回應中的恢復時間與 rate-limit headers 冷卻，不猜固定午夜。到期在下一次對話重試優先供應商，不在背景反覆探測。設定頁顯示本機累計與重試時間；這不是官方即時剩餘額度。一般 HTTP/網路錯誤短暫冷卻，金鑰或權限錯誤需檢查設定。

回答改用自然聊天語氣，不展示來源清單或引用編號，也不套查證報告。搜尋資料在背景整理；只有缺少關鍵資訊才追問。當下情緒、抱怨或這一次聊天要求不當成長期偏好。記憶成功後用簡短提示確認，分類與原文在記憶宮殿管理。

AI 右鍵 → 搜尋 API 與輪替：設定金鑰與順序。Exa、Tavily 無本機額度上限；API 搜尋失敗即停用到下個月 2 號台灣時間 00:05，期間依序輪替至其他可用供應商（Exa → Tavily → Firecrawl）。到期在下一次搜尋重試，若仍失敗則封鎖至再下一個月 2 號。取消搜尋不算失敗。設定頁顯示原因與恢復時間；此日期是本機重試規則，並非官方補額度承諾。Tavily 保留官方餘額按鈕，但數值僅供參考；搜尋前不查餘額，也不以餘額封鎖。Firecrawl 無本機點數上限，搜尋前核對官方 API 餘額（短暫快取一分鐘並扣除已用點數）。帳單切換日與時間可手動設定，均為台灣時間；額度不足停用至該日，到期在下一次搜尋查證已補點數才恢復，否則隔一小時再查。日期不存在時採月底。官方餘額按鈕會呼叫供應商，但查詢 Tavily 餘額不會解除或延長搜尋封鎖。

自動模式先理解問題與記憶；Gemini／Groq 一般聊天每輪兩次生成，CF 一般聊天同次生成直接完成答覆與記憶判斷，通常一次。必要澄清一次；已有答覆卻多了追問時最多補一次重寫。需要查資料才插入搜尋，搜尋＋AI 仍是一次理解、一次整理，不另外呼叫模型潤稿。只搜尋模式不呼叫 Gemini。Ctrl+V 貼圖先預覽，送出後 Windows 本機 OCR 讀字。已設定 AI 時，只貼圖也會把 OCR 文字與前文交給 AI，判斷要回應、搜尋或必要釐清；不需固定口令。未設定 AI，或在只搜尋模式下無附加文字時，才直接擷取文字。圖片不上傳，OCR 文字不可自行建立記憶或提醒。

搜尋設定的「每輪搜尋換一家」可自行開啟：每次成功搜尋後，下輪從順序中的下一家開始，循環分散同一家 API 的請求頻率。缺少金鑰、手動停用、冷卻或月度封鎖的供應商會跳過；故障時當輪仍由下一家接手，下輪接著成功供應商的下一家。純聊天不搜尋，快取命中、取消或全數失敗不推進輪替。位置重開程式後保留；一般儲存不重設，變更順序或模式才從頭開始。關閉時回到原本優先順序。輪替分散請求，並不減少總搜尋用量。

AI 與搜尋在主程式中執行。Daily 的金鑰、額度及資料分開保存。資料位置見 [隱私說明](PRIVACY.md)。

## AI personas / AI 人格

設定 → AI 助理 → 管理 AI 人格。左邊選擇已存人格，右邊分開輸入名稱、角色人格與互動規則。新增莊方宜範例後可自由編輯；「儲存人格」只保存，「使用此人格」才切換。使用中的人格再次儲存會在下一則訊息更新。Ctrl+S 儲存。切換列表保留草稿，關閉時可繼續編輯或明確捨棄。刪除需再按一次確認；刪除正在使用的人格回到預設助理。

Settings → AI Assistant → Manage AI personas. Select saved profiles on the left; edit name, character and interaction rules separately on the right. The Zhuang Fangyi-inspired example is editable. Save stores the profile; Use this persona activates it on the next message. Saving edits to the active persona updates its next reply. Ctrl+S saves. Selecting other profiles keeps drafts; closing offers keep editing or discard. Delete requires a second click, and deleting the active profile restores the default assistant.

人格只影響語氣與互動，不清空對話，不建立使用者記憶，不改動提醒。初次開啟使用預設助理；保存與切換不呼叫 AI。人格及互動規則每區最多 6,000 字、合計 10,000 字，最多 30 組，可隨時刪除。

Personas shape voice and interaction without clearing history, adding personal memories or changing reminders. Default assistant remains active initially. Saving and switching make no AI call. Each text field supports 6,000 characters, 10,000 combined; up to 30 custom profiles can be saved.

## Gemini 五組主力 / Five Gemini primaries

同一頁點「設定 Gemini 五組主力輪換」。第 1 組沿用原本主金鑰，第 2–5 組在新視窗貼入並確認免費方案。空白保留；勾移除才清除。依序持續使用第一個可用帳號，受限才接下一組，五組皆不可用才接 Groq → Cloudflare。恢復後重新優先使用較前面的帳號；這不是每輪平均分配。所有組使用相同的選取後上下文與已啟用人格，圖片文字在本機 OCR 讀取。

Use Configure five-account Gemini rotation on the same page. Slot 1 keeps the existing primary key; paste slots 2–5 in the new window and confirm free plans. Blank keeps saved credentials; explicit removal clears a slot. Keep using the first available account until unavailable, then move to the next. Only when all five are unavailable do Groq → Cloudflare take over. Higher-priority accounts return after recovery; this is sequential priority, not per-turn round-robin. Context and active persona stay continuous, image text is extracted locally by Windows OCR.

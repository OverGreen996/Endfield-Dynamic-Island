# 變更紀錄 / Changelog

## v0.28.14

- 修正只貼圖直接回傳 OCR 字句：已設定 AI 時，辨識文字接上前文、人格與既有文字助理，判斷回應、搜尋或必要釐清，不需另外打搜尋口令。圖片仍留在本機，記憶／提醒只核對使用者原始文字。
- 保留沒有 AI 設定及只搜尋模式無附加文字時的本機擷取；空白圖片、取消及大小限制沿用。
- Captionless pasted images now use recognized text and context for normal AI responses. Raw images stay local; image text cannot authorize personal actions.

## v0.28.13

- Gemini、Groq、CF 共用自然對話原則：先回答當輪重點，依問題提供判斷、比較、數量或做法；個人分享與回覆偏好簡短承接，减少未被問到的教學、客服收尾與查證報告口吻。人格、事實、搜尋與操作邊界保留。
- Gemini／Groq 的正式答覆承接理解階段的答覆草稿，草稿標為非新使用者資料，不能再提取記憶或操作。CF 一般聊天仍一次生成；搜尋仍理解後整理，不固定添加額外潤稿。
- 多餘末尾問題或明確的客服式邀請最多重寫一次；必要澄清保留，引用某句話的解釋不因詞句相同而觸發邀請重寫。重寫無法覆蓋記憶與提醒。
- Groq 不再把嚴格 JSON schema 重複貼入系統提示。理解階段只使用核心對話原則，語氣範例留給專門回答與 CF 合併回答，減少不必要輸入量。沒有縮短使用者上下文／人格、換模型或增加本機額度限制。
- Shared conversation guidance now applies to Gemini, Groq and Cloudflare. Dedicated replies receive the understood draft as data; an unwanted closing question or stock invitation may receive one bounded rewrite. Structured output and grounded memory/actions remain independent. Provider voice and latency can still vary.

## v0.28.12

- CF 一般聊天改為一次生成同時完成理解、個人記憶判斷與最後答覆，省去普通聊天的第二次 CF 請求；已啟用人格與上下文完整保留。Gemini／Groq 的答覆流程與備援順序沿用。
- 搜尋題仍先理解、取得資料再整理，維持兩次生成；必要澄清一次。已有答覆卻多了追問時，最多補一次重寫，不重新提取記憶或提醒。
- 保留原文驗證、JSON 契約、取消及操作成功後才確認的限制。沒有縮短記憶／人格、換低階模型或減少搜尋資料；服務端忙碌仍可能逾時，不保證每次加快固定比例。
- 真實 CF 測試顯示單次流程可减少等待與生成請求，細節見 VALIDATION。降低 reasoning_effort 的試驗速度不穩定，未採用；語氣仍可能帶有 CF 套話。
- Cloudflare ordinary chat reuses the validated understanding response, preserving memory decisions, persona and context while avoiding a second generation. Search still plans and writes separately. Unnecessary closing questions may trigger one rewrite; provider latency remains variable.

## v0.28.11

- Gemini、Groq、Cloudflare 共用更直接的回答規則：先完成統計、比較或排錯，不以背景介紹代替結果；缺資料時說清缺口，不補猜數字或專有名詞。
- 搜尋整理明確保留計算範圍、數量與單位，避免把累計值重複相加；完整與部分資料分開表達。聊天用自然繁體中文與純文字列點，避免露出 Markdown 標記。
- 搜尋後只產生答覆，不再輸出記憶／提醒欄位；理解階段的個人記憶判斷仍保留。區分徵詢意見與執行要求，不盲目附和或假稱已修改。
- 一般文字聊天採理解／記憶判斷 → 專門答覆兩步，不再直接顯示分類階段的草稿。延續同一上下文與人格；不額外搜尋，一般聊天每輪兩次生成，必要澄清仍只有一次。
- 已能回答卻追加末尾問句的對話，最多重寫一次；不再次提取記憶或操作。必要澄清不刪除。Cloudflare 升級至 Qwen3.8-27B，使用結構化輸出與直接答覆模式，避免冗長推理拖延。較舊 Qwen 模型更快消耗免費計算額度，仍維持最後備援。
- Ctrl+V 圖片改用 Windows 本機 OCR 擷取文字。只貼圖送出直接顯示文字，無模型或搜尋請求、不上傳圖片；另附問題時以辨識文字進入正常文字助理／搜尋流程。支援 Windows 已安裝的繁中／英文 OCR，辨識不到文字時不猜測、不建立記憶或提醒。
- 既有人格、記憶、金鑰、模型輪替與靈動島介面沿用。
- Shared response guidance prioritizes requested results, grounded quantities and natural conversation. Pasted images use local Windows OCR; extraction alone needs no API key or request, and captioned questions use only recognized text.

## v0.28.10

- 音樂、通知與效能總覽回到 QinAnze 原 HUD 的 560×60 膠囊比例、炭灰實心背景、小方形圖標、白字透明度層級和黃綠圓形徽章；移除大型 Logo、常駐標題、外緣黃弧及四格表格裝飾。
- 四項效能保持 CPU／GPU／RAM／VRAM 名稱、即時數值與容量資訊；右側 GPU 徽章與 GPU 使用率一致，既有方案、副本與採樣機制沿用。效能總覽預設完整動畫，手動叫出走原版波紋；常駐與返回頁面保留快速切換。
- 音樂保留上一首／下一首／隨機／重播／拖動進度，播放鍵結合圓形曲目進度；跟隨全域縮放，以原版的圖標、波紋、標題與收窄順序入場。收起、通知中斷及重新開啟會取消舊動畫；Windows 關閉動畫時直接顯示。
- 通知改成同高膠囊與圓形倒數／固定徽章，共用原版入場動畫；動畫結束後才開始閱讀倒數，固定通知可捲動正文。通知優先、返回原頁與草稿保留不變。
- AI 介面、人頭像與人格功能未修改；介面驗證使用合成播放與資料，不呼叫模型 API。
- Music, notifications and the performance overview now share the upstream capsule proportions, typography, small icon and circular badge. The upstream ripple choreography is reused for music; AI visuals remain unchanged.

## v0.28.9

- 修正「效能總覽」另存副本後只剩空殼：四項指標排版獨立保存，副本仍讀取 CPU、GPU、RAM、VRAM 並支援背景採樣；重新開啟保留名稱、配色和 GPU 選擇。
- 自動偵測 GPU、修改 GPU／時間選項不再產生無意義副本；單純選取內建方案並儲存會保留原方案。
- 自動修復舊版空白效能總覽副本的顯示模式，保留原 ID、名稱、設定與輪播順序；普通自訂模板不轉換。
- Fix overview copies losing their metric layout. Device options no longer create redundant copies, and legacy blank overview copies are repaired while retaining user settings.


## v0.28.8

- 新增 AI 人格庫：角色人格與互動規則分開編寫，支援新增、儲存、複製、刪除與切換；莊方宜為可編輯範例，預設助理保留原有行為。未儲存草稿可在列表間切換而不丟失，關閉需明確捨棄。人格以 Windows DPAPI 加密保存。
- 每輪固定一份已啟用人格，切換從下一輪生效；Gemini、Groq、Cloudflare 使用同一人格，圖片直接聊天也套用。人格不會被寫成使用者記憶，儲存與切換不呼叫模型。
- Gemini 主力擴充為最多五組，依序使用 1 → 2 → 3 → 4 → 5；目前組受限才用下一組，全部不可用才接 Groq → Cloudflare。按官方恢復時間自動重試較優先帳號，圖片同樣由 Gemini 主力池處理。第 1 組沿用原金鑰、政策與帳本，新增組獨立加密與記錄用量，設定不重設冷卻或用量。
- 人格與 Gemini 設定採用內嵌標題列、中英文與窄視窗排版。多帳號並非保證額度增加；Google 限制按專案計算，使用須遵守官方條款。


## 0.28.7 — 自然對話、模型備援與逐字回覆 / Natural dialogue, model fallback and progressive replies

- 新回覆以快速逐字效果顯示，較長回答自動加快；只更新正在顯示的文字，不重建整段歷史。
- 收起或通知優先切換時暫停，返回後從原位置繼續；舊對話不重播，查看上方訊息時不強制捲到底。
- 完整回覆先加密保存，表情符號與組合字元完整顯示；關閉或新對話會停止顯示計時器。
- New replies reveal progressively. Complete replies are saved before presentation; hiding pauses and returning resumes without replaying history or splitting Unicode characters. This changes presentation, not API requests.
- 重寫對話指令：普通問答與聊天自然接話，不套查證報告、不反覆追問、不顯示來源列表；舊對話只用於承接，不模仿其制式語氣。當下情緒與單次聊天要求不當成長期偏好。
- 一般文字助理採 Gemini → Groq GPT-OSS 120B → Cloudflare Qwen3-30B-A3B；理解、搜尋整理與記憶判断共用同一機制，圖片仍由 Gemini 判讀。
- 移除執行中的本機次數與累計 token 上限，由官方錯誤與重試資訊切換。Gemini 日額度按太平洋午夜，Cloudflare 按 UTC 午夜，Groq 按回傳的重試/恢復時間。短暫故障不封整天，用量與既有資料保留。
- 新增中英文備援設定、Windows 加密金鑰與獨立連線測試；程式直接呼叫 API，不需要額外背景服務。備援金鑰需自行設定，安裝包不含私人憑證。
- 搜尋新增可選「每輪搜尋換一家」：按自訂順序循環，成功後下一輪換供應商，跳過不可用項目。位置跨重開保留；純聊天、快取命中、取消或全數失敗不推進，既有封鎖與帳單規則不變。預設沿用優先順序。
- Optional per-turn search rotation distributes successful search turns across the configured order. Progress persists across restarts; cache hits and cancellations do not advance it. Existing fallback and recovery policies remain in effect.
- Dialogue is conversational, without report templates or source lists. The native text fallback chain uses Groq GPT-OSS 120B and Cloudflare Qwen3 after Gemini; provider errors and reset information replace local quota caps. Images remain on Gemini. Keys are encrypted locally and must be supplied by the user.

## 0.28.6 — AI 對話判斷與動態記憶分類 / AI intent and dynamic memory categories

- 移除聊天入口的口令、句型與關鍵字路由；AI 一次決定聊天/搜尋，以及獨立的多筆記憶和本機提醒操作。
- 記憶分類由 AI 建立簡短中文名稱，優先沿用現有名稱；可手動修改分類與原文，明確更正更新同一筆。
- 搜尋前提取記憶，搜尋結果及助理整理不能變成個人事實；成功寫入後才確認操作。
- 400 格式拒絕不再誤鎖為用量不明；保留請求紀錄，仍保護真正不明用量。
- AI classifies natural conversation without command phrases, supports search plus multiple memories, and creates editable Chinese categories. Existing personal data is not cleared by the installer.

## 0.28.5 — Firecrawl 官方額度與手動帳單日 / Firecrawl balance and billing calendar

- 移除 Firecrawl 本機點數上限，以官方 API 剩餘點數決定是否可搜尋。
- 可設定每月帳單切換日與時間（台灣時間），不足時停用至設定日期；到期核對補額度，未補則隔一小時再查。
- 沒有該日的月份使用月底；中英文設定顯示下次帳單切換、官方餘額及核對時間。
- All three providers have no local quota cap. Exa/Tavily retain their monthly failure stop; Firecrawl uses official credits and a user-set billing calendar.

## 0.28.4 — Tavily 舊餘額紀錄遷移 / Tavily usage-state migration

- 舊版搜尋前查餘額的錯誤不轉成月度搜尋封鎖；只有實際 API 搜尋失敗觸發新規則。
- 保留金鑰、用量和已發出搜尋的月度封鎖；遷移不呼叫 API。

## 0.28.3 — Tavily 失敗後自動輪替 / Tavily failure rotation

- Tavily 移除本機點數上限，與 Exa 共用失敗封鎖規則：停用至下個月 2 號台灣時間 00:05，到期於下一次搜尋重試。
- 搜尋不先查 Tavily 用量、不以官方剩餘數值封鎖；手動查詢餘額僅供參考，不改變搜尋封鎖時間。
- 保留既有金鑰與用量，搜尋設定中英文顯示失敗原因及恢復時間。
- Firecrawl retains its own credit cap and billing cycle. Exa and Tavily use one monthly failure-and-retry policy.

## 0.28.2 — Exa 失敗後自動輪替 / Exa failure rotation

- 移除 Exa 本機美元額度限制與手動解封設定，保留金鑰及用量紀錄。
- Exa API 搜尋失敗後停用至下個月 2 號台灣時間 00:05，期間使用 Tavily、Firecrawl；到期於下一次搜尋重試。
- 手動取消不算失敗；重新啟動或儲存金鑰不會解除封鎖，備援快取不延後到期重試。
- 搜尋設定中英文顯示停用原因、恢復時間與請求數；更新安裝及使用教學。
- Remove Exa's local budget cap. Failed searches persist a stop until the next month's 2nd, 00:05 Taiwan time, with fallback and lazy retry.

## 0.28.1 — 安裝與教學整理 / Distribution cleanup

- 移除停用的後端原始碼、啟動及下載工具；建置流程改為原生 AI 測試。
- 修正設定提示與中英文教學，金鑰儲存立即生效。
- 新增安裝包殘留檢查；隔離測試安裝、升級與解除安裝。
- 搜尋用量明確標示 Exa 美元預算及其他供應商點數。

## 0.28.0 — 原生 AI / Native assistant

AI、金鑰設定、用量與三家搜尋輪替內建於主程式，保留既有資料及用量。Daily 獨立運行。

更早的紀錄保存在原始碼 archive 目錄，僅為歷史資料，不是目前的操作教學，也不隨安裝包配送。

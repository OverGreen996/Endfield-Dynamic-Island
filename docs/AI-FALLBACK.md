# AI 備援設定 / AI fallback setup

Gemini、Groq 與 CF 使用共用的自然對話原則；只有普通回覆出現多餘末尾問題或客服邀請才最多重寫一次。必要澄清不重寫。Gemini／Groq 的語氣範例留給正式答覆，不重複貼出 JSON schema；原模型、上下文、人格與供應商限流／備援機制沿用。

一般文字助理順序：Gemini 1 → 2 → 3 → 4 → 5 → Groq `openai/gpt-oss-120b` → Cloudflare `@cf/qwen/qwen3.8-27b`。理解問題、整理搜尋與記憶判斷使用同一順序；圖片文字由 Windows 本機 OCR 處理，圖片不傳給模型。主程式直接呼叫供應商，不必部署 Worker、Node、Docker 或中繼後端。

Gemini／Groq 一般文字聊天分成理解／記憶判斷與專門答覆，每輪兩次生成；CF 一般聊天同一次生成完成兩者，通常一次。需要查資料才加搜尋，仍先理解再整理。必要澄清一次生成，多餘收尾追問最多一次重寫。各步共用原對話、人格及本輪已選模型，失敗時沿既有順序備援。設定中的累計用量會記入每次實際生成，並非每條使用者訊息。CF 的服務端延遲仍可能波動。

Cloudflare 採 [Qwen3.8-27B](https://developers.cloudflare.com/workers-ai/models/qwen3.8-27b/) 的直接答覆模式。它比舊 Qwen3-30B-A3B 更能遵循本次對話要求，但官方每 token 計算成本較高，免費額度可支撐的次數較少，且延遲會受服務負載影響，因此保留在 Groq 之後。程式不啟用付費或新增本機額度上限。OCR 擷取文字不使用這些模型。

## 填入設定

1. Groq：[Console API Keys](https://console.groq.com/keys) → Create API Key，複製自己的 `gsk_…` 金鑰。
2. Cloudflare：[Workers AI](https://dash.cloudflare.com/?to=/:account/ai/workers-ai) → Use REST API → Create a Workers AI API Token，沿用預填權限，取得 Token 與 32 碼 Account ID。若自行建立 Token，需要 Workers AI Read 與 Edit 權限，範圍限定自己的帳號。參考 [官方設定教學](https://developers.cloudflare.com/workers-ai/get-started/rest-api/)。
3. 靈動島設定 → AI 助理 → 設定 AI 備援與金鑰，貼入上面三個值，確認免費方案後儲存。
4. 分別按「測試此服務（一次請求）」。每個按鈕只呼叫該服務，使用一次生成；不搜尋，也不寫入對話或記憶。

空白保留已存資料；明確勾選移除才清除該家的資料。密碼框不回顯已存金鑰，語言切換保留未存草稿，關閉清除輸入框。金鑰與 Account ID 綁定目前 Windows 使用者加密保存。不要把金鑰放進聊天室或 Git。

## 官方限制與恢復

沒有軟體自行設定的次數與累計 token 上限。官方回應成功就繼續使用；被限流、額度不足、服務錯誤或逾時時改用下一家。程式不會啟用付費方案，使用者仍須維持自己的免費帳號設定。

| 服務 | 恢復依據 |
| --- | --- |
| Gemini | RPD 每日太平洋時間午夜重設；台灣在太平洋夏令期間 15:00、冬令期間 16:00。只有官方錯誤確認為日額度才等到該時間；短暫限流依 RetryInfo / Retry-After。 |
| Groq | 使用 Retry-After、requests/tokens reset headers 及錯誤中的 retry delay；不假定固定午夜。官方 headers 的 requests 表示 RPD、tokens 表示 TPM，不能把它誤當 TPD。缺少資訊時短暫冷卻後重新確認。 |
| Cloudflare | 錯誤 3036 為日額度，等待下一個 UTC 午夜，即台灣 08:00；其他短暫錯誤短暫冷卻。 |

到期在下一次對話才試優先供應商，不額外定時呼叫模型探測。用量頁是本機紀錄，不是官方帳號即時餘額。所有服務都暫不可用時顯示簡短狀態；不編答案。更換金鑰保留已記錄的用量。

依據：[Gemini rate limits](https://ai.google.dev/gemini-api/docs/rate-limits)、[Groq rate limits and headers](https://console.groq.com/docs/rate-limits)、[Cloudflare pricing/reset](https://developers.cloudflare.com/workers-ai/platform/pricing/)。

## English

Open Settings → AI Assistant → Configure AI fallback and keys. Get a Groq key from its Console, and a Cloudflare Account ID and API Token from Workers AI → Use REST API. Custom Cloudflare tokens require Workers AI Read and Edit permissions for your account. Save after confirming that paid billing is disabled. Each provider test uses one generation; saving uses none.

Text requests prefer Gemini, then Groq GPT-OSS 120B, then Cloudflare Qwen3.8-27B. Windows OCR reads image text locally; raw images are not uploaded. All adapters run directly inside the app. Local daily/minute request and cumulative token caps are removed; provider responses trigger fallback and cooldowns. Gemini daily quotas reset at midnight Pacific time, Cloudflare daily quotas at midnight UTC, and Groq recovery follows its response headers or retry instructions. Recovery is tested on the next conversation, not by background API polling.

Keys are encrypted for the current Windows user. Blank inputs keep saved credentials; explicit removal clears them. Replacing keys preserves usage totals. Local counters are not a live account balance. Real provider access must be tested with your own credentials; synthetic regression tests do not verify your account permissions.

## Gemini 主力池 / Primary pool

設定 → AI 助理 → 設定 Gemini 五組主力輪換。第 1 組在原本 AI 助理頁面填入；新視窗填入第 2–5 組，空白保留，勾選移除才清除。每組依序持續使用，受限才接下一組；不是逐輪平均分配。恢復後下一輪重新選擇可用的最優先組。儲存不呼叫 API，日用量統計合計所有已設定組。

每輪保留同一份上下文與人格。理解與搜尋整理優先使用同一組 Gemini；故障接手也收到相同上下文。圖片在本機 OCR 讀字；已設定 AI 時，只貼圖也將辨識文字與前文交給當次文字模型理解和回應，不傳原始圖片。未設定 AI 或只搜尋模式無附加文字時才只擷取文字。

Google 配額按專案，而非金鑰計算。多帳號／專案不是保證五倍額度；使用須遵守 [Google API 限制條款](https://developers.google.com/terms/)。本程式不批量建立帳號、不隱藏請求來源、不在冷卻期間反覆重試被拒絕的金鑰，按官方冷卻／恢復時間處理。

In Settings → AI Assistant, slot 1 stays on the existing primary credential; the Gemini pool editor adds slots 2–5. An available account remains selected across turns until it becomes unavailable. Accounts are tried in priority order, never concurrently. Context and active persona carry across accounts. Daily usage displays combined local generation counts. Images are read locally by Windows OCR; questions use recognized text only. Empty credential fields keep saved values; explicit removal clears a slot. Saving makes no API call. Google's per-project quotas and API terms still apply.

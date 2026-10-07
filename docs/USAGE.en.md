# Usage

Click the visible performance capsule to pin it temporarily, including during its intro animation. Readings continue updating and automatic dismissal pauses. Click again to hide. Temporary pinning does not change the saved always-visible setting.

Gemini, Groq and Cloudflare share direct, conversational reply guidance. Ordinary reply writers receive the understood draft as data; a stock closing invitation or unwanted question may trigger one rewrite. Clarifications, grounded memories and reminders remain separate. Voice quality still varies by model.

| Action | Result |
| --- | --- |
| Alt+A | Open AI and focus the composer |
| Enter / Shift+Enter | Send / insert a newline |
| Esc or assistant header | Collapse upward while keeping the conversation |
| Assistant context menu → New conversation | Clear the active conversation context |
| Ctrl+V | Preview a pasted image; analysis begins only after sending |
| Alt+M | Open the music island |
| Music context menu | Choose playlist or browser-follow mode |

Playlist mode controls the dedicated player; browser-follow mode uses Windows media sessions. Switching to browser-follow closes the dedicated player. Playlist shuffle persists across tracks. Browser controls depend on the session's capabilities.

Music, notifications and the performance overview use the original slim capsule and circular badge. The ring shows GPU utilization, track progress or notification time remaining. The notification reading timer starts after the intro; pinned text can be scrolled.

Notifications take priority and return to the previous view afterward, keeping the AI draft. Hover holds a preview; click to pin/hide.

AI interprets natural reminder requests, without requiring command phrases. Ambiguous requests ask for clarification. The app validates and saves a future Taiwan-time reminder before acknowledging it. Delivery runs locally while the app is running; it does not wake a sleeping computer. At 300 reminders, the oldest created item is removed.

One AI understanding step independently decides chat, search, memories and local reminder actions. Trigger phrases, first-person patterns and keyword routing have been removed. A turn may search and create several memories. AI creates concise Chinese categories and reuses suitable existing ones; edit any category or entry in Memory Palace. AI decides whether jokes, third-party information, temporary feelings or opt-out statements should be ignored. Local checks validate provenance, shape and storage, rather than interpreting intent. Review decisions because AI can make mistakes.

New replies reveal progressively after arriving, with longer responses displayed faster. Hiding or notification preemption pauses presentation; returning resumes from the same position. Reading older messages does not force scrolling to the bottom, and existing history never replays. Complete replies are encrypted before presentation; the display effect adds no model requests.

Replies are conversational, without report templates, citation IDs or source lists. Research happens in the background. Temporary feelings or one-off chat requests are not treated as lasting preferences. Successful memory saves receive a short confirmation; manage categories and facts in Memory Palace.

Settings → AI Assistant → AI model fallback configures Groq GPT-OSS 120B and Cloudflare Qwen3.8-27B. Text requests follow Gemini → Groq → Cloudflare, including understanding, search synthesis and memory decisions. Windows OCR reads image text locally; raw images are not uploaded. There are no local request or cumulative token caps. Gemini daily quotas reset at midnight Pacific time (15:00 Taiwan during Pacific daylight time, 16:00 otherwise); Cloudflare resets at 08:00 Taiwan. Groq recovery follows API retry instructions and reset headers. Expired cooldowns are retried on the next conversation, without background polling. See [setup instructions](AI-FALLBACK.md).

Memory proposals come from the current user's words before searching. Search summaries cannot introduce memories or actions from articles or model replies. Explicit corrections update a selected entry, and duplicates are not saved again. Search-only mode does not call AI or create personal records.

## AI and search

Enter your own Gemini key in Settings. Saving takes effect immediately. Auto mode understands the question once and searches only when needed. Search+AI normally plans once and synthesizes once; search-only mode does not call Gemini.

Gemini and Groq ordinary chat use two generations: understanding with new-memory decisions, then a dedicated reply with the same context and persona. Cloudflare ordinary chat combines these in one generation. Existing memories and persona load locally; they do not require a third model call. A necessary clarification uses one generation, and an unnecessary closing question may trigger at most one rewrite. Search still plans and synthesizes separately. The Gemini-understanding/Groq-writing experiment did not pass the manual quality review, so there is no split-work option in this release. Cloudflare latency remains variable; fewer requests do not guarantee a fixed speedup.

Paste a screenshot with Ctrl+V, then Send. With AI configured, even a captionless paste sends recognized text and conversation context to the normal assistant to decide whether to respond, search or ask a necessary clarification. No trigger phrase is required. Without an AI key, or for captionless search-only mode, local text extraction remains available. Raw images stay on this PC. OCR does not identify objects, characters or photographs. It uses Windows' installed OCR languages, preferring Traditional Chinese. If a language is missing, install its text recognition component in Windows language settings. OCR text is saved in encrypted conversation history, but cannot create personal memories or reminders by itself.

Configure search keys and order through Search APIs and rotation. Exa and Tavily have no local quota caps. Any search API failure disables the affected provider until the 2nd of the next month at 00:05 Taiwan time. The next available provider takes over (Exa → Tavily → Firecrawl). The next search after that time retries the provider, and another failure postpones it until the following month's 2nd. User cancellation does not count as failure. Settings show the reason and retry time. This is a local retry policy, not a promise of provider credit renewal. Tavily retains an informational official balance display. Searches do not check or block on that balance. Firecrawl has no local credit cap. It checks official credits before searching, caches balances for one minute, and reconciles search costs. Set its monthly renewal day and time in Taiwan time. Insufficient credits pause it until that date; the next search verifies credits before resuming. If still empty, retry after an hour. Shorter months use their last day. Official refresh calls the provider; refreshing Tavily usage neither unlocks nor extends its search failure stop.

AI and search run inside the app. Daily's keys and data are independent. See [Installation](INSTALL.en.md) and [Privacy](PRIVACY.md).

Enable “Rotate provider after each search turn” in search settings to cycle through your configured order after each successful search. Unavailable providers are skipped; a failed provider falls over during the same turn, and the following turn starts after the provider that succeeded. Ordinary chat, cache hits, cancellations and fully failed searches do not advance rotation. Progress survives restart and ordinary saves. Changing order or mode restarts the cycle. Turning it off restores priority order. Rotation spreads requests across providers; it does not reduce total search usage.

## AI personas / AI 人格

設定 → AI 助理 → 管理 AI 人格。左邊選擇已存人格，右邊分開輸入名稱、角色人格與互動規則。新增莊方宜範例後可自由編輯；「儲存人格」只保存，「使用此人格」才切換。使用中的人格再次儲存會在下一則訊息更新。Ctrl+S 儲存。切換列表保留草稿，關閉時可繼續編輯或明確捨棄。刪除需再按一次確認；刪除正在使用的人格回到預設助理。

Settings → AI Assistant → Manage AI personas. Select saved profiles on the left; edit name, character and interaction rules separately on the right. The Zhuang Fangyi-inspired example is editable. Save stores the profile; Use this persona activates it on the next message. Saving edits to the active persona updates its next reply. Ctrl+S saves. Selecting other profiles keeps drafts; closing offers keep editing or discard. Delete requires a second click, and deleting the active profile restores the default assistant.

人格只影響語氣與互動，不清空對話，不建立使用者記憶，不改動提醒。初次開啟使用預設助理；保存與切換不呼叫 AI。人格及互動規則每區最多 6,000 字、合計 10,000 字，最多 30 組，可隨時刪除。

Personas shape voice and interaction without clearing history, adding personal memories or changing reminders. Default assistant remains active initially. Saving and switching make no AI call. Each text field supports 6,000 characters, 10,000 combined; up to 30 custom profiles can be saved.

## Gemini 五組主力 / Five Gemini primaries

同一頁點「設定 Gemini 五組主力輪換」。第 1 組沿用原本主金鑰，第 2–5 組在新視窗貼入並確認免費方案。空白保留；勾移除才清除。依序持續使用第一個可用帳號，受限才接下一組，五組皆不可用才接 Groq → Cloudflare。恢復後重新優先使用較前面的帳號；這不是每輪平均分配。所有組使用相同的選取後上下文與已啟用人格，圖片文字在本機 OCR 讀取。

Use Configure five-account Gemini rotation on the same page. Slot 1 keeps the existing primary key; paste slots 2–5 in the new window and confirm free plans. Blank keeps saved credentials; explicit removal clears a slot. Keep using the first available account until unavailable, then move to the next. Only when all five are unavailable do Groq → Cloudflare take over. Higher-priority accounts return after recovery; this is sequential priority, not per-turn round-robin. Context and active persona stay continuous, image text is extracted locally by Windows OCR.

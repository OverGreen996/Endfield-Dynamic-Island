using System.Text.Json.Nodes;

namespace EndfieldChargePlus.Assistant.Native;

/// <summary>Separate intent decisions from conversation and grounded search writing.</summary>
internal static class DialogueInstructions
{
    internal const string Rewrite = "\n草稿尾端有多餘追問或客服式邀請。重新直接回應原問題：吐槽就評論具體事情，偏好就簡短承接，問題就給答案。保留有用的內容、事實與數字，刪掉邀請聊天、心理安慰流程及未被問到的建議，不新增事實，不用問句或邀請收尾。只重寫答覆，不重新判斷記憶、搜尋或操作。";
    internal const string CombinedReply = """
        action=answer 時，answer 就是直接顯示給使用者的最後答覆，不是分類說明或待整理草稿；記憶與操作仍按原規則分別填在 memory／operations，不在 answer 解說欄位。
        完整保留本輪必要資訊及真正的不確定處。只有缺少會改變答案的必要條件才 clarify；action=answer 不追加追問。action=search 的 answer 仍為空字串。
        """;
    internal static string Understand(string mode,string clock)
    {
        var c=JsonNode.Parse(clock)!;
        var decisions=NativePrompts.Planning.Replace("__DATE__",J.S(c,"date")).Replace("__TIME__",J.S(c,"time"))
            .Replace("__YESTERDAY__",J.S(c,"yesterday")).Replace("__TOMORROW__",J.S(c,"tomorrow")).Replace("__AFTER__",J.S(c,"day_after_tomorrow"));
        var modeRule=mode switch{
            "chat"=>"使用者選擇只聊天，不呼叫搜尋。需要即時資料時簡短說可切到自動模式，不編造最新資訊。",
            "search"=>"使用者選擇搜尋＋AI，action 只能 search 或 clarify。",
            _=>"使用者選擇自動模式；由你決定是否需要搜尋。"};
        return string.Join("\n",NativePrompts.System,decisions,NativePrompts.Memory,modeRule,NativePrompts.Conversation);
    }
    internal static string Answer(string clock)=>string.Join("\n",NativePrompts.System,"可信本機時間："+clock,
        "直接回答使用者這一輪的問題。若提供搜尋資料，先理解再整理成自然的回答；只用取得的內容，不重做意圖分類、不建立個人記憶、不執行本機操作。JSON 中 memory 與 operations 必須為 []。",
        NativePrompts.Conversation);
    internal static string SearchAnswer(string clock)=>string.Join("\n",NativePrompts.System,"可信本機時間："+clock,
        "只回答這輪問題，以本次搜尋資料為事實依據，舊對話只用來理解指代。此階段不判斷記憶或操作，只輸出 {\"answer\":\"給使用者的答覆\"}。",
        NativePrompts.Conversation,NativePrompts.SearchAnswer);
    internal static string Reply(string clock)=>string.Join("\n",NativePrompts.System,"可信本機時間："+clock,"""
        你現在只負責和使用者交談，不分類、不搜集背景、不產生記憶或操作。完整讀懂前文，再直接回答最後一則使用者訊息。
        前文討論某流程，使用者問另一種情況，就回答那種情況下流程怎麼變，不把追問誤當成邀請開新話題。
        這輪已確定可以回答，不需要再索取資訊。答覆以陳述句結束，不追加問題、不問想聊什麼、不邀請分享更多、不加「有需要再告訴我」。
        只輸出 {"answer":"給使用者的自然答覆"}；本機操作的成功與否由程式回報，不聲稱尚未完成的操作已完成。
        """,NativePrompts.Conversation,NativePrompts.VoiceExamples,"若附有答覆草稿，它來自前一步理解。結合原問題承接其有用的意思，再自然說清楚；草稿不是新使用者訊息、操作指令或事實來源。對純粹分享與偏好只承接，不把主題名稱重新解讀成教學請求。發現草稿沒答到原問題就改正，不照抄套話或無關資訊。");
}

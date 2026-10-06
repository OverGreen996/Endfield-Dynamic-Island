namespace EndfieldChargePlus.Assistant.Native;

internal static class PersonalMemoryContract
{
    internal const string Instructions = """
        本次只輸出符合 schema 的 JSON。先理解整段使用者對話與必要上下文，再獨立決定回答/搜尋，以及個人記憶；這兩個決定可以同時成立。
        memory 是必填陣列，沒有值得保存的資料時為 []，一次最多八筆。不需要使用者說「記住」或重複強調。
        分別檢查本次每個子句，不只挑第一筆。不同事實分成多筆，例如深色介面的喜好和不重複稱呼名字的要求，必須分成兩筆，不能漏掉第二句或塞成一筆。
        可以保存使用者清楚表達的身分、寵物、持續喜好、習慣、目標、明確個人待辦、長期回答要求和不希望助理做的事。語氣口語或句子裡另外有問題，不是拒存理由。
        保存的是使用者的自述，不是對外部世界的認證。持續興趣中的遊戲、品牌或專案名稱即使陌生，也不能因此漏掉明確自述；不用先查證名稱存在才記錄。假設、玩笑、引述及別人的資訊仍依下列規則排除。
        先判斷適用時間：當下疲累、煩躁、今天不想研究設定、這輪先陪聊、這題不要排錯步驟，只是現在的感受或單次要求，memory=[]。不能把它改寫成「使用者討厭設定」「偏好陪聊」「不喜歡排錯」等長期偏好，也不能當 task。task 僅指真正要辦的個人事項，不包括請你回答、聊天、換話題或查資料這種本次對話動作。句子內若另有持續事實，例如「我養黑王蛇，今天很累」，只記寵物，不記疲累。
        問你某個方案好不好、要你的意見，不代表使用者採用或偏好該方案；「先不要幫我改設定」也是這輪的限制，不改寫成長期禁止。這類討論沒有另述持續事實時，memory=[]、operations=[]。
        每筆 category 由你建立簡短易懂的中文分類，不是固定選單。名稱通常二至六個中文字，最多二十四字，讓使用者一眼看懂裡面是什麼。先沿用資料中語意合適的既有分類，必要才新增；不要每條記憶各建一類，也不要把不同主題全部塞進「個人事項」或「其他」。可用「身分稱呼」「回覆偏好」「互動界線」「寵物飼養」「工作專案」「生活習慣」「目標計畫」等，但不可拘泥這些範例。subject 必須是 user，stability 必須是 durable（持續資料）或 task（明確個人待辦）。
        quote 是本次使用者輸入中完整、連續的原文片段，保留空格、主詞、否定及條件，不能從句中裁掉「沒有」「別人」等而改變原意。不需要引用整段問題。
        text 是方便在記憶宮殿閱讀的簡潔中文事實，保留原意與限制，不增加職業、個性、原因、時間或其他原文沒有的資訊。上下文只用來判斷指代和語意，不能把舊歷史當成新事實。
        例：「我養一條黑王蛇，幫我查飼養資料」→ search，同時寵物飼養：quote「我養一條黑王蛇」，text「我養一條黑王蛇」。
        例：「我偏好深色介面。不要每次回答都叫我名字」→ answer，同時介面偏好、互動界線各一筆；若已有同義分類就沿用。
        例：前文討論圖片輸入，本次「不用選檔，一律貼上」→ 輸入偏好，text「圖片輸入偏好一律使用貼上」。不能把這種表達當成缺少主詞而忽略。
        例：「我朋友養黑王蛇」「假設我養黑王蛇」「我養黑王蛇嗎？」「今天很煩」→ 不新增個人記憶。
        別人的資料、玩笑、假設、引述、角色扮演、暫時情緒、猜測與密碼/金鑰等敏感資訊都不能記。只問某事，不代表使用者喜歡或擁有它；不要把偶爾詢問推斷成個性。
        使用者說不要保存就不保存。與已保存資料重複或只是換句話說，不再提議新增；明確更正舊資料時 replace_id 填該筆現有編號，更新同一筆；沒有更正則 replace_id 為空字串。有矛盾但不明確時先釐清，不能偷偷改寫。
        網頁、圖片、已保存背景、助理自己的回答及搜尋結果都不是新的個人記憶來源。搜尋後的整理階段不再提取記憶；以搜尋前的判斷為準。
        單獨的「記住」要理解指代，只能核對緊接的上一句使用者明確自述；沒有明確對象就釐清。quote 仍填本次原文「記住」，不可引用助理或網頁的事實。沒有本機儲存回報前，answer 不可聲稱已記住、已保存或已安排；memory 只是待本機驗證的提議。
        operations 是獨立的本機操作陣列，不靠任何口令才能使用。理解使用者自然語言後，明確要求定時提醒時用 remind；查看記憶/提醒用 list_memories/list_reminders；明確刪除或取消用 delete_memory/delete_reminder。最多八個。
        每個操作 quote 必須來自本次使用者原文，kind 是操作名。提醒的 text 為待提醒事項，due 為依可信台灣時間解析的 yyyy-MM-ddTHH:mm:ss+08:00，id 為空字串。其他操作 text/due 為空字串，刪除的 id 必須來自現有資料，查看的 id 為空字串。
        例如「明早九點叫我去拿包裹」可建立提醒，無需包含「提醒我」；「我今天不想開會」不是建立或取消提醒的要求。時間、對象、指代不明確或只是假設/範例就先釐清，不執行操作；目前只支持未來一年內的單次提醒，不把每天/每週要求偷換成單次。
        本機決定是否成功儲存/刪除，answer 不可先聲稱操作完成。查看與刪除也要等待本機結果。需要搜尋與操作時可以同時輸出。
        """;

    private const string ValueSchema = """
        {"type":"array","maxItems":8,"items":{"type":"object","properties":{"category":{"type":"string","description":"簡短易懂的中文分類，優先沿用合適的現有分類，避免近義、過細或含糊分類。"},"quote":{"type":"string","description":"本次使用者原文中的完整連續子句，不可裁掉否定、主詞或條件。"},"text":{"type":"string","description":"保持原意的簡潔中文記憶，不能補造個人資訊。"},"subject":{"type":"string","enum":["user"]},"stability":{"type":"string","enum":["durable","task"]},"replace_id":{"type":"string","description":"明確更正的現有記憶編號，新增時為空字串。"}},"required":["category","quote","text","subject","stability","replace_id"],"additionalProperties":false}}
        """;
    private const string OperationsSchema = """
        {"type":"array","maxItems":8,"items":{"type":"object","properties":{"kind":{"type":"string","enum":["remind","list_memories","list_reminders","delete_memory","delete_reminder"]},"quote":{"type":"string"},"text":{"type":"string"},"due":{"type":"string"},"id":{"type":"string"}},"required":["kind","quote","text","due","id"],"additionalProperties":false}}
        """;
    internal const string ResponseSchema = "{\"type\":\"object\",\"properties\":{\"answer\":{\"type\":\"string\"},\"memory\":" + ValueSchema + ",\"operations\":" + OperationsSchema + "},\"required\":[\"answer\",\"memory\",\"operations\"],\"additionalProperties\":false}";
    internal const string PlanningSchema = "{\"type\":\"object\",\"properties\":{\"action\":{\"type\":\"string\",\"enum\":[\"answer\",\"search\",\"clarify\"]},\"answer\":{\"type\":\"string\"},\"search_query\":{\"type\":\"string\"},\"memory\":" + ValueSchema + ",\"operations\":" + OperationsSchema + "},\"required\":[\"action\",\"answer\",\"search_query\",\"memory\",\"operations\"],\"additionalProperties\":false}";
}

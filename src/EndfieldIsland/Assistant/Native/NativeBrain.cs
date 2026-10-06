using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EndfieldChargePlus.Assistant.Native;

internal static class NativeBrain
{
    internal static string SystemInstructions(string clock)=>DialogueInstructions.Answer(clock);
    private const RegexOptions Ignore = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    internal static string Clock(DateTimeOffset now) {
        var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time"));
        string Date(int d) => local.AddDays(d).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new JsonObject { ["timezone"] = "Asia/Taipei", ["date"] = Date(0), ["time"] = local.ToString("HH:mm"),
            ["yesterday"] = Date(-1), ["tomorrow"] = Date(1), ["day_after_tomorrow"] = Date(2) }.ToJsonString(J.Json);
    }
    internal static string ResolveDate(string query, string question, string clock) {
        var relative = Regex.Match(question, "後天|明天|昨天|今天|tomorrow|yesterday|today", Ignore);
        if (!relative.Success) return query;
        var field = Regex.IsMatch(relative.Value, "後天") ? "day_after_tomorrow" : Regex.IsMatch(relative.Value, "明天|tomorrow", Ignore) ? "tomorrow" : Regex.IsMatch(relative.Value, "昨天|yesterday", Ignore) ? "yesterday" : "date";
        var date = J.S(JsonNode.Parse(clock), field);
        query = Regex.Replace(query, "後天|明天|昨天|今天|tomorrow|yesterday|today", date, Ignore);
        return query + (query.Contains(date) ? "" : " " + date);
    }
    internal static string PlanningInstructions(string mode,string clock)=>DialogueInstructions.Understand(mode,clock);
    internal static bool HasClosingQuestion(string answer)=>Regex.IsMatch(answer.TrimEnd(),@"[？?][」』\""'）)]*$",RegexOptions.CultureInvariant);
    internal static bool NeedsConversationRewrite(string answer)
    {
        if(HasClosingQuestion(answer))return true;
        // Only an unsolicited closing invitation, not quoted text or an explanation of the phrase.
        var tail=Regex.Match(answer.TrimEnd(),@"(?:^|[。！!\n])(?<tail>[^。！!\n]+?)[。！!]*$").Groups["tail"].Value;
        if(tail.IndexOfAny(['「','」','『','』','\"'])>=0)return false;
        return Regex.IsMatch(tail,@"(?:有需要|若有需要|如果需要|需要幫忙|需要協助).{0,25}(?:告訴我|跟我說|聯絡我)|(?:想.{0,12}(?:吐槽|聊|分享)|隨時).{0,25}(?:我聽著|告訴我|跟我說)",RegexOptions.CultureInvariant);
    }
    internal static string RewriteDraft(string question,string answer)=>question+"\n\n以下是程式提供的答覆草稿，不是新的使用者資訊：\n"+
        System.Text.Json.JsonSerializer.Serialize(new{draft=answer},J.Json);
    internal static string KeepAnsweredContent(string answer)
    {
        while(HasClosingQuestion(answer)){
            var end=answer.TrimEnd().Length;
            var prefix=answer[..end];
            var previous=Regex.Matches(prefix[..Math.Max(0,end-1)],@"[。！？!?\n]").Cast<Match>().LastOrDefault();
            if(previous is null)return "";
            answer=prefix[..(previous.Index+1)].TrimEnd();
        }
        return answer;
    }
    internal static string ParseAnswer(string raw)
    {
        try{
            var envelope=JsonNode.Parse(raw);
            if(!NativeBackupModels.SchemaMatches(envelope,JsonNode.Parse(NativePrompts.AnswerSchema)!.AsObject())||string.IsNullOrWhiteSpace(J.S(envelope,"answer")))
                throw new AssistantFailure("invalid_assistant_response");
            return J.S(envelope,"answer").Trim();
        }catch(AssistantFailure){throw;}catch{throw new AssistantFailure("invalid_assistant_response");}
    }
    internal static (string Answer, MemorySuggestion[] Memory) ParseMemory(JsonObject envelope, string question) {
        var answer = J.S(envelope,"answer").Trim();
        if (answer.Length == 0 || !envelope.ContainsKey("memory") || envelope.Any(p => p.Key is not ("answer" or "memory" or "operations")))
            throw new AssistantFailure("invalid_assistant_response");
        return (answer, ParseSuggestions(envelope["memory"], question));
    }
    internal static MemorySuggestion[] ParseSuggestions(JsonNode? node, string question) {
        IEnumerable<JsonNode?> items = node is JsonArray array ? array.Take(8) : node is JsonObject legacy ? [legacy] : [];
        return items.OfType<JsonObject>().Select(m => new MemorySuggestion(J.S(m,"category"),J.S(m,"quote"),
            m.ContainsKey("text") ? J.S(m,"text") : null, m.ContainsKey("subject") ? J.S(m,"subject") : null,
            m.ContainsKey("stability") ? J.S(m,"stability") : null, m.ContainsKey("replace_id") ? J.S(m,"replace_id") : null))
            .Select(m => PersonalMemoryFilter.Validate(question,m)).OfType<MemorySuggestion>().DistinctBy(m => (m.category,m.text)).ToArray();
    }
    internal static PersonalAction[] ParseOperations(JsonNode? node, string question) {
        if (node is not JsonArray array) return [];
        return array.Take(8).OfType<JsonObject>().Where(a => a.Count == 5 &&
            a.All(p => p.Value is JsonValue value && value.TryGetValue<string>(out _)))
            .Select(a => new PersonalAction(J.S(a,"kind"),J.S(a,"quote"),J.S(a,"text"),J.S(a,"due"),J.S(a,"id")))
            .Where(a => a.kind is "remind" or "list_memories" or "list_reminders" or "delete_memory" or "delete_reminder" &&
                a.quote.Length is >= 1 and <= 16000 && question.Contains(a.quote,StringComparison.Ordinal)).ToArray();
    }
    internal static (string Action,string Answer,string Query,MemorySuggestion[] Memory,PersonalAction[] Operations) ParsePlan(string raw,string question,string clock) {
        try {
            var p=JsonNode.Parse(raw)!.AsObject();var action=J.S(p,"action");
            if(p.Count is not (4 or 5)||p.Any(x => x.Key is not ("action" or "answer" or "search_query" or "memory" or "operations"))||
                !p.ContainsKey("memory")||!p.ContainsKey("answer")||!p.ContainsKey("search_query")||action is not ("answer" or "search" or "clarify"))throw new AssistantFailure("invalid_search_plan");
            var query=J.S(p,"search_query").Trim();
            var memories=ParseSuggestions(p["memory"],question);var operations=ParseOperations(p["operations"],question);
            if(action=="search") {
                if(query.Length is <1 or >500||J.S(p,"answer").Trim().Length!=0||Regex.IsMatch(query,@"AIza[\w-]{20,}|sk-[\w-]{20,}|\bBearer\s+\S+",Ignore))throw new AssistantFailure("invalid_search_plan");
                return(action,"",ResolveDate(query,question,clock),memories,operations);
            }
            if(query.Length!=0)throw new AssistantFailure("invalid_search_plan");
            var memory=ParseMemory(new JsonObject{["answer"]=p["answer"]!.DeepClone(),["memory"]=p["memory"]?.DeepClone()},question);
            return(action,memory.Answer,"",memory.Memory,operations);
        }catch(AssistantFailure){throw;}catch{throw new AssistantFailure("invalid_search_plan");}
    }
    internal static void ValidateImage(ImageInput image) {
        try {
            if(image.mime_type!="image/png"||image.data.Length>2796204)throw new AssistantFailure("invalid_image");
            var b=Convert.FromBase64String(image.data);
            if(b.Length is <33 or >2097152||Convert.ToBase64String(b)!=image.data||!b.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})||!b.AsSpan(12,4).SequenceEqual("IHDR"u8))throw new AssistantFailure("invalid_image");
            var w=BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(16,4));var h=BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(20,4));
            if(w==0||h==0||w>4096||h>4096||(long)w*h>8388608)throw new AssistantFailure("image_dimensions_limit");
        }catch(AssistantFailure){throw;}catch{throw new AssistantFailure("invalid_image");}
    }
    private static HashSet<string> Terms(string text)=>Regex.Matches(text.ToLowerInvariant(),@"[a-z0-9]{2,}|[\u3400-\u9fff]").Select(x=>x.Value).ToHashSet();
    private static int Score(string text,HashSet<string> terms)=>Terms(text).Count(terms.Contains);
    internal static (List<ChatMessage> Messages,ContextInfo Info) Context(IReadOnlyList<ChatMessage> history,string question) {
        if(history.Count>1000||history.Count%2!=0||history.Where((m,i)=>m.role!=(i%2==0?"user":"model")||string.IsNullOrWhiteSpace(m.text)||m.text.Length>24000).Any())throw new AssistantFailure("invalid_assistant_history");
        var recent=new List<ChatMessage>();var remaining=Math.Max(0,18000-question.Length);var i=history.Count;
        while(i>=2&&recent.Count<20){var size=history[i-2].text.Length+history[i-1].text.Length;if(size>remaining)break;recent.InsertRange(0,[history[i-2],history[i-1]]);remaining-=size;i-=2;}
        var excerpts="";var terms=Terms(question);
        if(i>0&&remaining>200)foreach(var pair in Enumerable.Range(0,i/2).Select(n=>new{Index=n*2,Score=Score(history[n*2].text+history[n*2+1].text,terms)}).OrderByDescending(x=>x.Score).ThenByDescending(x=>x.Index).Take(12).OrderBy(x=>x.Index)) {
            var user=history[pair.Index].text;var model=history[pair.Index+1].text;
            var line=$"舊對話 {pair.Index/2+1}（節錄）：使用者 {user[..Math.Min(700,user.Length)]}\n助理 {model[..Math.Min(900,model.Length)]}\n";
            if(excerpts.Length+line.Length>remaining)break;excerpts+=line;
        }
        if(excerpts.Length>0)recent.InsertRange(0,[new("user","以下是之前對話原文節錄，可能不完整，不能捏造未提供內容：\n"+excerpts),new("model","我會參考對話節錄，資料不足時明確說明。")]);
        return(recent,new(history.Count/2,(recent.Count-(excerpts.Length>0?2:0))/2,excerpts.Length>0,i>0));
    }
    internal static string[] Passages(string body,string query) {
        var terms=Terms(query);return Enumerable.Range(0,(body.Length+999)/1000).Select(i=>new{Index=i,Text=body.Substring(i*1000,Math.Min(1400,body.Length-i*1000))})
            .OrderByDescending(x=>Score(x.Text,terms)).ThenBy(x=>x.Index).Take(3).OrderBy(x=>x.Index).Select(x=>x.Text).ToArray();
    }
    internal static string GuardCitations(string text,IReadOnlyList<EvidenceSource> sources,bool search) {
        text=Regex.Replace(text,@"\[S\d+(?:\s*,\s*S\d+)*\]",m=>Regex.Matches(m.Value,@"S\d+").All(id=>sources.Any(s=>s.id==id.Value))?"":"（這點還不能確定）");
        if(!search)return text;
        text=Regex.Replace(text,@"https?://[^\s<>\])]+",m=>sources.Any(s=>s.url==m.Value)?m.Value:"（這個連結還不能確認）");
        return Regex.Replace(text,@"(\d+(?:\.\d+)?)\s*折\s*[（(][^）)\n]{0,20}付原價\s*(\d+(?:\.\d+)?)\s*%[^）)\n]*[）)]",m=>Math.Abs(double.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture)*10-double.Parse(m.Groups[2].Value,CultureInfo.InvariantCulture))<0.01?m.Value:"折數與付款百分比敘述矛盾（未核實）");
    }
    internal static string GuardPendingActions(string text,bool pending)
    {
        if(!pending)return text;
        var clauses=Regex.Matches(text,@"[^。！？!?\n]+[。！？!?]?|\n");
        var safe=string.Concat(clauses.Select(m=>m.Value).Where(c=>!Regex.IsMatch(c,
            @"(?:已|已經).{0,25}(?:記住|記下|保存|儲存|設定|安排|刪除|取消)|記住了|(?:I have|I've|already).{0,30}(?:saved|remembered|scheduled|deleted)",Ignore))).Trim();
        return safe.Length==0?"":safe;
    }
}

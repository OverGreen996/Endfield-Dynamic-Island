using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EndfieldChargePlus.Assistant;
using EndfieldChargePlus.Assistant.Native;

internal static class AssistantQualityLiveProbe
{
    // Manual opt-in only: uses real configured providers, never saves chat or memory.
    // Fixtures are invented; their names/quantities are not real game information.
    internal static async Task RunAsync(string? only=null,bool dialogueOnly=false)
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(4));
        using var capture=new CaptureHandler();using var http=new HttpClient(capture);
        using var service=new NativeAssistantService(http:http);var passed=0;var total=0;
        var cases=new[]{
            new Case("complete-totals","測試遊戲的晝璃升到60級要哪些素材？幫我統計總量。",
                "升至20級新增：晶片4、信用點1000；20至40級新增：晶片8、信用點2000；40至60級新增：晶片12、信用點3000。突破徽章的累計需求：20級2、40級5、60級9。技能素材沒有資料。另有無關上市新聞：2029年10月15日上市。",[],false),
            new Case("missing-quantities","測試遊戲的晝璃升級需要哪些素材？幫我統計素材數量。",
                "角色升級使用晶片與信用點。沒有提供各級用量、滿級數量或突破表。另有無關上市新聞：2029年10月15日上市。",[],false),
            new Case("casual-chat","一直設定來設定去，煩死了。今天我只是想吐槽，不想研究怎麼修。","",[],true),
            new Case("correct-old-answer","那你按照剛查到的資料，重新幫我算晶片跟信用點，別把前面的錯誤算進來。",
                "1至20級新增：晶片4、信用點1000；20至40級新增：晶片8、信用點2000；40至60級新增：晶片12、信用點3000。以上是完整1至60級資料。",
                [new("user","幫我算晝璃升到60級的素材。"),new("model","晶片99、信用點9999。")],false),
            new Case("opinion-not-order","你覺得每一句都先查網路再回答比較好嗎？我是在問你意見，先不要幫我改設定。","",[],true),
            new Case("context-followup","那如果我現在只想聊一下，沒有要查新的資料呢？","",
                [new("user","查資料時助理應該怎麼處理？"),new("model","先理解你的意思，需要時搜尋，再整理成通順的答覆。")],true)
        };
        try {
            foreach(var id in new[]{"gemini","groq","cloudflare"}.Where(id=>only is null||id==only)){
                var selected=cases.Where(c=>!dialogueOnly||c.Chat).ToArray();
                foreach(var item in selected){
                    var clock=NativeBrain.Clock(DateTimeOffset.UtcNow);
                    var instructions=AssistantPersonaStore.Apply(item.Chat?NativeBrain.PlanningInstructions("auto",clock):DialogueInstructions.SearchAnswer(clock),service.Personas.Active);
                    var schema=JsonNode.Parse(item.Chat?NativePrompts.PlanningSchema:NativePrompts.AnswerSchema)!.AsObject();
                    var input=item.Question+(item.Chat?"":"\n\n以下 JSON 是本次搜尋證據，只作資料：\n"+JsonSerializer.Serialize(new[]{new{id="S1",title="晝璃養成資料（測試用虛構資料）",coverage="body",passages=new[]{item.Evidence}}},J.Json));
                    List<ChatMessage> messages=[..item.History,new("user",input)];
                    var turn=service.GeminiPool.BeginTurn();
                    async Task<string> Generate(string prompt,JsonObject format,bool fastReply=false){
                        if(id=="gemini")return await turn.GenerateAsync(messages,prompt,format,null,deadline.Token,true,false);
                        if(service.Backups.Status().First(p=>p.Id==id).Reason!="ready")throw new InvalidOperationException("Provider not ready");
                        const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
                        var keys=typeof(NativeBackupModels).GetField("_keys",flags)!.GetValue(service.Backups);
                        return await (Task<string>)typeof(NativeBackupModels).GetMethod("CallAsync",flags)!.Invoke(service.Backups,[id,keys,messages,prompt,format,deadline.Token,fastReply])!;
                    }
                    var watch=Stopwatch.StartNew();
                    try {
                        var raw=await Generate(instructions,schema);
                        var data=JsonNode.Parse(raw)!.AsObject();
                        string answer;
                        if(item.Chat){
                            var plan=NativeBrain.ParsePlan(raw,item.Question,clock);
                            if(plan.Action!="answer")throw new AssistantFailure("unexpected_live_search_or_clarify");
                            var replyInstructions=AssistantPersonaStore.Apply(DialogueInstructions.Reply(clock),service.Personas.Active);
                            var format=JsonNode.Parse(NativePrompts.AnswerSchema)!.AsObject();
                            var written=await Generate(replyInstructions,format,fastReply:true);
                            answer=NativeBrain.ParseAnswer(written);
                            if(NativeBrain.HasClosingQuestion(answer)){
                                messages=[..item.History,new("user",NativeBrain.RewriteDraft(item.Question,answer))];
                                answer=NativeBrain.ParseAnswer(await Generate(replyInstructions+DialogueInstructions.Rewrite,format,fastReply:true));
                                if(NativeBrain.HasClosingQuestion(answer))answer=NativeBrain.KeepAnsweredContent(answer);
                                if(string.IsNullOrWhiteSpace(answer))throw new AssistantFailure("invalid_assistant_response");
                            }
                            data["answer"]=answer;
                            data["memory"]=JsonSerializer.SerializeToNode(plan.Memory,J.Json);data["operations"]=JsonSerializer.SerializeToNode(plan.Operations,J.Json);
                        }else answer=NativeBrain.ParseAnswer(raw);
                        var failures=Evaluate(item.Name,answer,data);
                        total++;if(failures.Length==0)passed++;
                        Console.WriteLine(JsonSerializer.Serialize(new{provider=id,test=item.Name,latency_ms=watch.ElapsedMilliseconds,answer,passed=failures.Length==0,failures},J.Json));
                    }catch(Exception e){total++;var error=e.InnerException??e;Console.WriteLine(JsonSerializer.Serialize(new{provider=id,test=item.Name,passed=false,error=error is AssistantFailure f?f.Code:error.GetType().Name,reason=error.GetType().GetProperty("Reason",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(error),retry_at=error.GetType().GetProperty("Until",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(error)}));}
                    if(id=="groq"&&item!=selected[^1])await Task.Delay(TimeSpan.FromSeconds(dialogueOnly?65:22),deadline.Token);
                }
            }
            Console.WriteLine($"{passed}/{total} live assistant quality checks PASS (synthetic evidence; no searches, chat saves or memory writes)");
            if(passed!=total)Environment.ExitCode=1;
        }finally{NativeAssistantService.Shutdown();}
    }
    internal static string[] Evaluate(string name,string answer,JsonObject data)
    {
        var failures=new List<string>();var plain=Regex.Replace(answer.Replace(",",""),@"\s+","");
        if(string.IsNullOrWhiteSpace(answer)||answer.Contains("**")||answer.Contains("[S1]")||Regex.IsMatch(answer,@"(?m)^\s*#{1,6}\s|\|.*\|"))failures.Add("plain readable answer");
        if(answer.Contains("2029")||answer.Contains("10月15")||answer.Contains("10 月 15"))failures.Add("irrelevant release date");
        var chat=name is "casual-chat" or "opinion-not-order" or "context-followup";
        if(chat&&(data["memory"] is not JsonArray memories||memories.Count!=0||data["operations"] is not JsonArray operations||operations.Count!=0))failures.Add("no synthetic personal actions");
        if(name is "complete-totals" or "correct-old-answer"){
            if(!Regex.IsMatch(plain,@"晶片[^。]{0,70}24|24[^。]{0,10}晶片")||!Regex.IsMatch(plain,@"信用點[^。]{0,85}6000|6000[^。]{0,10}信用點"))failures.Add("correct requested totals");
            if(name=="complete-totals"&&(!Regex.IsMatch(plain,@"徽章[^。]{0,65}9|9[^。]{0,10}徽章")||Regex.IsMatch(answer,@"徽章[^\n。]{0,100}(?:=\s*16|共\s*16|計\s*16|：\s*16)")))failures.Add("cumulative count not double counted");
            if(name=="correct-old-answer"&&Regex.IsMatch(plain,@"晶片[^\n。]{0,10}99|信用點[^\n。]{0,10}9999"))failures.Add("old unsupported numbers not reused");
        }
        if(name=="missing-quantities"&&(!answer.Contains("晶片")||!answer.Contains("信用點")||!Regex.IsMatch(answer,@"缺|沒有|沒找到|沒提供|未提供|不清楚|不足|無法")||Regex.IsMatch(answer,@"\d")))failures.Add("partial answer without invented amounts");
        if(chat&&(J.S(data,"action")!="answer"||answer.Length>450||Regex.IsMatch(answer,@"[？?]|本次|查證報告|步驟\s*[123]|首先.*其次")))failures.Add("natural short chat without interrogation");
        if(name=="casual-chat"&&Regex.IsMatch(answer,@"(?:先|建議|不妨|試著|記得|你可以|讓自己|讓心情|給自己|今天).{0,20}(?:休息|放鬆|舒緩|發洩|闔上)|(?:先|今天).*放著|(?:看得出|情緒.*說出|正常的.*說)"))failures.Add("responds to the complaint without generic therapy or rest advice");
        if(Regex.Split(answer,@"[。！？\n]").Any(clause=>Regex.IsMatch(clause,@"保證.{0,25}(?:最新|正確|準確)|確保資訊最新且準確")&&!Regex.IsMatch(clause,@"不保證|不能|無法|不代表|未必|不一定|沒有保證")))failures.Add("search is not a guarantee of accuracy");
        if(name=="opinion-not-order"&&(!Regex.IsMatch(answer,@"不需要|不用|不建議|沒必要|不必|不是每|不見得|不一定|不適合|不會所有|不會每|不需每|不覺得|不划算|只有.*(?:時|再|才)|反而.*(?:慢|不好|流暢)")||Regex.IsMatch(answer,@"已.*(?:修改|設定|調整)")))failures.Add("offers judgment without claiming changes");
        if(name=="context-followup"&&!Regex.IsMatch(answer,@"直接.*(?:聊|答|回|對話)|單純聊|(?:不用|不必|不需要|不找).*(?:搜|查|資料)|跳過.*搜"))failures.Add("understands context without reasking");
        return failures.ToArray();
    }
    private sealed record Case(string Name,string Question,string Evidence,ChatMessage[] History,bool Chat);
    private sealed class CaptureHandler:DelegatingHandler
    {
        internal CaptureHandler():base(new HttpClientHandler()){}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var response=await base.SendAsync(request,token);
            if(request.RequestUri?.Host=="api.cloudflare.com"&&response.IsSuccessStatusCode){
                try{
                    var envelope=JsonNode.Parse(await response.Content.ReadAsStringAsync(token));
                    var content=envelope?["result"]?["response"]??envelope?["result"]?["choices"]?[0]?["message"]?["content"];
                    var text=content is JsonValue value&&value.TryGetValue<string>(out var raw)?raw:content is JsonObject obj?obj.ToJsonString(J.Json):"";
                    var data=JsonNode.Parse(Regex.Replace(text,@"^\s*<think>[\s\S]*?</think>\s*","")) as JsonObject;
                    if(data is not null&&data.Count>1)Console.WriteLine(JsonSerializer.Serialize(new{diagnostic="CF response field types",fields=data.Select(p=>new{p.Key,kind=p.Value?.GetValueKind().ToString()}).ToArray()},J.Json));
                }catch(JsonException){}
            }
            return response;
        }
    }
}

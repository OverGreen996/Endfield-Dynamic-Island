using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using EndfieldChargePlus.Assistant;
using EndfieldChargePlus.Assistant.Native;

internal static class ProviderResponsibilitiesLiveProbe
{
    // Manual opt-in: one real provider, current persona, synthetic memory/evidence. No real search or user-data writes.
    internal static async Task RunAsync(string provider="groq",string? only=null,string? cfEffort=null,bool oneCall=false)
    {
        using var handler=new CfMeasurementHandler(cfEffort);using var http=new HttpClient(handler){Timeout=Timeout.InfiniteTimeSpan};using var service=new NativeAssistantService(http:http);
        using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(10));
        if(provider is not ("gemini" or "groq" or "cloudflare"))throw new ArgumentException("Unsupported provider");
        if(provider!="gemini"&&service.Backups.Status().Single(p=>p.Id==provider).Reason!="ready")throw new Exception(provider+" is not ready; no other provider will be used.");
        var persona=service.Personas.Active;
        const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
        var keys=typeof(NativeBackupModels).GetField("_keys",flags)!.GetValue(service.Backups);
        var call=typeof(NativeBackupModels).GetMethod("CallAsync",flags)!;
        var cases=new[]{
            new Case("casual-chat","一直設定來設定去，煩死了。今天我只是想吐槽，不想研究怎麼修。",[],"answer",""),
            new Case("context-followup","那如果我現在只想聊一下，沒有要查新的資料呢？",
                [new("user","查資料時助理應該怎麼處理？"),new("model","先理解你的意思，需要時搜尋，再整理成通順的答覆。")],"answer",""),
            new Case("personal-memory","我養一條黑王蛇。回覆別一直叫我的名字，平常直接講重點就好。",[],"answer",""),
            new Case("opinion-not-order","你覺得每一句都先查網路再回答比較好嗎？我是在問你意見，先不要幫我改設定。",[],"answer",""),
            new Case("complete-totals","我平常在玩《昇原》。幫我算晝璃升到60級需要的素材總量。",[],"search",
                "升至20級新增晶片4、信用點1000；20至40級新增晶片8、信用點2000；40至60級新增晶片12、信用點3000。突破徽章累計需求為20級2、40級5、60級9。技能素材沒有資料。這些是虛構測試資料。"),
            new Case("missing-quantities","幫我查《昇原》的晝璃升級需要哪些素材，統計素材數量。",[],"search",
                "角色升級使用晶片與信用點。沒有提供各級用量、滿級數量或突破表。這些是虛構測試資料。"),
            new Case("transfer-vent","排了半小時，輪到我窗口剛好休息，真的很衰。只是抱怨，不用替我想辦法。",[],"answer",""),
            new Case("transfer-advice","我每次開設定都要從頭找同一個選項，你覺得介面怎麼改比較合理？先講方案，不要動程式。",[],"answer","")
        }.Where(c=>only=="transfer"?c.Name.StartsWith("transfer-",StringComparison.Ordinal):only is null?!c.Name.StartsWith("transfer-",StringComparison.Ordinal):c.Name==only||only=="chat"&&c.Action=="answer").ToArray();
        if(cases.Length==0)throw new ArgumentException("Unknown test case");
        var root=Path.Combine(Path.GetTempPath(),"island-groq-responsibilities-"+Guid.NewGuid().ToString("N"));
        var personal=new PersonalAssistantStore(Path.Combine(root,"synthetic-personal.dpapi"));
        personal.AcceptModelSuggestion("我叫測試人物",new("身分稱呼","我叫測試人物","我叫測試人物","user","durable",""),DateTimeOffset.Now);
        personal.AcceptModelSuggestion("我偏好繁體中文",new("回覆偏好","我偏好繁體中文","我偏好繁體中文","user","durable",""),DateTimeOffset.Now);
        var results=new List<bool>();var elapsed=new List<long>();var generationCount=0;
        try{
            foreach(var item in cases){
                var clock=NativeBrain.Clock(DateTimeOffset.UtcNow);
                var selected=NativeBrain.Context(personal.WithMemory(item.History,item.Question),item.Question);
                List<ChatMessage> messages=[..selected.Messages,new("user",item.Question)];
                var turn=service.GeminiPool.BeginTurn();
                async Task<string> Generate(string instructions,JsonObject schema,bool fast=false){
                    generationCount++;
                    if(provider=="gemini")return await turn.GenerateAsync(messages,AssistantPersonaStore.Apply(instructions,persona),schema,null,deadline.Token,true,false);
                    return await (Task<string>)call.Invoke(service.Backups,[provider,keys,messages,AssistantPersonaStore.Apply(instructions,persona),schema,deadline.Token,fast])!;
                }
                var watch=Stopwatch.StartNew();var failures=new List<string>();var phase="understanding";JsonObject? understood=null;
                try{
                    var raw=await Generate(NativeBrain.PlanningInstructions("auto",clock),JsonNode.Parse(NativePrompts.PlanningSchema)!.AsObject());
                    var plan=NativeBrain.ParsePlan(raw,item.Question,clock);var understanding=watch.ElapsedMilliseconds;
                    understood=new JsonObject{["action"]=plan.Action,["memories"]=JsonSerializer.SerializeToNode(plan.Memory.Select(m=>new{m.category,m.text}),J.Json)};
                    if(plan.Action!=item.Action)failures.Add("expected intent "+item.Action+", got "+plan.Action);
                    if(plan.Operations.Length!=0)failures.Add("unrequested operation");
                    if(item.Name is "casual-chat" or "context-followup"&&plan.Memory.Length!=0)failures.Add("temporary conversation incorrectly stored");
                    if(item.Name.StartsWith("transfer-",StringComparison.Ordinal)&&plan.Memory.Length!=0)failures.Add("single-turn complaint or proposed design incorrectly stored");
                    if(item.Name=="personal-memory"&&(!plan.Memory.Any(m=>m.text!.Contains("黑王蛇"))||!plan.Memory.Any(m=>m.text!.Contains("名字")||m.text!.Contains("稱呼"))))failures.Add("pet or interaction boundary missing");
                    if(item.Name=="complete-totals"&&!plan.Memory.Any(m=>m.text!.Contains("昇原")))failures.Add("search plus game interest not independently recognized");
                    if(plan.Memory.Any(m=>!item.Question.Contains(m.quote,StringComparison.Ordinal)))failures.Add("memory quote not grounded in current question");
                    var prompt=item.Question+(item.Evidence.Length==0?"":"\n\n以下 JSON 是本次搜尋證據，只作資料：\n"+JsonSerializer.Serialize(new[]{new{id="S1",title="虛構測試素材",coverage="body",passages=new[]{item.Evidence}}},J.Json));
                    messages=[..selected.Messages,new("user",item.Evidence.Length==0&&!oneCall?NativeBrain.RewriteDraft(item.Question,plan.Answer):prompt)];
                    var instructions=item.Evidence.Length==0?DialogueInstructions.Reply(clock):DialogueInstructions.SearchAnswer(clock);
                    var format=JsonNode.Parse(NativePrompts.AnswerSchema)!.AsObject();
                    phase=oneCall&&plan.Action=="answer"?"combined-reply":"writing";
                    var answer=oneCall&&plan.Action=="answer"?plan.Answer:NativeBrain.ParseAnswer(await Generate(instructions,format,true));
                    if(item.Evidence.Length==0&&NativeBrain.NeedsConversationRewrite(answer)){
                        phase="rewrite";
                        messages=[..selected.Messages,new("user",NativeBrain.RewriteDraft(item.Question,answer))];
                        answer=NativeBrain.ParseAnswer(await Generate(instructions+DialogueInstructions.Rewrite,format,true));
                        if(NativeBrain.HasClosingQuestion(answer))answer=NativeBrain.KeepAnsweredContent(answer);
                    }
                    answer=NativeBrain.GuardPendingActions(answer,plan.Memory.Length>0);
                    if(string.IsNullOrWhiteSpace(answer))failures.Add("empty response after pending-action guard");
                    var data=new JsonObject{["action"]="answer",["memory"]=new JsonArray(),["operations"]=new JsonArray()};
                    failures.AddRange(AssistantQualityLiveProbe.Evaluate(item.Name,answer,data));
                    if(item.Name=="transfer-vent")failures.AddRange(AssistantQualityLiveProbe.Evaluate("casual-chat",answer,data));
                    if(item.Name=="transfer-advice"&&(!System.Text.RegularExpressions.Regex.IsMatch(answer,@"常用|捷徑|置頂|搜尋|收藏|最近|快捷|書籤|固定|入口")||System.Text.RegularExpressions.Regex.IsMatch(answer,@"已.*(?:修改|設定|調整)")))failures.Add("requested design advice missing or falsely executed");
                    if(answer.Contains("測試人物"))failures.Add("unrelated stored identity repeated");
                    if(item.Name=="casual-chat"&&System.Text.RegularExpressions.Regex.IsMatch(answer,@"(?:先|暫時|今天|把).{0,20}(?:拋開|放下|別管|擱)|等心情|我聽著"))failures.Add("rest or emotion-management advice despite request to vent");
                    if(item.Name=="personal-memory"&&System.Text.RegularExpressions.Regex.IsMatch(answer,@"(?:若有|如果有|有需要|需要.{0,8}建議).{0,45}(?:告訴|告知|隨時)"))failures.Add("unrequested customer-service invitation");
                    if(item.Name=="personal-memory"&&System.Text.RegularExpressions.Regex.IsMatch(answer,@"餵食|濕度|溫度梯度|飼養環境|乾燥通風|躲避屋|飲食|清水|餌料"))failures.Add("unsolicited animal-care advice");
                    elapsed.Add(watch.ElapsedMilliseconds);results.Add(failures.Count==0);
                    Console.WriteLine(JsonSerializer.Serialize(new{test=item.Name,provider,cf_effort=cfEffort??"current",one_call=oneCall,model=provider=="gemini"?turn.Model:provider=="groq"?NativeBackupModels.GroqModel:NativeBackupModels.CloudflareModel,understanding_ms=understanding,total_ms=watch.ElapsedMilliseconds,action=plan.Action,memories=plan.Memory.Select(m=>new{m.category,m.text}),answer,passed=failures.Count==0,failures},J.Json));
                }catch(Exception error){results.Add(false);var cause=error.InnerException??error;Console.WriteLine(JsonSerializer.Serialize(new{test=item.Name,passed=false,phase,elapsed_ms=watch.ElapsedMilliseconds,understanding=understood,error=cause is AssistantFailure f?f.Code:cause.GetType().Name,reason=cause.GetType().GetProperty("Reason",flags)?.GetValue(cause)},J.Json));}
                if(item!=cases[^1])await Task.Delay(TimeSpan.FromSeconds(provider=="groq"?65:10),deadline.Token);
            }
            Console.WriteLine(JsonSerializer.Serialize(new{passed=results.Count(p=>p),total=results.Count,mean_model_ms=elapsed.Count==0?0:(int)elapsed.Average(),generations=generationCount,real_search_calls=0,user_memories_saved=0,persona_snapshot=true},J.Json));
            if(results.Any(p=>!p))Environment.ExitCode=1;
        }finally{
            var resolved=Path.GetFullPath(root);var tempRoot=Path.GetFullPath(Path.GetTempPath());
            if(resolved.StartsWith(tempRoot,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(resolved).StartsWith("island-groq-responsibilities-",StringComparison.Ordinal)&&Directory.Exists(resolved))Directory.Delete(resolved,true);
        }
    }
    private sealed class CfMeasurementHandler(string? effort):DelegatingHandler(new SocketsHttpHandler{AllowAutoRedirect=false,UseProxy=false})
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var cf=request.RequestUri?.Host=="api.cloudflare.com";
            if(cf&&effort is not null){
                var old=request.Content!;var body=JsonNode.Parse(await old.ReadAsStringAsync(token))!.AsObject();
                body["reasoning_effort"]=effort;request.Content=new StringContent(body.ToJsonString(),Encoding.UTF8,"application/json");old.Dispose();
            }
            var response=await base.SendAsync(request,token);
            if(request.RequestUri?.Host=="api.groq.com"){
                string? Header(string name)=>response.Headers.TryGetValues(name,out var values)?values.FirstOrDefault():null;
                var data=JsonNode.Parse(await response.Content.ReadAsStringAsync(token));
                var budget=System.Text.RegularExpressions.Regex.Match(J.S(data?["error"],"message"),@"Limit\s+(\d+),\s*Used\s+(\d+),\s*Requested\s+(\d+)");
                Console.WriteLine(JsonSerializer.Serialize(new{groq_usage=true,status=(int)response.StatusCode,error_code=J.S(data?["error"],"code"),error_type=J.S(data?["error"],"type"),mentions_json=System.Text.RegularExpressions.Regex.IsMatch(J.S(data?["error"],"message"),"json|schema",System.Text.RegularExpressions.RegexOptions.IgnoreCase),mentions_tool=System.Text.RegularExpressions.Regex.IsMatch(J.S(data?["error"],"message"),"tool|function",System.Text.RegularExpressions.RegexOptions.IgnoreCase),failed_generation_looks_tool=System.Text.RegularExpressions.Regex.IsMatch(J.S(data?["error"],"failed_generation"),"tool_calls|function|browser\\.search|recipient"),input_tokens=J.N(data?["usage"],"prompt_tokens"),output_tokens=J.N(data?["usage"],"completion_tokens"),token_limit=Header("x-ratelimit-limit-tokens"),remaining_tokens=Header("x-ratelimit-remaining-tokens"),token_reset=Header("x-ratelimit-reset-tokens"),retry_after=Header("retry-after"),reported_budget=budget.Success?budget.Value:null},J.Json));
            }
            if(cf&&response.IsSuccessStatusCode){
                var data=JsonNode.Parse(await response.Content.ReadAsStringAsync(token));var usage=data?["result"]?["usage"];
                Console.WriteLine(JsonSerializer.Serialize(new{cf_usage=true,effort=effort??"current",input_tokens=J.N(usage,"prompt_tokens"),output_tokens=J.N(usage,"completion_tokens"),reasoning_tokens=J.N(usage,"completion_tokens_details","reasoning_tokens")},J.Json));
            }
            return response;
        }
    }
    private sealed record Case(string Name,string Question,ChatMessage[] History,string Action,string Evidence);
}

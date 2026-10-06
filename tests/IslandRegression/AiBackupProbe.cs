using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using EndfieldChargePlus.Assistant;
using EndfieldChargePlus.Assistant.Native;

internal static class AiBackupProbe
{
    internal const string GroqKey="gsk_SyntheticNeverSentToNetwork000000000000";
    internal const string CfKey="SyntheticCloudflareTokenNeverSent00000000";
    internal const string Account="0123456789abcdef0123456789abcdef";
    internal static string Root()=>Path.Combine(Path.GetTempPath(),"island-backup-"+Guid.NewGuid().ToString("N"));
    private static JsonObject SimpleSchema()=>JsonNode.Parse("""{"type":"object","properties":{"answer":{"type":"string"}},"required":["answer"],"additionalProperties":false}""")!.AsObject();
    private static HttpResponseMessage Json(string body,HttpStatusCode code=HttpStatusCode.OK)=>new(code){Content=new StringContent(body,Encoding.UTF8,"application/json")};
    private static HttpResponseMessage Result(string id,string text)=>Json(id=="groq"?new JsonObject{["choices"]=new JsonArray(new JsonObject{["message"]=new JsonObject{["content"]=text}}),["usage"]=new JsonObject{["total_tokens"]=12}}.ToJsonString():new JsonObject{["success"]=true,["result"]=new JsonObject{["response"]=text,["usage"]=new JsonObject{["prompt_tokens"]=8,["completion_tokens"]=4}}}.ToJsonString());
    internal sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>send(request,token);}
    internal static async Task RunAsync()
    {
        var count=0;
        void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);count++;Console.WriteLine("PASS: "+name);}
        async Task Expect(Func<Task> call,string code){try{await call();throw new Exception("FAIL: expected "+code);}catch(AssistantFailure error){Check(error.Code==code,code);}}
        var root=Root();var now=DateTimeOffset.Parse("2026-10-06T09:00:00+08:00").ToUnixTimeMilliseconds();
        var routes=new List<string>();var groqResponse="ok";var cfQuota=false;
        using var http=new HttpClient(new Handler(async(request,token)=>{
            var id=request.RequestUri!.Host=="api.groq.com"?"groq":"cloudflare";routes.Add(id);
            Check(request.Method==HttpMethod.Post&&!request.RequestUri.AbsoluteUri.Contains(GroqKey)&&!request.RequestUri.AbsoluteUri.Contains(CfKey),"fixed POST endpoints never expose keys in URL");
            Check(request.Headers.Authorization?.Parameter==(id=="groq"?GroqKey:CfKey),"provider receives only its own bearer credential");
            var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!;
            Check(J.S(body["messages"]![0],"role")=="system"&&J.S(body["messages"]![1],"role")=="user","trusted instructions stay separate from user text");
            if(id=="groq"){
                Check(J.S(body,"model")==NativeBackupModels.GroqModel&&J.B(body["response_format"]!["json_schema"],"strict"),"Groq uses GPT-OSS 120B with strict JSON schema");
                if(groqResponse=="rate"){
                    var response=Json("{\"error\":{\"message\":\"synthetic quota\"}}",HttpStatusCode.TooManyRequests);
                    response.Headers.TryAddWithoutValidation("x-ratelimit-remaining-requests","0");response.Headers.TryAddWithoutValidation("x-ratelimit-reset-requests","2h3m1s");return response;
                }
                if(groqResponse=="auth")return Json("{}",HttpStatusCode.Unauthorized);
                if(groqResponse=="bad")return Result(id,"{\"answer\":true}");
            }else{
                Check(request.RequestUri.AbsolutePath.Contains(Account+"/ai/run/"+NativeBackupModels.CloudflareModel)&&J.S(body["response_format"],"type")=="json_schema"&&J.B(body["response_format"]?["json_schema"],"strict")&&body["chat_template_kwargs"]?["enable_thinking"]?.GetValue<bool>()==false,"Cloudflare uses Qwen3.8 account REST API, instruct mode and strict response schema");
                if(cfQuota)return Json("{\"success\":false,\"errors\":[{\"code\":3036}]}",HttpStatusCode.TooManyRequests);
            }
            return Result(id,id=="cloudflare"?"<think></think>\n```json\n{\"answer\":\"OK\"}\n```":"{\"answer\":\"OK\"}");
        }));
        var backups=new NativeBackupModels(root,http,()=>now);
        await Expect(()=>Task.Run(()=>backups.Configure(Account,CfKey,GroqKey,true,false)),"backup_free_confirmation_required");
        Check(!File.Exists(Path.Combine(root,"data","assistant-backup-keys.dpapi")),"unconfirmed save writes no credentials");
        backups.Configure(Account,CfKey,GroqKey,true,true);
        Check(routes.Count==0&&backups.Status().All(p=>p.Configured&&p.Requests==0),"saving credentials makes no HTTP request");
        var encrypted=Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root,"data","assistant-backup-keys.dpapi")));
        Check(!encrypted.Contains(GroqKey)&&!encrypted.Contains(CfKey)&&!encrypted.Contains(Account),"all provider credentials encrypted with Windows DPAPI");
        var reply=await backups.GenerateAsync([new("user","你好")],"test",SimpleSchema(),default);
        Check(routes.SequenceEqual(new[]{"groq"})&&reply.Contains("OK")&&backups.LastModel==NativeBackupModels.GroqModel,"Groq gets first backup request; Cloudflare untouched on success");
        Check(backups.Status().First().Requests==1&&backups.Status().First().Tokens==12,"provider usage counts reported tokens and each attempt");
        groqResponse="rate";routes.Clear();await backups.GenerateAsync([new("user","下一題")],"test",SimpleSchema(),default);
        Check(routes.SequenceEqual(new[]{"groq","cloudflare"})&&backups.LastModel==NativeBackupModels.CloudflareModel,"Groq 429 automatically falls through to Cloudflare");
        var retry=backups.Status().First().RetryAt;
        Check(retry==now+TimeSpan.FromHours(2).TotalMilliseconds+181000,"Groq request reset header determines cooldown");
        backups.Configure("","","",true,true);Check(backups.Status().First().RetryAt==retry&&backups.Status().First().Requests==2,"blank save retains credentials, cooldown and counters");
        var reloaded=new NativeBackupModels(root,http,()=>now);routes.Clear();await reloaded.GenerateAsync([new("user","新題")],"test",SimpleSchema(),default);
        Check(routes.SequenceEqual(new[]{"cloudflare"}),"cooldown survives restart and skips unavailable Groq");
        cfQuota=true;routes.Clear();await Expect(()=>reloaded.GenerateAsync([new("user","新題")],"test",SimpleSchema(),default),"backup_models_unavailable");
        var cf=reloaded.Status().Single(p=>p.Id=="cloudflare");
        Check(cf.Reason=="daily_quota"&&DateTimeOffset.FromUnixTimeMilliseconds(cf.RetryAt).ToOffset(TimeSpan.FromHours(8)).Hour==8,"Cloudflare daily quota resets at UTC midnight, Taipei 08:00");
        now=cf.RetryAt+1;groqResponse="ok";cfQuota=false;routes.Clear();await reloaded.GenerateAsync([new("user","恢復")],"test",SimpleSchema(),default);
        Check(routes.SequenceEqual(new[]{"groq"})&&reloaded.Status().First().Reason=="ready","time advancing automatically restores the preferred backup");
        groqResponse="bad";routes.Clear();await reloaded.GenerateAsync([new("user","驗證")],"test",SimpleSchema(),default);
        Check(routes.SequenceEqual(new[]{"groq","cloudflare"})&&reloaded.Status().First().Reason=="invalid_response","malformed structured output switches provider before memory or reminders");
        now+=61000;groqResponse="auth";routes.Clear();await reloaded.GenerateAsync([new("user","權限")],"test",SimpleSchema(),default);
        now+=86400000;routes.Clear();await reloaded.GenerateAsync([new("user","明天")],"test",SimpleSchema(),default);
        Check(routes.SequenceEqual(new[]{"cloudflare"})&&reloaded.Status().First().Reason=="auth","bad credentials stay paused instead of retrying every conversation");
        reloaded.Configure("","",GroqKey+"1",true,true);Check(reloaded.Status().First().Reason=="ready"&&reloaded.Status().First().Requests>0,"replacing credentials clears auth failure without resetting usage");
        reloaded.Configure("","","",false,true);var before=routes.Count;await Expect(()=>reloaded.GenerateAsync([new("user","停用")],"test",SimpleSchema(),default),"backup_models_unavailable");Check(routes.Count==before,"disabled backup sends no requests");
        Check(PersonalMemoryFilter.ContainsSecret(GroqKey),"Groq key shapes excluded from personal memory");
        await ServiceFlowAsync(Check);
        await CancellationAsync(Check);
        await ProviderLimitsAsync(Check);
        await ConfigurationFailureAsync(Check);
        var corrupt=Root();Directory.CreateDirectory(Path.Combine(corrupt,"data"));var corruptPath=Path.Combine(corrupt,"data","assistant-backup-keys.dpapi");File.WriteAllBytes(corruptPath,[1,2,3]);
        var broken=new NativeBackupModels(corrupt,http);await Expect(()=>Task.Run(()=>broken.Configure(Account,CfKey,GroqKey,true,true)),"backup_storage_error");
        Check(File.ReadAllBytes(corruptPath).SequenceEqual(new byte[]{1,2,3}),"corrupt credential file preserved without overwrite");
        Console.WriteLine($"{count}/{count} native AI backup checks PASS; synthetic HTTP only, zero external calls.");
    }
    private static async Task CancellationAsync(Action<bool,string> check)
    {
        var calls=0;using var http=new HttpClient(new Handler(async(_,token)=>{calls++;await Task.Delay(5000,token);return Json("{}");}));
        var backups=new NativeBackupModels(Root(),http);backups.Configure(Account,CfKey,GroqKey,true,true);
        using var cancel=new CancellationTokenSource(50);
        try{await backups.GenerateAsync([new("user","cancel")],"",SimpleSchema(),cancel.Token);throw new Exception("FAIL: cancellation not honored");}catch(OperationCanceledException){}
        check(calls==1&&backups.Status().All(p=>p.Reason=="ready"),"caller cancellation sends no fallback and does not falsely pause provider");
    }
    private static async Task ProviderLimitsAsync(Action<bool,string> check)
    {
        var now=DateTimeOffset.Parse("2026-10-06T14:00:00+08:00").ToUnixTimeMilliseconds();
        var policy=NativeConfiguration.Confirm(NativeConfiguration.InitialPolicy(),"AIzaSyntheticNeverSent00000000000000000",true,DateTimeOffset.FromUnixTimeMilliseconds(now).AddMinutes(-1));
        using var ledger=new GeminiLedger(":memory:",policy,now:()=>now,providerManagedLimits:true);
        var usage=JsonNode.Parse("""{"promptTokenCount":40,"candidatesTokenCount":20,"totalTokenCount":60}""");
        for(var i=0;i<25;i++){ledger.Preflight();ledger.Finish(ledger.Reserve(40),usage);}
        var status=ledger.Status(true);
        check(J.N(status["models"]![ledger.Model],"used_requests_today")==25&&status["daily_local_request_limit"] is null,"production mode exceeds old 20 RPD and 3 RPM caps without local blocking");
        check(status["models"]![ledger.Model]!["remaining_requests_today"] is null,"local ledger does not pretend to know official remaining quota");
        ledger.PreflightFailure(429,23000,false);
        check(J.S(ledger.Status(true)["models"]![ledger.Model],"locked_reason")=="provider_rate_limit"&&J.N(ledger.Status(true)["models"]![ledger.Model],"retry_at")==now+23000,"Gemini minute quota uses actual retry delay rather than a full-day lock");
        now+=23001;ledger.Preflight();var id=ledger.Reserve(40);ledger.Fail(id,429,1000,true);
        var retry=J.N(ledger.Status(true)["models"]![ledger.Model],"retry_at");
        check(DateTimeOffset.FromUnixTimeMilliseconds(retry).ToOffset(TimeSpan.FromHours(8)).Hour==15,"Gemini summer daily reset is midnight Pacific, Taipei 15:00");
        now=retry+1;ledger.Preflight();check(J.S(ledger.Status(true)["models"]![ledger.Model],"locked_reason")=="","Gemini daily quota restores automatically after provider reset");
        var winter=DateTimeOffset.Parse("2026-12-06T09:00:00+08:00").ToUnixTimeMilliseconds();
        check(DateTimeOffset.FromUnixTimeMilliseconds(GeminiLedger.NextReset(winter)).ToOffset(TimeSpan.FromHours(8)).Hour==16,"Gemini winter reset respects Pacific DST, Taipei 16:00");
        using var http=new HttpClient(new Handler((_,_)=>Task.FromResult(Json("""{"error":{"details":[{"@type":"type.googleapis.com/google.rpc.QuotaFailure","violations":[{"quotaMetric":"generativelanguage.googleapis.com/generate_content_free_tier_requests","quotaId":"GenerateRequestsPerDayPerProjectPerModel-FreeTier"}]},{"@type":"type.googleapis.com/google.rpc.RetryInfo","retryDelay":"45s"}]}}""",HttpStatusCode.TooManyRequests))));
        try{await NativeHttp.JsonAsync(http,"https://generativelanguage.googleapis.com/v1beta/test","x-goog-api-key","synthetic",new JsonObject(),default);throw new Exception("FAIL: expected daily quota");}
        catch(ProviderResponseFailure error){check(error.DailyQuota&&error.RetryMs==45000,"Google QuotaFailure and RetryInfo parsed without exposing raw error or credentials");}
        var calls=0;using var groq=new HttpClient(new Handler((request,_)=>{
            calls++;if(request.RequestUri!.Host=="api.cloudflare.com")return Task.FromResult(Result("cloudflare","{\"answer\":\"OK\"}"));
            return Task.FromResult(Json("{\"error\":{\"message\":\"Tokens per day limit reached. Please try again in 4h2m.\"}}",HttpStatusCode.TooManyRequests));
        }));
        var backups=new NativeBackupModels(Root(),groq,()=>now);backups.Configure(Account,CfKey,GroqKey,true,true);
        await backups.GenerateAsync([new("user","quota")],"test",SimpleSchema(),default);
        check(calls==2&&backups.Status().First().RetryAt==now+TimeSpan.FromHours(4).TotalMilliseconds+120000,"Groq daily token error uses its stated retry duration and continues with Cloudflare");
    }
    private static async Task ConfigurationFailureAsync(Action<bool,string> check)
    {
        var root=Root();using var http=new HttpClient(new Handler((_,_)=>throw new Exception("FAIL: configuration issued API request")));
        var backups=new NativeBackupModels(root,http);backups.Configure(Account,CfKey,GroqKey,true,true);
        var keys=Path.Combine(root,"data","assistant-backup-keys.dpapi");var state=Path.Combine(root,"data","assistant-backup-state.json");var previous=File.ReadAllBytes(keys);
        File.Delete(state);Directory.CreateDirectory(state);
        try{backups.Configure("","",GroqKey+"1",true,true);throw new Exception("FAIL: blocked state save succeeded");}catch(IOException){}catch(UnauthorizedAccessException){}
        check(File.ReadAllBytes(keys).SequenceEqual(previous)&&backups.HasConfigured,"state save failure rolls back credentials and preserves the active configuration");
        var damaged=Root();Directory.CreateDirectory(Path.Combine(damaged,"data"));var file=Path.Combine(damaged,"data","assistant-backup-keys.dpapi");
        File.WriteAllBytes(file,System.Security.Cryptography.ProtectedData.Protect(Encoding.UTF8.GetBytes("{\"Groq\":null}"),Encoding.UTF8.GetBytes("Endfield.Assistant.Backups.v1"),System.Security.Cryptography.DataProtectionScope.CurrentUser));
        var invalid=new NativeBackupModels(damaged,http);
        check(invalid.Status().All(s=>s.Reason=="backup_storage_error")&&!invalid.HasConfigured,"invalid encrypted fields produce readable status without crashing the settings window");
    }
    private static async Task ServiceFlowAsync(Action<bool,string> check)
    {
        var root=Root();const string geminiKey="AIzaSyntheticNeverSent00000000000000000";
        var configuration=new NativeConfiguration(root);configuration.Initialize();configuration.Configure(geminiKey,true);
        var primaryOk=false;var calls=new List<string>();var gen=0;var searches=0;
        const string question="我養一條黑王蛇，幫我查飼養資料。";
        string Plan()=>"""{"action":"search","answer":"","search_query":"黑王蛇 飼養 資料","memory":[{"category":"寵物飼養","quote":"我養一條黑王蛇","text":"我養一條黑王蛇","subject":"user","stability":"durable","replace_id":""}],"operations":[]}""";
        string Answer()=>"""{"answer":"根據來源整理飼養資料 [S1]。"}""";
        using var http=new HttpClient(new Handler(async(request,token)=>{
            var host=request.RequestUri!.Host;calls.Add(host);
            if(host=="generativelanguage.googleapis.com"){
                if(request.RequestUri.AbsolutePath.EndsWith(":countTokens"))return Json("{\"totalTokens\":40}");
                if(!primaryOk)return Json("{}",HttpStatusCode.TooManyRequests);
                var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!;
                var writer=body["generationConfig"]?["responseFormat"]?["text"]?["schema"]?["properties"]?.AsObject().Count==1;
                return Json(new JsonObject{["candidates"]=new JsonArray(new JsonObject{["content"]=new JsonObject{["parts"]=new JsonArray(new JsonObject{["text"]=writer?"{\"answer\":\"主力正常\"}":"{\"action\":\"answer\",\"answer\":\"主力正常\",\"search_query\":\"\",\"memory\":[],\"operations\":[]}"})}}),["usageMetadata"]=new JsonObject{["promptTokenCount"]=40,["candidatesTokenCount"]=20,["totalTokenCount"]=60}}.ToJsonString());
            }
            if(host=="api.exa.ai"){
                searches++;var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!;check(J.S(body,"query")=="黑王蛇 飼養 資料","backup planning forwards only the relevant search query");
                return Json("""{"results":[{"title":"飼養資料","url":"https://example.org/care","text":"黑王蛇飼養資料：提供清水、躲藏空間並管理環境。"}]}""");
            }
            check(host=="api.groq.com","service never calls Cloudflare while Groq succeeds");var payload=JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!;
            var system=J.S(payload["messages"]![0],"content");
            if(++gen==2){
                var summarySchema=payload["response_format"]!["json_schema"]!["schema"]!;
                check(summarySchema["properties"]!.AsObject().Count==1&&summarySchema["properties"]!["answer"] is not null&&summarySchema["additionalProperties"]!.GetValue<bool>()==false,
                    "backup summary accepts only answer and cannot create article memories or actions");
            }
            return Result("groq",gen==1?Plan():Answer());
        }));
        using(var service=new NativeAssistantService(root,http,reader:(_,_)=>Task.FromResult<SearchRow?>(null))){
            service.Backups.Configure(Account,CfKey,GroqKey,true,true);
            await service.Search.ConfigureAsync(NativeSearch.DefaultSettings(),new(){["exa"]="exa-synthetic000000"},new HashSet<string>(),default);
            var reply=await service.AskAsync(question,[],"auto",default);
            check(reply.model==NativeBackupModels.GroqModel&&reply.answer_kind=="model"&&reply.search_used&&searches==1&&gen==2,"Gemini 429 switches planning and evidence summary to Groq");
            check(reply.memory_suggestions?.Length==1&&reply.memory_suggestions[0].category=="寵物飼養"&&reply.sources?.Length==1,"search plus user memory preserved through the same backup pipeline");
            check(J.S(service.Usage()["models"]!["gemini-3.5-flash-lite"],"locked_reason").Length>0,"backup does not erase Gemini's quota lock");
        }
        var fresh=Root();var config=new NativeConfiguration(fresh);config.Initialize();config.Configure(geminiKey,true);primaryOk=true;calls.Clear();
        using(var service=new NativeAssistantService(fresh,http)){
            service.Backups.Configure(Account,CfKey,GroqKey,true,true);var reply=await service.AskAsync("你好",[],"auto",default);
            check(reply.text=="主力正常"&&calls.All(h=>h=="generativelanguage.googleapis.com")&&service.Backups.Status().All(p=>p.Requests==0),"healthy Gemini remains first choice without backup API calls");
        }
    }
}

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EndfieldChargePlus.Assistant;
using EndfieldChargePlus.Assistant.Native;

internal static class NativeAssistantProbe
{
    // Explicit manual integration check; excluded from Test.ps1 and all automated suites.
    internal static async Task LiveAsync()
    {
        try {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(55));
            var service=NativeAssistantService.Shared;var before=J.N(service.Usage(),"models","gemini-3.5-flash-lite","used_requests_today");
            var reply=await service.AskAsync("請只回覆：連線正常",[],"chat",timeout.Token);
            var after=J.N(service.Usage(),"models","gemini-3.5-flash-lite","used_requests_today");
            Console.WriteLine(JsonSerializer.Serialize(new{success=reply.answer_kind=="model"&&reply.text.Contains("連線正常"),reply=reply.text,search_used=reply.search_used,model_calls=after-before}));
            if(reply.answer_kind!="model"||!reply.text.Contains("連線正常"))Environment.ExitCode=1;
        }catch(AssistantFailure e){Console.WriteLine(JsonSerializer.Serialize(new{success=false,error=e.Code}));Environment.ExitCode=1;}
        catch(Exception e){Console.WriteLine(JsonSerializer.Serialize(new{success=false,error=e.GetType().Name}));Environment.ExitCode=1;}
        finally{NativeAssistantService.Shutdown();}
    }
    private static int _passed;
    private static void Check(bool v,string name){if(!v)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);_passed++;}
    private static void Fails(Action a,string code){try{a();throw new Exception("Expected "+code);}catch(AssistantFailure e){Check(e.Code==code,code);}}
    private static async Task FailsAsync(Func<Task> a,string name){try{await a();throw new Exception("Expected failure: "+name);}catch(OperationCanceledException){Check(true,name);}catch(SearchFailure){Check(true,name);}catch(AssistantFailure){Check(true,name);}}
    private static string NewRoot()=>Path.Combine(Path.GetTempPath(),"island-native-test-"+Guid.NewGuid().ToString("N"));
    private const string Key="AIzaTestOnlyNeverSentToGoogle1234567890000";
    private static JsonObject Usage()=>JsonNode.Parse("""{"promptTokenCount":40,"candidatesTokenCount":20,"thoughtsTokenCount":0,"totalTokenCount":60}""")!.AsObject();
    private static JsonObject Policy()=>NativeConfiguration.Confirm(NativeConfiguration.InitialPolicy(),Key,true,DateTimeOffset.UtcNow.AddMinutes(-5));
    private static string Response(string text)=>new JsonObject{["candidates"]=new JsonArray(new JsonObject{["content"]=new JsonObject{["parts"]=new JsonArray(new JsonObject{["text"]=text})}}),["usageMetadata"]=Usage()}.ToJsonString();
    internal static async Task RunAsync()
    {
        var root=NewRoot();var configuration=new NativeConfiguration(root);configuration.Initialize();
        configuration.Configure(Key,true);Check(configuration.LoadKey()==Key&&NativeConfiguration.KeyValid(Key,configuration.Policy()),"DPAPI save/load and pinned policy");
        Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root,"data","gemini-key.dpapi"))).Contains(Key),"credential never saved as plaintext");
        var now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using(var ledger=new GeminiLedger(configuration.LedgerPath,configuration.Policy(),now:()=>now)){
            ledger.Preflight();var id=ledger.Reserve(40);Check(ledger.Finish(id,Usage()),"reserve then reconcile actual tokens");
            Check(J.N(ledger.Status(true),"models","gemini-3.5-flash-lite","used_requests_today")==1,"ledger counts completed requests");
        }
        configuration.Configure(Key,true);
        using(var reopened=new GeminiLedger(configuration.LedgerPath,configuration.Policy(),now:()=>now+1000))Check(J.N(reopened.Status(true),"models","gemini-3.5-flash-lite","used_requests_today")==1,"key confirmation and reopen preserve usage");
        using(var ledger=new GeminiLedger(":memory:",Policy(),now:()=>now)){
            var id=ledger.Reserve(40);Check(!ledger.Finish(id,null),"missing usage fails closed");Fails(ledger.Preflight,"unknown_usage_lock");
        }
        using(var ledger=new GeminiLedger(":memory:",Policy(),now:()=>now)){
            var id=ledger.Reserve(40);ledger.Fail(id,400);ledger.Preflight();
            Check(J.N(ledger.Status(true),"accounted_tokens_today")==0&&J.N(ledger.Status(true),"models","gemini-3.5-flash-lite","used_requests_today")==1,"known rejected HTTP 400 preserves request counter without unknown-usage lock");
        }
        using(var ledger=new GeminiLedger(":memory:",Policy(),now:()=>now)){
            var id=ledger.Reserve(40);ledger.Fail(id,503);Fails(ledger.Preflight,"provider_temporary_unavailable");now+=61000;ledger.Preflight();Check(true,"temporary failure recovers after cooldown");
        }
        using(var ledger=new GeminiLedger(":memory:",Policy(),now:()=>now)){
            for(var i=0;i<3;i++){var id=ledger.Reserve(40);ledger.Finish(id,Usage());}Fails(ledger.Preflight,"minute_request_limit");
        }
        using(var ledger=new GeminiLedger(":memory:",Policy(),now:()=>now)){
            var id=ledger.Reserve(40);var huge=Usage();huge["totalTokenCount"]=long.MaxValue;Check(!ledger.Finish(id,huge),"unsafe token metadata never overflows ledger");
        }
        var clock=NativeBrain.Clock(DateTimeOffset.Parse("2026-10-05T18:00:00Z"));Check(NativeBrain.ResolveDate("明天節日","明天是什麼節日",clock).Contains("2026-10-07"),"Taipei relative dates use trusted clock");
        Check(NativePrompts.Planning.Contains("財報")&&NativePrompts.Memory.Contains("不需要使用者說"),"AI instructions cover fresh claims and implicit personal facts");
        Check(NativeBrain.ParseAnswer("{\"answer\":\"  直接答覆  \"}")=="直接答覆","final response accepts plain answer text");
        Fails(()=>NativeBrain.ParseAnswer("{\"answer\":\" \"}"),"invalid_assistant_response");
        Fails(()=>NativeBrain.ParseAnswer("{\"answer\":\"完成\",\"operations\":[{\"kind\":\"delete_memory\"}]}"),"invalid_assistant_response");
        Check(NativeBrain.HasClosingQuestion("直接聊天不用搜尋。你想聊什麼？")&&NativeBrain.KeepAnsweredContent("直接聊天不用搜尋。你想聊什麼？")=="直接聊天不用搜尋。","closing follow-up can be removed without losing the answer");
        Check(NativeBrain.KeepAnsweredContent("可以直接聊。你想談什麼？最近如何？")=="可以直接聊。"&&NativeBrain.KeepAnsweredContent("你想談什麼？")=="","multiple trailing questions never become an empty successful answer");
        JsonObject M(string category,string quote)=>new(){["answer"]="我了解這項資訊。",["memory"]=new JsonArray(new JsonObject{["category"]=category,["quote"]=quote,["text"]=quote,["subject"]="user",["stability"]="durable",["replace_id"]=""})};
        Check(NativeBrain.ParseMemory(M("個人事項","我养一隻黑王蛇"),"我养一隻黑王蛇").Memory.Length==1,"explicit pet self-description classified");
        Check(NativeBrain.ParseMemory(M("回答方式","不需要每次回答都叫我名字"),"不需要每次回答都叫我名字").Memory.Length==1,"interaction preference classified");
        Check(NativeBrain.ParseMemory(new JsonObject{["answer"]="不保存這段假設。",["memory"]=new JsonArray()},"我可能是世界首富").Memory.Length==0,"AI no-memory decision is preserved without local keyword overrides");
        Check(NativeBrain.ParseMemory(M("身分資料","我叫別人"),"記住").Memory.Length==0,"memory cannot be taken from old history");
        Check(NativeBrain.ParseMemory(M("個人事項","我的 Key 是 sk-this-is-only-a-test-key-never-used"),"我的 Key 是 sk-this-is-only-a-test-key-never-used").Memory.Length==0,"actual credential shapes excluded");
        Check(NativeBrain.GuardCitations("[S9] https://wrong.example 85折（付原價15%）",[new("S1","source","https://valid.example",null,null,null)],true).Contains("連結還不能確認"),"unverified sources and discount claims guarded");
        await GeminiFlowAsync();await ReplyFlowAsync();await EvidenceFlowAsync();await SearchFlowAsync();
        _passed += await AiPersonalIntentProbe.RunAsync();
        _passed+=await MonthlySearchRetryProbe.RunAsync();
        _passed+=await SearchTurnRotationProbe.RunAsync();
        _passed+=await FirecrawlBillingProbe.RunAsync();
        Console.WriteLine($"{_passed}/{_passed} native assistant checks PASS");
    }
    private static async Task GeminiFlowAsync()
    {
        var root=NewRoot();var c=new NativeConfiguration(root);c.Initialize();c.Configure(Key,true);
        var calls=new List<string>();var current="";
        using var http=new HttpClient(new MockHandler(async(request,token)=>{
            Check(request.RequestUri!.Host=="generativelanguage.googleapis.com"&&!request.RequestUri.Query.Contains(Key),"Gemini key stays out of URL");
            calls.Add(request.RequestUri.AbsolutePath);var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!;
            if(request.RequestUri.AbsolutePath.EndsWith(":countTokens"))return Json("{\"totalTokens\":40}");
            current=J.S(body["contents"]!.AsArray()[^1]!["parts"]![0],"text");
            var writer=body["generationConfig"]?["responseFormat"]?["text"]?["schema"]?["properties"]?.AsObject().Count==1;
            return Json(Response(writer?"{\"answer\":\"您好。\"}":new JsonObject{["action"]="answer",["answer"]="您好。",["search_query"]="",["memory"]=null}.ToJsonString()));
        }));
        using(var service=new NativeAssistantService(root,http,reader:(_,_)=>Task.FromResult<SearchRow?>(null))){
            var reply=await service.AskAsync("你好",[],"auto",default);Check(reply.text=="您好。"&&!reply.search_used&&calls.Count==4,"ordinary chat separates understanding and reply, no search");
            Check(current.StartsWith("你好\n\n以下是程式提供的答覆草稿，不是新的使用者資訊：\n",StringComparison.Ordinal)&&J.S(JsonNode.Parse(current.Split('\n')[^1]),"draft")=="您好。","original question and understood draft remain data, not new instructions or memories");
        }
        var missing=NewRoot();using(var service=new NativeAssistantService(missing,http)){
            await FailsAsync(()=>service.AskAsync("目前新聞",[],"auto",default),"auto cannot decide or send private text to search without AI");
            Check(calls.Count==4,"missing AI key dispatches no request");
        }
        using(var ledger=new GeminiLedger(":memory:",Policy())){
            var id=ledger.Reserve(40);ledger.Fail(id,429);var gemini=new NativeGemini(ledger,Key,http);
            await FailsAsync(()=>gemini.GenerateAsync([new("user","測試")],"",null,null,default),"quota lock prevents all outbound requests");Check(calls.Count==4,"blocked request does not even count tokens");
        }
        // Cancellation after dispatch remains accounted and locks uncertain usage.
        using(var ledger=new GeminiLedger(":memory:",Policy())){
            using var slow=new HttpClient(new MockHandler(async(r,t)=>{if(r.RequestUri!.AbsolutePath.EndsWith(":countTokens"))return Json("{\"totalTokens\":40}");await Task.Delay(1000,t);return Json("{}");}));
            var gemini=new NativeGemini(ledger,Key,slow);using var cancel=new CancellationTokenSource(100);
            await FailsAsync(()=>gemini.GenerateAsync([new("user","取消測試")],"",null,null,cancel.Token),"cancel generation");Fails(ledger.Preflight,"unknown_usage_lock");
        }
    }
    private static async Task ReplyFlowAsync()
    {
        var root=NewRoot();var config=new NativeConfiguration(root);config.Initialize();config.Configure(Key,true);
        var generates=0;var clarify=false;var rewritten="";
        using var http=new HttpClient(new MockHandler(async(request,token)=>{
            var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!;
            if(request.RequestUri!.AbsolutePath.EndsWith(":countTokens"))return Json("{\"totalTokens\":40}");
            generates++;
            if(clarify)return Json(Response("""{"action":"clarify","answer":"你指哪個版本？","search_query":"","memory":[],"operations":[]}"""));
            if(generates==1)return Json(Response("""{"action":"answer","answer":"理解草稿","search_query":"","memory":[{"category":"寵物飼養","quote":"我養黑王蛇","text":"我養黑王蛇","subject":"user","stability":"durable","replace_id":""}],"operations":[]}"""));
            if(generates==2)return Json(Response("""{"answer":"養黑王蛇確實很有特色。想再說說嗎？"}"""));
            rewritten=J.S(body["contents"]!.AsArray()[^1]!["parts"]![0],"text");
            return Json(Response("""{"answer":"養黑王蛇確實很有特色。"}"""));
        }));
        using var service=new NativeAssistantService(root,http);
        var reply=await service.AskAsync("我養黑王蛇，先聊這件事。",[],"auto",default);
        Check(generates==3&&reply.text=="養黑王蛇確實很有特色。"&&!reply.search_used,"unwanted follow-up is revised once without a search");
        Check(reply.memory_suggestions?.Length==1&&reply.memory_suggestions[0].text=="我養黑王蛇"&&rewritten.Contains("不是新的使用者資訊"),"rewriting cannot replace the understood personal memory");
        clarify=true;var before=generates;reply=await service.AskAsync("這個是哪個版本",[],"auto",default);
        Check(generates==before+1&&reply.text=="你指哪個版本？","genuine necessary clarification bypasses the answer rewrite");
    }
    private static async Task SearchFlowAsync()
    {
        var root=NewRoot();var time=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();var routes=new List<string>();
        using var http=new HttpClient(new MockHandler((request,token)=>{
            var url=request.RequestUri!;routes.Add(url.Host+url.AbsolutePath);
            if(url.Host=="api.exa.ai")return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PaymentRequired));
            if(url.AbsolutePath=="/usage")return Task.FromResult(Json("""{"account":{"plan_usage":2,"plan_limit":1000,"paygo_limit":0,"paygo_usage":0},"key":{"usage":2,"limit":1000}}"""));
            if(url.AbsolutePath=="/v2/team/credit-usage")return Task.FromResult(Json(new JsonObject{["success"]=true,["data"]=new JsonObject{["planCredits"]=1000,["remainingCredits"]=999,["billingPeriodStart"]=DateTimeOffset.FromUnixTimeMilliseconds(time).AddDays(-1).ToString("O"),["billingPeriodEnd"]=DateTimeOffset.FromUnixTimeMilliseconds(time).AddDays(29).ToString("O")}}.ToJsonString()));
            if(url.Host=="api.tavily.com")return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
            return Task.FromResult(Json("""{"success":true,"data":{"web":[{"title":"官方文件","url":"https://example.com/docs","description":"mock evidence for native runtime"}]},"creditsUsed":2}"""));
        }));
        using(var search=new NativeSearch(root,http,()=>time,(_,_)=>Task.FromResult<SearchRow?>(null))){
            await search.ConfigureAsync(NativeSearch.DefaultSettings(),new(){["exa"]="exa-test-key0000",["tavily"]="tvly-test-key0000",["firecrawl"]="fc-test-key0000"},new HashSet<string>(),default);
            var reply=await search.SearchAsync("終末地公開文件",default);Check(reply.provider_id=="firecrawl"&&reply.results.Length==1,"Exa 402 and Tavily 429 rotate to Firecrawl");
            var status=search.Status();Check(J.S(status["providers"]![0],"reason")=="exa_failed","Exa failure lock persists until next month's retry time");
            Check(J.N(status["providers"]![2],"used")==2,"Firecrawl reconciles credits conservatively");
            var count=routes.Count;await search.SearchAsync("終末地公開文件",default);Check(routes.Count==count,"cache avoids duplicate paid searches");
            await search.SearchAsync("另一份文件",default);Check(routes.Count(x=>x.StartsWith("api.exa.ai"))==1,"Exa is not retried after budget lock");
            var raw=File.ReadAllText(Path.Combine(root,"search-secrets.dpapi"));Check(!raw.Contains("test-key"),"search keys encrypted with DPAPI");
        }
        using(var reopened=new NativeSearch(root,http,()=>time,(_,_)=>Task.FromResult<SearchRow?>(null))){
            var count=routes.Count;await reopened.SearchAsync("終末地公開文件",default);Check(routes.Count==count,"search cache and state survive restart");
        }
        using(var other=new NativeSearch(NewRoot(),http))Check(!J.B(other.Status(),"configured"),"other application store has no keys or counters");
        Check(!PublicPageReader.Public(IPAddress.Parse("127.0.0.1"))&&!PublicPageReader.Public(IPAddress.Parse("192.168.1.1"))&&!PublicPageReader.Public(IPAddress.Parse("::1"))&&PublicPageReader.Public(IPAddress.Parse("8.8.8.8")),"page reader excludes local and private IP ranges");
    }
    private static async Task EvidenceFlowAsync()
    {
        var root=NewRoot();var config=new NativeConfiguration(root);config.Initialize();config.Configure(Key,true);var generate=0;var search=0;var query="";
        using var http=new HttpClient(new MockHandler(async(r,t)=>{
            var body=JsonNode.Parse(await r.Content!.ReadAsStringAsync(t))!;
            if(r.RequestUri!.Host=="api.exa.ai"){
                search++;query=J.S(body,"query");return Json("""{"results":[{"title":"官方公告","url":"https://example.com/news","text":"This is the mock announcement evidence for the requested date."}],"costDollars":{"total":0.005}}""");
            }
            if(r.RequestUri.AbsolutePath.EndsWith(":countTokens"))return Json("{\"totalTokens\":40}");
            generate++;
            return generate==1?Json(Response("""{"action":"search","answer":"","search_query":"《明日方舟：終末地》 明天活動 公告","memory":null}""")):
                Json(Response("""{"answer":"來源記載公告 [S1]，另有 [S9] https://fake.example 。"}"""));
        }));
        using var service=new NativeAssistantService(root,http,reader:(_,_)=>Task.FromResult<SearchRow?>(null));
        await service.Search.ConfigureAsync(NativeSearch.DefaultSettings(),new(){["exa"]="exa-test-key0000"},new HashSet<string>(),default);
        var result=await service.AskAsync("終末地明天有什麼活動",[],"auto",default);
        Check(generate==2&&search==1&&result.answer_kind=="model","search uses one plan and one evidence summary");
        Check(query.Contains("《明日方舟：終末地》")&&!query.Contains("明天"),"planner subject preserved and relative date resolved");
        Check(result.sources?.Length==1&&result.text.Contains("這點還不能確定")&&result.text.Contains("連結還不能確認"),"unsupported references and invented links cannot look verified");
        Check(!result.text.Contains("[S1]")&&!result.text.Contains("以下整理來源記載"),"natural reply hides citation IDs and does not prepend a report template");
        Check(result.memory_suggestions?.Length==0,"search articles do not become personal memory");
        var before=generate;var evidence=await service.AskAsync("另一項公開文件",[],"web",default);Check(generate==before&&evidence.answer_kind=="evidence","search-only never calls Gemini");
        Fails(()=>NativeBrain.ValidateImage(new("image/png","invalid")),"invalid_image");
        var bytes=new byte[33];new byte[]{137,80,78,71,13,10,26,10}.CopyTo(bytes,0);Encoding.ASCII.GetBytes("IHDR").CopyTo(bytes,12);bytes[18]=32;bytes[22]=32;
        Fails(()=>NativeBrain.ValidateImage(new("image/png",Convert.ToBase64String(bytes))),"image_dimensions_limit");
    }
    private static HttpResponseMessage Json(string body)=>new(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/json")};
    private sealed class MockHandler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>send(request,cancellationToken);
    }
}
